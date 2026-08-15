using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

static class ResponsesWebSocketCompatibility
{
    private const int MaximumMessageBytes = 4 * 1024 * 1024;

    public static async Task HandleAsync(
        HttpContext context,
        IHttpClientFactory httpClientFactory,
        ModelRegistry registry,
        IReadOnlyDictionary<string, string> extraHeaders,
        bool logTraffic,
        string responsesMode)
    {
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var requestStates = new Dictionary<string, JsonObject>(StringComparer.Ordinal);

        try
        {
            while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
            {
                var message = await ReceiveTextAsync(socket, context.RequestAborted);
                if (message is null)
                {
                    break;
                }

                if (!TryParseCreateEvent(message, out var request, out var error))
                {
                    await SendErrorAsync(socket, error!, context.RequestAborted);
                    continue;
                }

                var isPrewarm = IsPrewarm(request!);
                var resolvedRequest = ResolveRequest(request!, requestStates, out var resolveError);
                if (resolvedRequest is null)
                {
                    await SendErrorAsync(socket, resolveError!, context.RequestAborted);
                    continue;
                }

                var responseId = $"resp_{Guid.NewGuid():N}";
                if (isPrewarm)
                {
                    requestStates[responseId] = resolvedRequest.DeepClone().AsObject();
                    await SendLifecycleAsync(socket, CreateResponse(responseId, resolvedRequest, "completed"), context.RequestAborted);
                    continue;
                }

                var model = resolvedRequest["model"]?.GetValue<string>();
                var endpoint = registry.GetCandidates(model).FirstOrDefault();
                if (endpoint is null)
                {
                    await SendErrorAsync(socket, "没有可用的上游 Endpoint。", context.RequestAborted, "no_upstream", 503);
                    continue;
                }

                var response = await SendHttpRequestAsync(context, httpClientFactory, endpoint, extraHeaders, logTraffic, responsesMode, resolvedRequest);
                if (response is null)
                {
                    await SendErrorAsync(socket, "上游未返回有效的 Responses 数据。", context.RequestAborted, "upstream_error", 502);
                    continue;
                }

                response["id"] ??= responseId;
                requestStates[response["id"]!.GetValue<string>()] = resolvedRequest.DeepClone().AsObject();
                await SendLifecycleAsync(socket, response, context.RequestAborted);
                registry.MarkSuccessful(model, endpoint);
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
        catch (WebSocketException) when (socket.State is WebSocketState.Aborted or WebSocketState.Closed)
        {
        }
        finally
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }
        }
    }

    internal static bool TryParseCreateEvent(string payload, out JsonObject? request, out string? error)
    {
        request = null;
        error = null;
        try
        {
            var eventObject = JsonNode.Parse(payload) as JsonObject;
            if (eventObject is null || eventObject["type"]?.GetValue<string>() != "response.create")
            {
                error = "WebSocket 仅支持 type 为 response.create 的事件。";
                return false;
            }

            eventObject.Remove("type");
            request = eventObject;
            return true;
        }
        catch (JsonException)
        {
            error = "WebSocket 消息不是有效的 JSON 对象。";
            return false;
        }
    }

    private static JsonObject? ResolveRequest(JsonObject request, IReadOnlyDictionary<string, JsonObject> states, out string? error)
    {
        error = null;
        var previousResponseId = request["previous_response_id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(previousResponseId))
        {
            request.Remove("generate");
            request.Remove("stream");
            request.Remove("background");
            return request;
        }

        if (!states.TryGetValue(previousResponseId, out var previous))
        {
            error = "Previous response is not available on this WebSocket connection.";
            return null;
        }

        var merged = previous.DeepClone().AsObject();
        foreach (var (name, value) in request)
        {
            if (name is "previous_response_id" or "generate" or "stream" or "background")
            {
                continue;
            }
            if (name == "input" && merged["input"] is JsonArray previousInput && value is JsonArray nextInput)
            {
                foreach (var item in nextInput)
                {
                    previousInput.Add(item?.DeepClone());
                }
                continue;
            }

            merged[name] = value?.DeepClone();
        }
        return merged;
    }

    private static bool IsPrewarm(JsonObject request) => request["generate"]?.GetValue<bool>() == false;

    private static async Task<JsonObject?> SendHttpRequestAsync(
        HttpContext sourceContext,
        IHttpClientFactory httpClientFactory,
        GatewayEndpoint endpoint,
        IReadOnlyDictionary<string, string> extraHeaders,
        bool logTraffic,
        string responsesMode,
        JsonObject request)
    {
        request.Remove("generate");
        request.Remove("stream");
        request.Remove("background");
        request["stream"] = false;

        var body = Encoding.UTF8.GetBytes(request.ToJsonString());
        var localContext = new DefaultHttpContext();
        localContext.TraceIdentifier = sourceContext.TraceIdentifier;
        localContext.RequestAborted = sourceContext.RequestAborted;
        localContext.Request.Method = HttpMethods.Post;
        localContext.Request.ContentType = "application/json";
        localContext.Request.Body = new MemoryStream(body);
        localContext.Response.Body = new MemoryStream();
        foreach (var header in sourceContext.Request.Headers)
        {
            if (!IsWebSocketHopByHopHeader(header.Key))
            {
                localContext.Request.Headers[header.Key] = header.Value;
            }
        }

        await ResponsesCompatibility.HandleAsync(localContext, httpClientFactory, endpoint, extraHeaders, logTraffic, responsesMode);
        if (localContext.Response.StatusCode is < 200 or >= 300)
        {
            return null;
        }

        localContext.Response.Body.Position = 0;
        return await JsonNode.ParseAsync(localContext.Response.Body, cancellationToken: sourceContext.RequestAborted) as JsonObject;
    }

    private static bool IsWebSocketHopByHopHeader(string name) =>
        name.Equals("Connection", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Host", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Sec-WebSocket-", StringComparison.OrdinalIgnoreCase);

    private static async Task<string?> ReceiveTextAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }
            if (result.MessageType != WebSocketMessageType.Text || output.Length + result.Count > MaximumMessageBytes)
            {
                throw new WebSocketException(WebSocketError.InvalidMessageType);
            }
            output.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
    }

    private static async Task SendLifecycleAsync(WebSocket socket, JsonObject response, CancellationToken cancellationToken)
    {
        var inProgress = response.DeepClone().AsObject();
        inProgress["status"] = "in_progress";
        await SendEventAsync(socket, "response.created", new JsonObject { ["response"] = inProgress.DeepClone() }, cancellationToken);
        await SendEventAsync(socket, "response.in_progress", new JsonObject { ["response"] = inProgress }, cancellationToken);
        if (response["output"] is JsonArray output)
        {
            foreach (var (item, outputIndex) in output.OfType<JsonObject>().Select((item, index) => (item, index)))
            {
                await SendEventAsync(socket, "response.output_item.added", new JsonObject
                {
                    ["output_index"] = outputIndex,
                    ["item"] = item.DeepClone()
                }, cancellationToken);

                if (item["type"]?.GetValue<string>() == "message"
                    && item["content"] is JsonArray content
                    && content.FirstOrDefault() is JsonObject part
                    && part["type"]?.GetValue<string>() == "output_text")
                {
                    var itemId = item["id"]?.GetValue<string>() ?? string.Empty;
                    var text = part["text"]?.GetValue<string>() ?? string.Empty;
                    await SendEventAsync(socket, "response.content_part.added", new JsonObject
                    {
                        ["item_id"] = itemId,
                        ["output_index"] = outputIndex,
                        ["content_index"] = 0,
                        ["part"] = new JsonObject { ["type"] = "output_text", ["text"] = string.Empty, ["annotations"] = new JsonArray() }
                    }, cancellationToken);
                    if (text.Length > 0)
                    {
                        await SendEventAsync(socket, "response.output_text.delta", new JsonObject
                        {
                            ["item_id"] = itemId,
                            ["output_index"] = outputIndex,
                            ["content_index"] = 0,
                            ["delta"] = text
                        }, cancellationToken);
                    }
                    await SendEventAsync(socket, "response.output_text.done", new JsonObject
                    {
                        ["item_id"] = itemId,
                        ["output_index"] = outputIndex,
                        ["content_index"] = 0,
                        ["text"] = text
                    }, cancellationToken);
                    await SendEventAsync(socket, "response.content_part.done", new JsonObject
                    {
                        ["item_id"] = itemId,
                        ["output_index"] = outputIndex,
                        ["content_index"] = 0,
                        ["part"] = part.DeepClone()
                    }, cancellationToken);
                }

                await SendEventAsync(socket, "response.output_item.done", new JsonObject
                {
                    ["output_index"] = outputIndex,
                    ["item"] = item.DeepClone()
                }, cancellationToken);
            }
        }
        var type = response["status"]?.GetValue<string>() == "incomplete" ? "response.incomplete" : "response.completed";
        await SendEventAsync(socket, type, new JsonObject { ["response"] = response }, cancellationToken);
    }

    private static JsonObject CreateResponse(string responseId, JsonObject request, string status) => new()
    {
        ["id"] = responseId,
        ["object"] = "response",
        ["created_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        ["status"] = status,
        ["model"] = request["model"]?.DeepClone() ?? "unknown",
        ["output"] = new JsonArray(),
        ["output_text"] = "",
        ["error"] = null,
        ["incomplete_details"] = null
    };

    private static Task SendErrorAsync(WebSocket socket, string message, CancellationToken cancellationToken, string code = "invalid_request_error", int status = 400) =>
        SendEventAsync(socket, "error", new JsonObject
        {
            ["error"] = new JsonObject { ["message"] = message, ["type"] = "invalid_request_error", ["code"] = code },
            ["status"] = status
        }, cancellationToken);

    private static async Task SendEventAsync(WebSocket socket, string type, JsonObject payload, CancellationToken cancellationToken)
    {
        payload["type"] = type;
        var data = Encoding.UTF8.GetBytes(payload.ToJsonString());
        await socket.SendAsync(data, WebSocketMessageType.Text, true, cancellationToken);
    }
}
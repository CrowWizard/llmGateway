using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

static class ResponsesCompatibility
{
    private static readonly HashSet<string> UnsupportedParameters = new(StringComparer.Ordinal)
    {
        "background", "conversation", "context_management", "include", "previous_response_id", "prompt"
    };

    public static async Task HandleAsync(
        HttpContext context,
        IHttpClientFactory httpClientFactory,
        string upstreamBaseUrl,
        string userAgent,
        bool overwriteUserAgent,
        IReadOnlyDictionary<string, string> extraHeaders,
        bool logTraffic)
    {
        JsonObject request;
        try
        {
            request = await JsonNode.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted) as JsonObject
                ?? throw new JsonException("Request body must be a JSON object.");
        }
        catch (JsonException exception)
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, exception.Message, null, "invalid_json");
            return;
        }

        if (!TryConvertRequest(request, out var chatRequest, out var error))
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, error!.Value.Message, error.Value.Parameter, "unsupported_parameter");
            return;
        }

        var streaming = request["stream"]?.GetValue<bool>() == true;
        using var upstreamRequest = CreateUpstreamRequest(
            context,
            chatRequest!,
            upstreamBaseUrl,
            userAgent,
            overwriteUserAgent,
            extraHeaders);

        if (logTraffic)
        {
            Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] [{context.TraceIdentifier}] [协议转换] POST /v1/responses -> POST /v1/chat/completions");
        }

        HttpResponseMessage upstreamResponse;
        try
        {
            upstreamResponse = await httpClientFactory.CreateClient("responses-compatibility").SendAsync(
                upstreamRequest,
                HttpCompletionOption.ResponseHeadersRead,
                context.RequestAborted);
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            await WriteErrorAsync(context, StatusCodes.Status504GatewayTimeout, "The upstream request timed out.", null, "upstream_timeout");
            return;
        }
        catch (HttpRequestException exception)
        {
            await WriteErrorAsync(context, StatusCodes.Status502BadGateway, exception.Message, null, "upstream_error");
            return;
        }

        using (upstreamResponse)
        {
            if (!upstreamResponse.IsSuccessStatusCode)
            {
                await CopyUpstreamErrorAsync(context, upstreamResponse);
                return;
            }

            if (streaming)
            {
                await ConvertStreamAsync(context, upstreamResponse);
            }
            else
            {
                await ConvertResponseAsync(context, upstreamResponse);
            }
        }
    }

    private static bool TryConvertRequest(JsonObject source, out JsonObject? target, out (string Message, string? Parameter)? error)
    {
        target = null;
        error = null;

        foreach (var parameter in UnsupportedParameters)
        {
            if (source.ContainsKey(parameter))
            {
                error = ($"Unsupported parameter: {parameter}", parameter);
                return false;
            }
        }

        if (source["store"]?.GetValue<bool>() == true)
        {
            error = ("The compatibility endpoint does not support store: true.", "store");
            return false;
        }

        if (source["model"] is not JsonValue model || !model.TryGetValue<string>(out var modelName) || string.IsNullOrWhiteSpace(modelName))
        {
            error = ("The model parameter is required.", "model");
            return false;
        }

        if (!source.TryGetPropertyValue("input", out var input) || input is null)
        {
            error = ("The input parameter is required.", "input");
            return false;
        }

        var messages = new JsonArray();
        if (source["instructions"] is JsonValue instructionsValue && instructionsValue.TryGetValue<string>(out var instructions))
        {
            messages.Add(new JsonObject { ["role"] = "developer", ["content"] = instructions });
        }

        if (!TryConvertInput(input, messages, out error))
        {
            return false;
        }

        target = new JsonObject
        {
            ["model"] = modelName,
            ["messages"] = messages
        };

        Copy(source, target, "temperature", "temperature");
        Copy(source, target, "top_p", "top_p");
        Copy(source, target, "parallel_tool_calls", "parallel_tool_calls");
        Copy(source, target, "metadata", "metadata");
        Copy(source, target, "service_tier", "service_tier");
        Copy(source, target, "safety_identifier", "safety_identifier");
        Copy(source, target, "prompt_cache_key", "prompt_cache_key");
        Copy(source, target, "user", "user");
        Copy(source, target, "max_output_tokens", "max_completion_tokens");

        var streaming = source["stream"]?.GetValue<bool>() == true;
        target["stream"] = streaming;
        if (streaming)
        {
            target["stream_options"] = new JsonObject { ["include_usage"] = true };
        }

        if (source["reasoning"] is JsonObject reasoning && reasoning["effort"] is not null)
        {
            target["reasoning_effort"] = reasoning["effort"]!.DeepClone();
        }

        if (source["text"] is JsonObject text)
        {
            if (text["format"] is not null)
            {
                target["response_format"] = ConvertTextFormat(text["format"]!);
            }

            if (text["verbosity"] is not null)
            {
                target["verbosity"] = text["verbosity"]!.DeepClone();
            }
        }

        if (source["tools"] is JsonArray tools)
        {
            var convertedTools = new JsonArray();
            foreach (var node in tools)
            {
                if (node is not JsonObject tool || tool["type"]?.GetValue<string>() != "function")
                {
                    error = ("Only function tools are supported by the compatibility endpoint.", "tools");
                    target = null;
                    return false;
                }

                var function = new JsonObject();
                foreach (var name in new[] { "name", "description", "parameters", "strict" })
                {
                    if (tool[name] is not null)
                    {
                        function[name] = tool[name]!.DeepClone();
                    }
                }
                convertedTools.Add(new JsonObject { ["type"] = "function", ["function"] = function });
            }
            target["tools"] = convertedTools;
        }

        if (source["tool_choice"] is JsonObject toolChoice && toolChoice["type"]?.GetValue<string>() == "function")
        {
            target["tool_choice"] = new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = toolChoice["name"]?.DeepClone() }
            };
        }
        else
        {
            Copy(source, target, "tool_choice", "tool_choice");
        }

        return true;
    }

    private static bool TryConvertInput(JsonNode input, JsonArray messages, out (string Message, string? Parameter)? error)
    {
        error = null;
        if (input is JsonValue inputValue && inputValue.TryGetValue<string>(out var text))
        {
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = text });
            return true;
        }

        if (input is not JsonArray items)
        {
            error = ("Input must be a string or an array of input items.", "input");
            return false;
        }

        JsonObject? pendingAssistantToolMessage = null;
        foreach (var node in items)
        {
            if (node is not JsonObject item)
            {
                error = ("Every input item must be an object.", "input");
                return false;
            }

            var type = item["type"]?.GetValue<string>();
            if (type is null or "message")
            {
                pendingAssistantToolMessage = null;
                var role = item["role"]?.GetValue<string>() ?? "user";
                if (role is not ("user" or "assistant" or "system" or "developer"))
                {
                    error = ($"Unsupported input role: {role}", "input");
                    return false;
                }

                if (!TryConvertContent(item["content"], out var content))
                {
                    error = ("Only text message content is supported.", "input");
                    return false;
                }
                messages.Add(new JsonObject { ["role"] = role, ["content"] = content });
            }
            else if (type == "function_call")
            {
                if (pendingAssistantToolMessage is null)
                {
                    pendingAssistantToolMessage = new JsonObject
                    {
                        ["role"] = "assistant",
                        ["content"] = null,
                        ["tool_calls"] = new JsonArray()
                    };
                    messages.Add(pendingAssistantToolMessage);
                }

                ((JsonArray)pendingAssistantToolMessage["tool_calls"]!).Add(new JsonObject
                {
                    ["id"] = item["call_id"]?.DeepClone() ?? item["id"]?.DeepClone(),
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = item["name"]?.DeepClone(),
                        ["arguments"] = item["arguments"]?.DeepClone() ?? "{}"
                    }
                });
            }
            else if (type == "function_call_output")
            {
                pendingAssistantToolMessage = null;
                messages.Add(new JsonObject
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = item["call_id"]?.DeepClone(),
                    ["content"] = NodeToText(item["output"])
                });
            }
            else
            {
                error = ($"Unsupported input item type: {type}", "input");
                return false;
            }
        }

        return true;
    }

    private static bool TryConvertContent(JsonNode? source, out JsonNode? content)
    {
        content = null;
        if (source is JsonValue value && value.TryGetValue<string>(out var text))
        {
            content = JsonValue.Create(text);
            return true;
        }

        if (source is not JsonArray parts)
        {
            return false;
        }

        var converted = new JsonArray();
        foreach (var node in parts)
        {
            if (node is not JsonObject part || part["type"]?.GetValue<string>() is not ("input_text" or "output_text" or "text"))
            {
                return false;
            }
            converted.Add(new JsonObject { ["type"] = "text", ["text"] = part["text"]?.DeepClone() ?? "" });
        }
        content = converted;
        return true;
    }

    private static JsonNode ConvertTextFormat(JsonNode format)
    {
        if (format is JsonObject formatObject && formatObject["type"]?.GetValue<string>() == "json_schema")
        {
            var jsonSchema = new JsonObject();
            foreach (var name in new[] { "name", "description", "schema", "strict" })
            {
                if (formatObject[name] is not null)
                {
                    jsonSchema[name] = formatObject[name]!.DeepClone();
                }
            }
            return new JsonObject { ["type"] = "json_schema", ["json_schema"] = jsonSchema };
        }
        return format.DeepClone();
    }

    private static HttpRequestMessage CreateUpstreamRequest(
        HttpContext context,
        JsonObject body,
        string upstreamBaseUrl,
        string userAgent,
        bool overwriteUserAgent,
        IReadOnlyDictionary<string, string> extraHeaders)
    {
        var uri = $"{upstreamBaseUrl.TrimEnd('/')}/v1/chat/completions";
        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };

        foreach (var header in context.Request.Headers)
        {
            if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        if (overwriteUserAgent || !request.Headers.Contains("User-Agent"))
        {
            request.Headers.Remove("User-Agent");
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        }
        foreach (var (name, value) in extraHeaders)
        {
            if (name.Equals("User-Agent", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            request.Headers.Remove(name);
            request.Headers.TryAddWithoutValidation(name, value);
        }
        return request;
    }

    private static async Task ConvertResponseAsync(HttpContext context, HttpResponseMessage upstreamResponse)
    {
        JsonObject chat;
        try
        {
            await using var stream = await upstreamResponse.Content.ReadAsStreamAsync(context.RequestAborted);
            chat = await JsonNode.ParseAsync(stream, cancellationToken: context.RequestAborted) as JsonObject
                ?? throw new JsonException("The upstream response is not a JSON object.");
        }
        catch (JsonException exception)
        {
            await WriteErrorAsync(context, StatusCodes.Status502BadGateway, exception.Message, null, "invalid_upstream_response");
            return;
        }

        var response = BuildResponse(chat);
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(response.ToJsonString(), context.RequestAborted);
    }

    private static JsonObject BuildResponse(JsonObject chat)
    {
        var responseId = CreateResponseId(chat["id"]?.GetValue<string>());
        var createdAt = chat["created"]?.GetValue<long>() ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var model = chat["model"]?.GetValue<string>() ?? "unknown";
        var output = new JsonArray();
        var status = "completed";
        JsonNode? incompleteDetails = null;
        var outputText = new StringBuilder();

        if (chat["choices"] is JsonArray choices && choices.FirstOrDefault() is JsonObject choice)
        {
            var finishReason = choice["finish_reason"]?.GetValue<string>();
            if (finishReason is "length" or "content_filter")
            {
                status = "incomplete";
                incompleteDetails = new JsonObject { ["reason"] = finishReason == "length" ? "max_output_tokens" : "content_filter" };
            }

            if (choice["message"] is JsonObject message)
            {
                var text = message["content"]?.GetValue<string>();
                if (text is not null)
                {
                    outputText.Append(text);
                    output.Add(CreateMessageItem(responseId, text, status));
                }

                if (message["tool_calls"] is JsonArray toolCalls)
                {
                    foreach (var node in toolCalls)
                    {
                        if (node is JsonObject toolCall && toolCall["function"] is JsonObject function)
                        {
                            output.Add(CreateFunctionItem(toolCall, function, status));
                        }
                    }
                }
            }
        }

        return new JsonObject
        {
            ["id"] = responseId,
            ["object"] = "response",
            ["created_at"] = createdAt,
            ["status"] = status,
            ["error"] = null,
            ["incomplete_details"] = incompleteDetails,
            ["instructions"] = null,
            ["model"] = model,
            ["output"] = output,
            ["output_text"] = outputText.ToString(),
            ["parallel_tool_calls"] = true,
            ["usage"] = ConvertUsage(chat["usage"] as JsonObject)
        };
    }

    private static async Task ConvertStreamAsync(HttpContext context, HttpResponseMessage upstreamResponse)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Append("X-Accel-Buffering", "no");

        var responseId = $"resp_{Guid.NewGuid():N}";
        var createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var model = "unknown";
        var sequence = 0;
        var text = new StringBuilder();
        var messageStarted = false;
        var outputIndex = 0;
        var finishReason = "stop";
        JsonObject? usage = null;
        var tools = new Dictionary<int, StreamTool>();

        JsonObject Snapshot(string status) => new()
        {
            ["id"] = responseId,
            ["object"] = "response",
            ["created_at"] = createdAt,
            ["status"] = status,
            ["model"] = model,
            ["output"] = new JsonArray(),
            ["error"] = null,
            ["incomplete_details"] = null
        };

        await WriteEventAsync(context, "response.created", new JsonObject { ["response"] = Snapshot("in_progress") }, sequence++);
        await WriteEventAsync(context, "response.in_progress", new JsonObject { ["response"] = Snapshot("in_progress") }, sequence++);

        await using var stream = await upstreamResponse.Content.ReadAsStreamAsync(context.RequestAborted);
        using var reader = new StreamReader(stream);
        while (!context.RequestAborted.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(context.RequestAborted);
            if (line is null)
            {
                break;
            }
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[5..].TrimStart();
            if (data == "[DONE]")
            {
                break;
            }

            JsonObject? chunk;
            try
            {
                chunk = JsonNode.Parse(data) as JsonObject;
            }
            catch (JsonException)
            {
                continue;
            }
            if (chunk is null)
            {
                continue;
            }

            model = chunk["model"]?.GetValue<string>() ?? model;
            usage = chunk["usage"] as JsonObject ?? usage;
            if (chunk["choices"] is not JsonArray choices || choices.FirstOrDefault() is not JsonObject choice)
            {
                continue;
            }
            finishReason = choice["finish_reason"]?.GetValue<string>() ?? finishReason;
            if (choice["delta"] is not JsonObject delta)
            {
                continue;
            }

            if (delta["content"] is JsonValue contentValue && contentValue.TryGetValue<string>(out var contentDelta))
            {
                if (!messageStarted)
                {
                    messageStarted = true;
                    var item = CreateMessageItem(responseId, "", "in_progress");
                    await WriteEventAsync(context, "response.output_item.added", new JsonObject { ["output_index"] = outputIndex, ["item"] = item.DeepClone() }, sequence++);
                    await WriteEventAsync(context, "response.content_part.added", new JsonObject
                    {
                        ["item_id"] = item["id"]!.DeepClone(), ["output_index"] = outputIndex, ["content_index"] = 0,
                        ["part"] = new JsonObject { ["type"] = "output_text", ["text"] = "", ["annotations"] = new JsonArray() }
                    }, sequence++);
                }
                text.Append(contentDelta);
                await WriteEventAsync(context, "response.output_text.delta", new JsonObject
                {
                    ["item_id"] = $"msg_{responseId[5..]}", ["output_index"] = outputIndex, ["content_index"] = 0, ["delta"] = contentDelta
                }, sequence++);
            }

            if (delta["tool_calls"] is JsonArray toolCalls)
            {
                foreach (var node in toolCalls)
                {
                    if (node is not JsonObject toolCall)
                    {
                        continue;
                    }
                    var index = toolCall["index"]?.GetValue<int>() ?? 0;
                    if (!tools.TryGetValue(index, out var state))
                    {
                        state = new StreamTool { OutputIndex = outputIndex + (messageStarted ? 1 : 0) + tools.Count };
                        tools[index] = state;
                    }
                    state.CallId ??= toolCall["id"]?.GetValue<string>();
                    if (toolCall["function"] is JsonObject function)
                    {
                        state.Name ??= function["name"]?.GetValue<string>();
                        var argumentDelta = function["arguments"]?.GetValue<string>() ?? "";
                        if (!state.Started && state.CallId is not null)
                        {
                            state.Started = true;
                            await WriteEventAsync(context, "response.output_item.added", new JsonObject
                            {
                                ["output_index"] = state.OutputIndex,
                                ["item"] = new JsonObject { ["id"] = state.ItemId, ["type"] = "function_call", ["status"] = "in_progress", ["call_id"] = state.CallId, ["name"] = state.Name ?? "", ["arguments"] = "" }
                            }, sequence++);
                        }
                        state.Arguments.Append(argumentDelta);
                        if (state.Started && argumentDelta.Length > 0)
                        {
                            await WriteEventAsync(context, "response.function_call_arguments.delta", new JsonObject
                            {
                                ["item_id"] = state.ItemId, ["output_index"] = state.OutputIndex, ["delta"] = argumentDelta
                            }, sequence++);
                        }
                    }
                }
            }
        }

        if (messageStarted)
        {
            var itemId = $"msg_{responseId[5..]}";
            await WriteEventAsync(context, "response.output_text.done", new JsonObject { ["item_id"] = itemId, ["output_index"] = outputIndex, ["content_index"] = 0, ["text"] = text.ToString() }, sequence++);
            await WriteEventAsync(context, "response.content_part.done", new JsonObject
            {
                ["item_id"] = itemId, ["output_index"] = outputIndex, ["content_index"] = 0,
                ["part"] = new JsonObject { ["type"] = "output_text", ["text"] = text.ToString(), ["annotations"] = new JsonArray() }
            }, sequence++);
            await WriteEventAsync(context, "response.output_item.done", new JsonObject { ["output_index"] = outputIndex, ["item"] = CreateMessageItem(responseId, text.ToString(), "completed") }, sequence++);
        }

        foreach (var state in tools.Values.OrderBy(tool => tool.OutputIndex))
        {
            await WriteEventAsync(context, "response.function_call_arguments.done", new JsonObject { ["item_id"] = state.ItemId, ["output_index"] = state.OutputIndex, ["arguments"] = state.Arguments.ToString() }, sequence++);
            await WriteEventAsync(context, "response.output_item.done", new JsonObject
            {
                ["output_index"] = state.OutputIndex,
                ["item"] = new JsonObject { ["id"] = state.ItemId, ["type"] = "function_call", ["status"] = "completed", ["call_id"] = state.CallId, ["name"] = state.Name, ["arguments"] = state.Arguments.ToString() }
            }, sequence++);
        }

        var finalStatus = finishReason is "length" or "content_filter" ? "incomplete" : "completed";
        var finalResponse = Snapshot(finalStatus);
        finalResponse["output_text"] = text.ToString();
        finalResponse["usage"] = ConvertUsage(usage);
        if (finalStatus == "incomplete")
        {
            finalResponse["incomplete_details"] = new JsonObject { ["reason"] = finishReason == "length" ? "max_output_tokens" : "content_filter" };
        }
        await WriteEventAsync(context, finalStatus == "completed" ? "response.completed" : "response.incomplete", new JsonObject { ["response"] = finalResponse }, sequence++);
    }

    private static JsonObject CreateMessageItem(string responseId, string text, string status) => new()
    {
        ["id"] = $"msg_{responseId[5..]}",
        ["type"] = "message",
        ["status"] = status,
        ["role"] = "assistant",
        ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = text, ["annotations"] = new JsonArray() })
    };

    private static JsonObject CreateFunctionItem(JsonObject toolCall, JsonObject function, string status) => new()
    {
        ["id"] = $"fc_{Guid.NewGuid():N}",
        ["type"] = "function_call",
        ["status"] = status,
        ["call_id"] = toolCall["id"]?.DeepClone(),
        ["name"] = function["name"]?.DeepClone(),
        ["arguments"] = function["arguments"]?.DeepClone() ?? "{}"
    };

    private static JsonObject? ConvertUsage(JsonObject? usage)
    {
        if (usage is null)
        {
            return null;
        }
        return new JsonObject
        {
            ["input_tokens"] = usage["prompt_tokens"]?.DeepClone() ?? 0,
            ["input_tokens_details"] = new JsonObject { ["cached_tokens"] = usage["prompt_tokens_details"]?["cached_tokens"]?.DeepClone() ?? 0 },
            ["output_tokens"] = usage["completion_tokens"]?.DeepClone() ?? 0,
            ["output_tokens_details"] = new JsonObject { ["reasoning_tokens"] = usage["completion_tokens_details"]?["reasoning_tokens"]?.DeepClone() ?? 0 },
            ["total_tokens"] = usage["total_tokens"]?.DeepClone() ?? 0
        };
    }

    private static async Task WriteEventAsync(HttpContext context, string type, JsonObject payload, int sequenceNumber)
    {
        payload["type"] = type;
        payload["sequence_number"] = sequenceNumber;
        await context.Response.WriteAsync($"event: {type}\ndata: {payload.ToJsonString()}\n\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
    }

    private static async Task CopyUpstreamErrorAsync(HttpContext context, HttpResponseMessage response)
    {
        context.Response.StatusCode = (int)response.StatusCode;
        context.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
        await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    }

    private static async Task WriteErrorAsync(HttpContext context, int statusCode, string message, string? parameter, string code)
    {
        if (context.Response.HasStarted)
        {
            return;
        }
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        var body = new JsonObject
        {
            ["error"] = new JsonObject
            {
                ["message"] = message,
                ["type"] = "invalid_request_error",
                ["param"] = parameter,
                ["code"] = code
            }
        };
        await context.Response.WriteAsync(body.ToJsonString(), context.RequestAborted);
    }

    private static void Copy(JsonObject source, JsonObject target, string sourceName, string targetName)
    {
        if (source[sourceName] is not null)
        {
            target[targetName] = source[sourceName]!.DeepClone();
        }
    }

    private static string NodeToText(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return text;
        }
        return node?.ToJsonString() ?? "";
    }

    private static string CreateResponseId(string? chatId)
    {
        if (!string.IsNullOrWhiteSpace(chatId))
        {
            var suffix = chatId.StartsWith("chatcmpl-", StringComparison.Ordinal) ? chatId[9..] : chatId;
            return $"resp_{suffix}";
        }
        return $"resp_{Guid.NewGuid():N}";
    }

    private sealed class StreamTool
    {
        public string ItemId { get; } = $"fc_{Guid.NewGuid():N}";
        public int OutputIndex { get; init; }
        public string? CallId { get; set; }
        public string? Name { get; set; }
        public bool Started { get; set; }
        public StringBuilder Arguments { get; } = new();
    }
}

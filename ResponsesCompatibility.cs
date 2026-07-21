using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

static class ResponsesCompatibility
{
    private static readonly HashSet<string> UnsupportedParameters = new(StringComparer.Ordinal)
    {
        "background", "conversation", "context_management", "previous_response_id", "prompt"
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

    internal static bool TryConvertRequest(JsonObject source, out JsonObject? target, out (string Message, string? Parameter)? error)
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
            messages.Add(new JsonObject { ["role"] = "system", ["content"] = instructions });
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

        var convertedToolNames = new HashSet<string>(StringComparer.Ordinal);
        var convertedToolMappings = new List<ToolMapping>();
        if (source["tools"] is JsonArray tools)
        {
            var convertedTools = new JsonArray();
            foreach (var node in tools)
            {
                if (node is not JsonObject tool)
                {
                    error = ("Every tool must be an object.", "tools");
                    target = null;
                    return false;
                }

                if (!TryConvertTool(tool, convertedToolNames, out var convertedTool, out var mapping, out var toolError))
                {
                    error = (toolError!, "tools");
                    target = null;
                    return false;
                }

                if (convertedTool is not null && mapping is not null)
                {
                    convertedTools.Add(convertedTool);
                    convertedToolMappings.Add(mapping);
                }
            }
            if (convertedTools.Count > 0)
            {
                target["tools"] = convertedTools;
            }
        }

        if (!TryConvertToolChoice(source["tool_choice"], convertedToolNames, convertedToolMappings, out var convertedToolChoice, out var toolChoiceError))
        {
            error = (toolChoiceError!, "tool_choice");
            target = null;
            return false;
        }
        if (convertedToolChoice is not null)
        {
            target["tool_choice"] = convertedToolChoice;
        }

        return true;
    }

    private static bool TryConvertTool(
        JsonObject tool,
        HashSet<string> convertedToolNames,
        out JsonObject? convertedTool,
        out ToolMapping? mapping,
        out string? error)
    {
        convertedTool = null;
        mapping = null;
        error = null;

        var sourceType = GetRequiredString(tool, "type");
        if (sourceType is null)
        {
            error = "Every tool requires a type.";
            return false;
        }

        JsonObject function;
        string functionName;
        string canonicalType;
        string? serverLabel = null;

        switch (sourceType)
        {
            case "function":
                functionName = GetRequiredString(tool, "name") ?? "";
                if (functionName.Length == 0)
                {
                    error = "Every function tool requires a name.";
                    return false;
                }
                function = new JsonObject { ["name"] = functionName };
                foreach (var name in new[] { "description", "parameters", "strict" })
                {
                    if (tool[name] is not null)
                    {
                        function[name] = tool[name]!.DeepClone();
                    }
                }
                canonicalType = "function";
                break;

            case "web_search":
            case "web_search_preview":
                functionName = "web_search";
                canonicalType = "web_search";
                function = CreateFunctionDefinition(
                    functionName,
                    "Search the web for current information. The caller must execute the search and return a function_call_output.",
                    new JsonObject
                    {
                        ["query"] = StringProperty("The search query.")
                    },
                    "query");
                break;

            case "file_search":
                functionName = "file_search";
                canonicalType = sourceType;
                function = CreateFunctionDefinition(
                    functionName,
                    "Search the file or vector stores configured by the caller. The caller must execute the search and return a function_call_output.",
                    new JsonObject
                    {
                        ["queries"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = new JsonObject { ["type"] = "string" },
                            ["minItems"] = 1,
                            ["description"] = "One or more semantic search queries."
                        }
                    },
                    "queries");
                break;

            case "computer":
            case "computer_use_preview":
                functionName = "computer_action";
                canonicalType = "computer";
                function = CreateFunctionDefinition(
                    functionName,
                    "Request a computer interaction. The caller must validate and execute the action, then return a function_call_output.",
                    new JsonObject
                    {
                        ["action"] = StringProperty("The computer action to perform."),
                        ["arguments"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["description"] = "Action-specific arguments such as coordinates, text, or keys.",
                            ["additionalProperties"] = true
                        }
                    },
                    "action");
                break;

            case "mcp":
                serverLabel = GetRequiredString(tool, "server_label");
                if (serverLabel is null)
                {
                    error = "Every MCP tool requires server_label for compatibility conversion.";
                    return false;
                }
                functionName = CreateMcpFunctionName(serverLabel);
                canonicalType = sourceType;
                function = CreateFunctionDefinition(
                    functionName,
                    "Request a tool call from the configured MCP server. The caller must execute it and return a function_call_output.",
                    new JsonObject
                    {
                        ["tool_name"] = StringProperty("The MCP tool name."),
                        ["arguments"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["description"] = "Arguments for the MCP tool.",
                            ["additionalProperties"] = true
                        }
                    },
                    "tool_name",
                    "arguments");
                break;

            default:
                return true;
        }

        if (!convertedToolNames.Add(functionName))
        {
            error = $"Tool conversion produced a duplicate function name: {functionName}.";
            return false;
        }

        convertedTool = new JsonObject { ["type"] = "function", ["function"] = function };
        mapping = new ToolMapping(canonicalType, serverLabel, functionName);
        return true;
    }

    private static bool TryConvertToolChoice(
        JsonNode? source,
        HashSet<string> convertedToolNames,
        IReadOnlyList<ToolMapping> mappings,
        out JsonNode? converted,
        out string? error)
    {
        converted = null;
        error = null;
        if (source is null)
        {
            return true;
        }

        if (source is JsonValue value && value.TryGetValue<string>(out var choice))
        {
            if (choice is "auto" or "none" or "required" && convertedToolNames.Count > 0)
            {
                converted = JsonValue.Create(choice);
            }
            return true;
        }

        if (source is not JsonObject toolChoice || GetRequiredString(toolChoice, "type") is not { } sourceType)
        {
            error = "tool_choice must be auto, none, required, or an object identifying a tool.";
            return false;
        }

        ToolMapping? selected;
        if (sourceType == "function")
        {
            var functionName = GetRequiredString(toolChoice, "name");
            selected = functionName is null
                ? null
                : mappings.FirstOrDefault(candidate => candidate.SourceType == "function" && candidate.FunctionName == functionName);
        }
        else
        {
            var canonicalType = sourceType switch
            {
                "web_search_preview" => "web_search",
                "computer_use_preview" => "computer",
                _ => sourceType
            };
            var serverLabel = canonicalType == "mcp" ? GetRequiredString(toolChoice, "server_label") : null;
            selected = mappings.FirstOrDefault(candidate =>
                candidate.SourceType == canonicalType
                && (canonicalType != "mcp" || candidate.ServerLabel == serverLabel));
        }

        if (selected is null)
        {
            error = $"tool_choice does not identify a converted tool: {sourceType}.";
            return false;
        }

        converted = new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject { ["name"] = selected.FunctionName }
        };
        return true;
    }

    private static JsonObject CreateFunctionDefinition(
        string name,
        string description,
        JsonObject properties,
        params string[] required) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["parameters"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray(required.Select(name => (JsonNode?)JsonValue.Create(name)).ToArray()),
            ["additionalProperties"] = false
        }
    };

    private static JsonObject StringProperty(string description) => new()
    {
        ["type"] = "string",
        ["description"] = description
    };

    private static string CreateMcpFunctionName(string serverLabel)
    {
        const string prefix = "mcp_call_";
        var sanitized = new string(serverLabel
            .Select(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' ? character : '_')
            .ToArray())
            .Trim('_');
        if (sanitized.Length == 0)
        {
            sanitized = "server";
        }
        return prefix + sanitized[..Math.Min(sanitized.Length, 64 - prefix.Length)];
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

                if (!TryConvertContent(item["content"], role, out var content, out var contentError))
                {
                    error = (contentError ?? "Unsupported message content.", "input");
                    return false;
                }
                messages.Add(new JsonObject { ["role"] = role, ["content"] = content });
            }
            else if (type == "function_call")
            {
                var callId = GetRequiredString(item, "call_id") ?? GetRequiredString(item, "id");
                var name = GetRequiredString(item, "name");
                if (callId is null || name is null)
                {
                    error = ("Function calls require call_id and name.", "input");
                    return false;
                }

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
                    ["id"] = callId,
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = name,
                        ["arguments"] = NodeToText(item["arguments"] ?? JsonValue.Create("{}"))
                    }
                });
            }
            else if (type == "function_call_output")
            {
                pendingAssistantToolMessage = null;
                var callId = GetRequiredString(item, "call_id");
                if (callId is null)
                {
                    error = ("Function call outputs require call_id.", "input");
                    return false;
                }
                messages.Add(new JsonObject
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = callId,
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

    private static bool TryConvertContent(
        JsonNode? source,
        string role,
        out JsonNode? content,
        out string? error)
    {
        content = null;
        error = null;
        if (source is JsonValue value && value.TryGetValue<string>(out var text))
        {
            content = JsonValue.Create(text);
            return true;
        }

        if (source is not JsonArray parts)
        {
            error = "Message content must be a string or an array.";
            return false;
        }

        var converted = new JsonArray();
        foreach (var node in parts)
        {
            if (node is not JsonObject part)
            {
                error = "Every message content part must be an object.";
                return false;
            }

            var type = part["type"]?.GetValue<string>();
            if (type is "input_text" or "output_text" or "text")
            {
                converted.Add(new JsonObject { ["type"] = "text", ["text"] = part["text"]?.DeepClone() ?? "" });
                continue;
            }

            if (type == "input_image" && role == "user")
            {
                var imageUrl = GetRequiredString(part, "image_url");
                if (imageUrl is null)
                {
                    error = "input_image requires image_url; file_id images cannot be sent to Chat Completions.";
                    return false;
                }
                var image = new JsonObject { ["url"] = imageUrl };
                if (part["detail"] is not null)
                {
                    image["detail"] = part["detail"]!.DeepClone();
                }
                converted.Add(new JsonObject { ["type"] = "image_url", ["image_url"] = image });
                continue;
            }

            if (type == "input_audio" && role == "user")
            {
                var audio = part["input_audio"] as JsonObject ?? part["audio"] as JsonObject;
                if (audio?["data"] is null || audio["format"] is null)
                {
                    error = "input_audio requires data and format.";
                    return false;
                }
                converted.Add(new JsonObject
                {
                    ["type"] = "input_audio",
                    ["input_audio"] = new JsonObject
                    {
                        ["data"] = audio["data"]!.DeepClone(),
                        ["format"] = audio["format"]!.DeepClone()
                    }
                });
                continue;
            }

            error = type == "input_file"
                ? "input_file cannot be represented by Chat Completions; convert the file to text or an image data URL first."
                : $"Unsupported message content type: {type}";
            return false;
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
                var text = ExtractMessageText(message["content"]);
                if (!string.IsNullOrEmpty(text))
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
        var messageOutputIndex = -1;
        var nextOutputIndex = 0;
        var finishReason = "stop";
        JsonObject? usage = null;
        var tools = new Dictionary<int, StreamTool>();

        JsonObject Snapshot(string status, JsonArray? output = null) => new()
        {
            ["id"] = responseId,
            ["object"] = "response",
            ["created_at"] = createdAt,
            ["status"] = status,
            ["model"] = model,
            ["output"] = output ?? new JsonArray(),
            ["error"] = null,
            ["incomplete_details"] = null
        };

        await WriteEventAsync(context, "response.created", new JsonObject { ["response"] = Snapshot("in_progress") }, sequence++);
        await WriteEventAsync(context, "response.in_progress", new JsonObject { ["response"] = Snapshot("in_progress") }, sequence++);

        async Task StartMessageAsync()
        {
            if (messageStarted)
            {
                return;
            }
            messageStarted = true;
            messageOutputIndex = nextOutputIndex++;
            var item = CreateMessageItem(responseId, "", "in_progress");
            await WriteEventAsync(context, "response.output_item.added", new JsonObject { ["output_index"] = messageOutputIndex, ["item"] = item.DeepClone() }, sequence++);
            await WriteEventAsync(context, "response.content_part.added", new JsonObject
            {
                ["item_id"] = item["id"]!.DeepClone(), ["output_index"] = messageOutputIndex, ["content_index"] = 0,
                ["part"] = new JsonObject { ["type"] = "output_text", ["text"] = "", ["annotations"] = new JsonArray() }
            }, sequence++);
        }

        async Task StartToolAsync(StreamTool state)
        {
            if (state.Started || state.CallId is null || state.Name is null)
            {
                return;
            }
            state.Started = true;
            state.OutputIndex = nextOutputIndex++;
            await WriteEventAsync(context, "response.output_item.added", new JsonObject
            {
                ["output_index"] = state.OutputIndex,
                ["item"] = new JsonObject
                {
                    ["id"] = state.ItemId,
                    ["type"] = "function_call",
                    ["status"] = "in_progress",
                    ["call_id"] = state.CallId,
                    ["name"] = state.Name,
                    ["arguments"] = ""
                }
            }, sequence++);
            if (state.Arguments.Length > 0)
            {
                await WriteEventAsync(context, "response.function_call_arguments.delta", new JsonObject
                {
                    ["item_id"] = state.ItemId, ["output_index"] = state.OutputIndex, ["delta"] = state.Arguments.ToString()
                }, sequence++);
            }
        }

        await using var stream = await upstreamResponse.Content.ReadAsStreamAsync(context.RequestAborted);
        using var reader = new StreamReader(stream);
        var eventData = new StringBuilder();
        while (!context.RequestAborted.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(context.RequestAborted);
            if (line is null)
            {
                break;
            }
            if (line.Length == 0)
            {
                if (eventData.Length == 0)
                {
                    continue;
                }
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (eventData.Length > 0)
                {
                    eventData.Append('\n');
                }
                eventData.Append(line[5..].TrimStart());
                continue;
            }
            else
            {
                continue;
            }

            var data = eventData.ToString();
            eventData.Clear();
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

            if (TryGetContentDelta(delta["content"], out var contentDelta) && contentDelta.Length > 0)
            {
                await StartMessageAsync();
                text.Append(contentDelta);
                await WriteEventAsync(context, "response.output_text.delta", new JsonObject
                {
                    ["item_id"] = $"msg_{responseId[5..]}", ["output_index"] = messageOutputIndex, ["content_index"] = 0, ["delta"] = contentDelta
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
                        state = new StreamTool();
                        tools[index] = state;
                    }
                    state.CallId ??= toolCall["id"]?.GetValue<string>();
                    if (toolCall["function"] is not JsonObject function)
                    {
                        continue;
                    }
                    state.Name ??= function["name"]?.GetValue<string>();
                    await StartToolAsync(state);
                    var argumentDelta = function["arguments"]?.GetValue<string>() ?? "";
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

        var completedItems = new List<(int OutputIndex, JsonObject Item)>();
        if (messageStarted)
        {
            var itemId = $"msg_{responseId[5..]}";
            await WriteEventAsync(context, "response.output_text.done", new JsonObject { ["item_id"] = itemId, ["output_index"] = messageOutputIndex, ["content_index"] = 0, ["text"] = text.ToString() }, sequence++);
            await WriteEventAsync(context, "response.content_part.done", new JsonObject
            {
                ["item_id"] = itemId, ["output_index"] = messageOutputIndex, ["content_index"] = 0,
                ["part"] = new JsonObject { ["type"] = "output_text", ["text"] = text.ToString(), ["annotations"] = new JsonArray() }
            }, sequence++);
            var message = CreateMessageItem(responseId, text.ToString(), "completed");
            await WriteEventAsync(context, "response.output_item.done", new JsonObject { ["output_index"] = messageOutputIndex, ["item"] = message.DeepClone() }, sequence++);
            completedItems.Add((messageOutputIndex, message));
        }

        foreach (var state in tools.Values.Where(tool => tool.Started).OrderBy(tool => tool.OutputIndex))
        {
            await WriteEventAsync(context, "response.function_call_arguments.done", new JsonObject { ["item_id"] = state.ItemId, ["output_index"] = state.OutputIndex, ["arguments"] = state.Arguments.ToString() }, sequence++);
            var item = CreateStreamFunctionItem(state, "completed");
            await WriteEventAsync(context, "response.output_item.done", new JsonObject
            {
                ["output_index"] = state.OutputIndex,
                ["item"] = item.DeepClone()
            }, sequence++);
            completedItems.Add((state.OutputIndex, item));
        }

        var finalOutput = new JsonArray(completedItems
            .OrderBy(entry => entry.OutputIndex)
            .Select(entry => (JsonNode)entry.Item)
            .ToArray());
        var finalStatus = finishReason is "length" or "content_filter" ? "incomplete" : "completed";
        var finalResponse = Snapshot(finalStatus, finalOutput);
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

    private static JsonObject CreateStreamFunctionItem(StreamTool state, string status) => new()
    {
        ["id"] = state.ItemId,
        ["type"] = "function_call",
        ["status"] = status,
        ["call_id"] = state.CallId,
        ["name"] = state.Name,
        ["arguments"] = state.Arguments.ToString()
    };

    private static bool TryGetContentDelta(JsonNode? content, out string delta)
    {
        delta = ExtractMessageText(content);
        return delta.Length > 0;
    }

    private static string ExtractMessageText(JsonNode? content)
    {
        if (content is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return text;
        }

        if (content is JsonArray parts)
        {
            return string.Concat(parts
                .OfType<JsonObject>()
                .Where(part => part["type"]?.GetValue<string>() is "text" or "output_text")
                .Select(part => part["text"]?.GetValue<string>() ?? string.Empty));
        }

        return string.Empty;
    }

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

    private static string? GetRequiredString(JsonObject source, string propertyName)
    {
        return source[propertyName] is JsonValue value
            && value.TryGetValue<string>(out var result)
            && !string.IsNullOrWhiteSpace(result)
                ? result
                : null;
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

    private sealed record ToolMapping(string SourceType, string? ServerLabel, string FunctionName);

    private sealed class StreamTool
    {
        public string ItemId { get; } = $"fc_{Guid.NewGuid():N}";
        public int OutputIndex { get; set; } = -1;
        public string? CallId { get; set; }
        public string? Name { get; set; }
        public bool Started { get; set; }
        public StringBuilder Arguments { get; } = new();
    }
}

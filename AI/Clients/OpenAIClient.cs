using MAKER.AI.Models;
using MAKER.Configuration;
using OpenAI.Chat;

using System.Reflection;
using System.Text.Json;

#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
namespace MAKER.AI.Clients
{
    /// <summary>
    /// AI client implementation that communicates with OpenAI's Chat Completion API.
    /// Supports function/tool calling by reflecting methods from a tools object and converting
    /// them into OpenAI-compatible tool definitions. Handles tool call loops automatically.
    /// </summary>
    internal class OpenAIClient(ExecutorConfig config, string model, bool priority = false) : IAIClient
    {
        private readonly ChatClient _client = new(model: model, apiKey: config.AIProviderKeys.OpenAI);

        /// <summary>
        /// Sends a prompt to the OpenAI Chat Completion API and returns the response.
        /// If a tools object is provided, its public methods are exposed as callable functions.
        /// The method loops until all tool calls are resolved and a final text response is produced.
        /// </summary>
        /// <param name="prompt">The prompt text to send to the model.</param>
        /// <param name="toolsObject">An optional object whose public methods are reflected into OpenAI tool definitions.</param>
        /// <returns>An <see cref="AIResponse"/> containing the final text content and cumulative token usage, or <c>null</c> if no response was produced.</returns>
        public async Task<AIResponse> Request(string prompt, object? toolsObject = null)
        {
            var opts = new ChatCompletionOptions();

            if (priority)
            {
                opts.ServiceTier = new ChatServiceTier("priority");
            }

            List<ChatMessage> messages = [new UserChatMessage(prompt)];


            if (toolsObject != null)
            {
                var tools = GenerateTools(toolsObject);
                tools.ForEach(t => opts.Tools.Add(t));
                opts.AllowParallelToolCalls = true;
            }

            opts.ReasoningEffortLevel = ChatReasoningEffortLevel.Low;

            //#pragma warning disable SCME0001
            //  opts.Patch.Set("$.prompt_cache_retention"u8, "24h");
            //#pragma warning restore SCME0001

            int inputTokens = 0;
            int outputTokens = 0;
            bool requiresAction = false;
            do
            {
                requiresAction = false;
                var request = await _client.CompleteChatAsync(messages, opts);

                if (request.Value == null)
                {
                    break;
                }

                inputTokens += request.Value.Usage.InputTokenCount;
                outputTokens += request.Value.Usage.OutputTokenCount;

                switch (request.Value.FinishReason)
                {
                    case ChatFinishReason.ToolCalls:
                        {
                            messages.Add(new AssistantChatMessage(request.Value));

                            foreach (ChatToolCall toolCall in request.Value.ToolCalls)
                            {
                                var functionName = toolCall.FunctionName;

                                if (toolsObject != null)
                                {
                                    var method = toolsObject.GetType().GetMethod(functionName, BindingFlags.Public | BindingFlags.Instance)
                                        ?? throw new Exception($"Tool call for {functionName} failed: no such method found on tools object.");

                                    var parameters = method.GetParameters();
                                    var args = new List<object?>();

                                    using JsonDocument argumentsJson = JsonDocument.Parse(toolCall.FunctionArguments);

                                    foreach (var param in parameters)
                                    {
                                        if (argumentsJson.RootElement.TryGetProperty(param.Name!, out var argValue))
                                        {
                                            args.Add(Convert.ChangeType(argValue.GetRawText().Trim('"', ' ', '\n'), param.ParameterType));
                                        }
                                        else
                                        {
                                            throw new Exception($"Tool call for {functionName} failed: missing argument {param.Name}.");
                                        }
                                    }

                                    try
                                    {
                                        var result = method.Invoke(toolsObject, [.. args]);

                                        messages.Add(new ToolChatMessage(
                                            toolCall.Id,
                                            result?.ToString() ?? string.Empty
                                        ));
                                    }
                                    catch (Exception ex)
                                    {
                                        messages.Add(new ToolChatMessage(
                                            toolCall.Id,
                                            $"[ERROR] [{ex.InnerException?.GetType().Name}]: {ex.InnerException?.Message}"
                                        ));
                                    }
                                }

                                // TODO: MCP, code exec, file search, etc
                                requiresAction = true;
                            }

                            break;
                        }

                    case ChatFinishReason.Stop:
                        {
                            messages.Add(new AssistantChatMessage(request.Value));
                            break;
                        }

                    case ChatFinishReason.Length:
                        throw new Exception("Incomplete model output due to MaxTokens parameter or token limit exceeded.");

                    case ChatFinishReason.ContentFilter:
                        throw new Exception("Omitted content due to a content filter flag.");

                    case ChatFinishReason.FunctionCall:
                        throw new InvalidOperationException("Deprecated in favor of tool calls.");

                    default:
                        throw new InvalidOperationException(request.Value.FinishReason.ToString());
                }

            } while (requiresAction);

            if (messages.Count <= 1)
            {
                return null;
            }

            return new AIResponse()
            {
                Content = messages.Last().Content[0].Text,
                InputTokens = inputTokens,
                OutputTokens = outputTokens
            };
        }

        /// <summary>
        /// Reflects over the public methods of the given tools object and converts them into
        /// OpenAI <see cref="ChatTool"/> definitions. Method and parameter descriptions are read
        /// from <see cref="AIDescription"/> attributes. Parameters are mapped to JSON Schema types.
        /// </summary>
        /// <param name="toolsObject">The object whose public methods will be exposed as AI-callable tools.</param>
        /// <returns>A list of <see cref="ChatTool"/> definitions representing the available tools.</returns>
        private List<ChatTool> GenerateTools(object toolsObject)
        {
            List<ChatTool> tools = [];
            Type objectType = toolsObject.GetType();

            var methods = objectType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                var description = method.GetCustomAttributes<AIDescription>().FirstOrDefault()?.Description;

                BinaryData? paramData = null;

                if (parameters.Length > 0)
                {
                    var paramObj = new
                    {
                        type = "object",
                        properties = parameters.ToDictionary(
                            p => p.Name!,
                            p => new
                            {
                                type = p.ParameterType == typeof(string) ? "string" : "number",
                                description = p.GetCustomAttributes<AIDescription>().FirstOrDefault()?.Description ?? string.Empty
                            }
                        ),
                        required = parameters.Where(p => !p.IsOptional).Select(p => p.Name).ToArray(),
                        additionalProperties = false
                    };

                    var json = JsonSerializer.Serialize(paramObj);
                    paramData = BinaryData.FromString(json);
                }


                var tool = ChatTool.CreateFunctionTool(
                    functionName: method.Name,
                    functionDescription: description,
                    functionParameters: paramData ?? null
                );

                tools.Add(tool);
            }

            return tools;
        }
    }
}
#pragma warning restore OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

using MAKER.AI.Models;
using MAKER.Configuration;
using OpenAI.Responses;
using System.Text.Json;

#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
namespace MAKER.AI.Clients
{
    internal sealed class OpenAIClient(ExecutorConfig config, string model, bool priority = false) : AIClientBase
    {
        private readonly ResponsesClient _client = new(config.AIProviderKeys.OpenAI);

        protected override async Task<AIResponse?> RequestInternal(string prompt, List<AIFunctionInfo>? tools = null, object? toolsObject = null, List<MCPServerInfo>? mcpServers = null, CancellationToken cancellationToken = default)
        {
            var opts = new CreateResponseOptions()
            {
                Model = model
            };

            if (tools != null)
            {
                var chatTools = GenerateTools(tools);
                chatTools.ForEach(t => opts.Tools.Add(t));
            }

            if (mcpServers != null)
            {
                foreach (var server in mcpServers)
                {
                    opts.Tools.Add(ResponseTool.CreateMcpTool(server.Name, server.Url, server.ApiKey ?? null, server.Description, toolCallApprovalPolicy: new McpToolCallApprovalPolicy(GlobalMcpToolCallApprovalPolicy.NeverRequireApproval)));
                }
            }

            opts.InputItems.Add(
                ResponseItem.CreateUserMessageItem(prompt)
            );

            //#pragma warning disable SCME0001
            //  opts.Patch.Set("$.prompt_cache_retention"u8, "24h");
            //#pragma warning restore SCME0001

            int inputTokens = 0;
            int outputTokens = 0;
            bool requiresAction = false;
            ResponseResult? responseResult = null;
            do
            {
                requiresAction = false;
                var request = await _client.CreateResponseAsync(opts, cancellationToken);
                responseResult = request.Value;

                if (request.Value == null)
                {
                    break;
                }

                inputTokens += request.Value.Usage.InputTokenCount;
                outputTokens += request.Value.Usage.OutputTokenCount;

                

                //switch (request.Value.)
                //{
                //    case ChatFinishReason.ToolCalls:
                //        {
                //            messages.Add(new AssistantChatMessage(request.Value));

                //            foreach (ChatToolCall toolCall in request.Value.ToolCalls)
                //            {
                //                try
                //                {
                //                    var result = InvokeTool(toolCall.FunctionName, toolCall.FunctionArguments.ToString(), toolsObject!);

                //                    messages.Add(new ToolChatMessage(
                //                        toolCall.Id,
                //                        result
                //                    ));
                //                }
                //                catch (Exception ex)
                //                {
                //                    messages.Add(new ToolChatMessage(
                //                        toolCall.Id,
                //                        FormatToolError(ex)
                //                    ));
                //                }

                //                // TODO: MCP, code exec, file search, etc
                //                requiresAction = true;
                //            }

                //            break;
                //        }

                //    case ChatFinishReason.Stop:
                //        {
                //            messages.Add(new AssistantChatMessage(request.Value));
                //            break;
                //        }

                //    case ChatFinishReason.Length:
                //        throw new InvalidOperationException("Incomplete model output due to MaxTokens parameter or token limit exceeded.");

                //    case ChatFinishReason.ContentFilter:
                //        throw new InvalidOperationException("Omitted content due to a content filter flag.");

                //    case ChatFinishReason.FunctionCall:
                //        throw new InvalidOperationException("Deprecated in favor of tool calls.");

                //    default:
                //        throw new InvalidOperationException(request.Value.FinishReason.ToString());
                //}

            } while (requiresAction);

            if (responseResult == null)
            {
                return null;
            }

            return new AIResponse()
            {
                Content = responseResult.GetOutputText(),
                InputTokens = inputTokens,
                OutputTokens = outputTokens
            };
        }

        private List<ResponseTool> GenerateTools(List<AIFunctionInfo> functions)
        {
            List<ResponseTool> tools = [];

            foreach (var function in functions)
            {
                BinaryData? paramData = null;

                if (function.Parameters.Count > 0)
                {
                    var paramObj = new
                    {
                        type = "object",
                        properties = function.Parameters.ToDictionary(
                            p => p.Name,
                            p => new
                            {
                                type = p.ParameterType == typeof(string) ? "string" : "number",
                                description = p.Description
                            }
                        ),
                        required = function.Parameters.Where(p => p.IsRequired).Select(p => p.Name).ToArray(),
                        additionalProperties = false
                    };

                    var json = JsonSerializer.Serialize(paramObj);
                    paramData = BinaryData.FromString(json);
                }

                var tool = ResponseTool.CreateFunctionTool(
                    functionName: function.Name,
                    functionDescription: function.Description,
                    functionParameters: paramData,
                    strictModeEnabled: false
                );

                tools.Add(tool);
            }

            return tools;
        }
    }
}
#pragma warning restore OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

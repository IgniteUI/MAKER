using System.Reflection;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using MAKER.AI.Models;
using MAKER.Configuration;

namespace MAKER.AI.Clients
{
    internal class AnthropicAIClient(ExecutorConfig config, string model) : IAIClient
    {
        private readonly AnthropicClient _client = new() { APIKey = config.AIProviderKeys.Anthropic };
        private readonly Model _model = Enum.Parse<Model>(model);

        public async Task<AIResponse?> Request(string prompt, object? toolsObject = null)
        {
            List<MessageParam> messages = [new() { Role = Role.User, Content = prompt }];

            List<ToolUnion>? tools = toolsObject != null ? GenerateTools(toolsObject) : null;

            int inputTokens = 0;
            int outputTokens = 0;
            Message? lastResponse = null;
            bool requiresAction = false;
            do
            {
                requiresAction = false;
                var request = await _client.Messages.Create(tools != null
                    ? new MessageCreateParams
                    {
                        MaxTokens = 8192,
                        Messages = messages,
                        Model = _model,
                        Tools = tools,
                        ToolChoice = new ToolChoice(new ToolChoiceAuto()),
                    }
                    : new MessageCreateParams
                    {
                        MaxTokens = 8192,
                        Messages = messages,
                        Model = _model,
                    });

                if (request == null)
                {
                    break;
                }

                lastResponse = request;
                inputTokens += (int)request.Usage.InputTokens;
                outputTokens += (int)request.Usage.OutputTokens;

                // NOTE: ApiEnum == is broken in SDK v11; cast to the enum to compare
                switch ((StopReason?)request.StopReason)
                {
                    case StopReason.ToolUse:
                        {
                            messages.Add(new MessageParam
                            {
                                Role = Role.Assistant,
                                Content = new MessageParamContent(BuildAssistantContent(request)),
                            });

                            foreach (var block in request.Content)
                            {
                                if (!block.TryPickToolUse(out var toolUse))
                                {
                                    continue;
                                }

                                if (toolsObject != null)
                                {
                                    var method = toolsObject.GetType().GetMethod(toolUse.Name, BindingFlags.Public | BindingFlags.Instance)
                                        ?? throw new Exception($"Tool call for {toolUse.Name} failed: no such method found on tools object.");

                                    var parameters = method.GetParameters();
                                    var args = new List<object?>();

                                    var inputJson = JsonSerializer.Serialize(toolUse.Input);
                                    using JsonDocument argumentsJson = JsonDocument.Parse(inputJson);

                                    foreach (var param in parameters)
                                    {
                                        if (argumentsJson.RootElement.TryGetProperty(param.Name!, out var argValue))
                                        {
                                            args.Add(Convert.ChangeType(argValue.GetRawText().Trim('"', ' ', '\n'), param.ParameterType));
                                        }
                                        else
                                        {
                                            throw new Exception($"Tool call for {toolUse.Name} failed: missing argument {param.Name}.");
                                        }
                                    }

                                    try
                                    {
                                        var result = method.Invoke(toolsObject, [.. args]);

                                        messages.Add(new MessageParam
                                        {
                                            Role = Role.User,
                                            Content = new MessageParamContent([new ContentBlockParam(new ToolResultBlockParam
                                            {
                                                ToolUseID = toolUse.ID,
                                                Content = new ToolResultBlockParamContent(result?.ToString() ?? string.Empty),
                                            })]),
                                        });
                                    }
                                    catch (Exception ex)
                                    {
                                        messages.Add(new MessageParam
                                        {
                                            Role = Role.User,
                                            Content = new MessageParamContent([new ContentBlockParam(new ToolResultBlockParam
                                            {
                                                ToolUseID = toolUse.ID,
                                                Content = new ToolResultBlockParamContent(
                                                    $"[ERROR] [{ex.InnerException?.GetType().Name}]: {ex.InnerException?.Message}"),
                                            })]),
                                        });
                                    }
                                }

                                requiresAction = true;
                            }

                            break;
                        }

                    case StopReason.EndTurn:
                        break;

                    case StopReason.MaxTokens:
                        throw new Exception("Incomplete model output due to MaxTokens parameter or token limit exceeded.");

                    default:
                        throw new InvalidOperationException(request.StopReason?.ToString());
                }

            } while (requiresAction);

            if (lastResponse == null || lastResponse.Content.Count == 0)
            {
                return null;
            }

            lastResponse.Content[lastResponse.Content.Count - 1].TryPickText(out var finalText);

            return new AIResponse()
            {
                Content = finalText?.Text,
                InputTokens = inputTokens,
                OutputTokens = outputTokens
            };
        }

        private static List<ContentBlockParam> BuildAssistantContent(Message response)
        {
            List<ContentBlockParam> content = [];

            foreach (var block in response.Content)
            {
                if (block.TryPickText(out var text))
                {
                    content.Add(new ContentBlockParam(new TextBlockParam { Text = text.Text }));
                }
                else if (block.TryPickToolUse(out var toolUse))
                {
                    content.Add(new ContentBlockParam(new ToolUseBlockParam
                    {
                        ID = toolUse.ID,
                        Name = toolUse.Name,
                        Input = toolUse.Input,
                    }));
                }
            }

            return content;
        }

        private List<ToolUnion> GenerateTools(object toolsObject)
        {
            List<ToolUnion> tools = [];
            var objectType = toolsObject.GetType();

            var methods = objectType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                var description = method.GetCustomAttributes<AIDescription>().FirstOrDefault()?.Description;

                InputSchema inputSchema;

                if (parameters.Length > 0)
                {
                    inputSchema = new InputSchema
                    {
                        Type = JsonSerializer.Deserialize<JsonElement>(@"""object"""),
                        Properties = parameters.ToDictionary(
                            p => p.Name!,
                            p => JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(new
                            {
                                type = p.ParameterType == typeof(string) ? "string" : "number",
                                description = p.GetCustomAttributes<AIDescription>().FirstOrDefault()?.Description ?? string.Empty
                            }))
                        ),
                        Required = parameters.Where(p => !p.IsOptional).Select(p => p.Name!).ToList(),
                    };
                }
                else
                {
                    inputSchema = new InputSchema
                    {
                        Type = JsonSerializer.Deserialize<JsonElement>(@"""object"""),
                    };
                }

                tools.Add(new ToolUnion(new Tool
                {
                    Name = method.Name,
                    Description = description,
                    InputSchema = inputSchema,
                }));
            }

            return tools;
        }
    }
}

using Anthropic;
using Anthropic.Models.Messages;
using MAKER.AI.Models;
using MAKER.Configuration;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using System.Text.Json;

namespace MAKER.AI.Clients
{
    internal sealed class AnthropicAIClient(ExecutorConfig config, string model) : AIClientBase
    {
        private readonly AnthropicClient _client = new() { ApiKey = config.AIProviderKeys.Anthropic };

        protected override async Task<AIResponse?> RequestInternal(string prompt, List<AIFunctionInfo>? tools = null, object? toolsObject = null, List<MCPServerInfo>? mcpServers = null, CancellationToken cancellationToken = default)
        {
            List<AITool> aiTools = [];

            int inputTokens = 0;
            int outputTokens = 0;

            foreach (var server in mcpServers ?? [])
            {
                McpClient mcpClient = await McpClient.CreateAsync(
                    new HttpClientTransport(new() { Endpoint = server.Url, AdditionalHeaders = server.ApiKey != null ? new Dictionary<string, string>() { ["Authorization"] = "Bearer " + server.ApiKey } : null }),
                    new()
                    {
                        ClientInfo = new()
                        {
                            Name = server.Name,
                            Description = server.Description,
                            Version = "1.0",
                        },
                        
                    },
                    cancellationToken: cancellationToken
                );
                aiTools.AddRange(await mcpClient.ListToolsAsync(cancellationToken: cancellationToken));
            }

            foreach (var tool in tools ?? [])
            {
                aiTools.Add(AIFunctionFactory.Create(tool.Info, toolsObject, tool.Name, tool.Description));
            }

            ChatOptions opts = new()
            {
                Tools = [ .. aiTools, new HostedCodeInterpreterTool()],
                ModelId = model,
                AllowMultipleToolCalls = true,
                ToolMode = ChatToolMode.Auto,
            };

            var client = new ChatClientBuilder(_client.AsIChatClient()).UseFunctionInvocation().Build();

            var resp = await client.GetResponseAsync(prompt, opts, cancellationToken);

            if (resp == null || string.IsNullOrEmpty(resp.Text))
            {
                return null;
            }

            opts.ConversationId = resp.ConversationId;

            inputTokens += (int)(resp.Usage?.InputTokenCount ?? 0);
            outputTokens += (int)(resp.Usage?.OutputTokenCount ?? 0);

            return new AIResponse()
            {
                Content = resp.Text,
                InputTokens = inputTokens,
                OutputTokens = outputTokens
            };
        }
    }
}

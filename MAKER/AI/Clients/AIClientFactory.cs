using MAKER.Configuration;

namespace MAKER.AI.Clients
{
    internal sealed class AIClientFactory(ExecutorConfig executorConfig) : IAIClientFactory
    {
        public IAIClient CreateClient(ClientProviderConfig config)
        {
            return config.Provider.ToUpperInvariant() switch
            {
                "OPENAI" => new OpenAIClient(executorConfig, config.Model),
                "GOOGLE" => new GoogleAIClient(executorConfig, config.Model),
                "ANTHROPIC" => new AnthropicAIClient(executorConfig, config.Model),
                _ => throw new NotSupportedException($"AI provider '{config.Provider}' is not supported."),
            };
        }
    }
}

using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using MAKER.AI.Models;
using MAKER.Configuration;

namespace MAKER.AI.Clients
{
    public class AnthropicAIClient : IAIClient
    {
        private readonly AnthropicClient _client;
        private readonly Model _model;

        public AnthropicAIClient(ExecutorConfig config, string model)
        {
            _model = Enum.Parse<Model>(model);
            _client = new AnthropicClient()
            {
                APIKey = config.AIProviderKeys.Anthropic
            };
        }

        public async Task<AIResponse?> Request(string prompt, object? toolsObject = null)
        {
            MessageCreateParams parameters = new()
            {
                MaxTokens = 1024,
                Messages =
                [
                    new()
                    {
                        Role = Role.User,
                        Content = prompt,
                    },
                ],
                Model = _model,
            };

            try
            {
                var response = await _client.Messages.Create(parameters);
                response.Content[response.Content.Count - 1].TryPickText(out var text);

                return new AIResponse()
                {
                    Content = text?.Text,
                    InputTokens = (int)response.Usage.InputTokens,
                    OutputTokens = (int)response.Usage.OutputTokens,
                };
            }
            catch (AnthropicBadRequestException)
            {
                throw;
            }
        }
    }
}

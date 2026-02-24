using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using MAKER.AI.Models;
using MAKER.Configuration;

namespace MAKER.AI.Clients
{
    /// <summary>
    /// AI client implementation that communicates with Anthropic's Claude Messages API.
    /// Currently supports basic prompt-response interactions without tool/function calling.
    /// </summary>
    public class AnthropicAIClient : IAIClient
    {
        private readonly AnthropicClient _client;
        private readonly Model _model;

        /// <summary>
        /// Initializes a new instance of the <see cref="AnthropicAIClient"/> class with the specified
        /// configuration and model name. The API key is read from <see cref="ExecutorConfig.AIProviderKeys"/>.
        /// </summary>
        /// <param name="config">The executor configuration containing the Anthropic API key.</param>
        /// <param name="model">The Anthropic model name to use (e.g., "ClaudeHaiku4_5").</param>
        public AnthropicAIClient(ExecutorConfig config, string model)
        {
            _model = Enum.Parse<Model>(model);
            _client = new AnthropicClient()
            {
                APIKey = config.AIProviderKeys.Anthropic
            };
        }

        /// <summary>
        /// Sends a prompt to the Anthropic Claude API and returns the response.
        /// </summary>
        /// <param name="prompt">The prompt text to send to Claude.</param>
        /// <param name="toolsObject">Optional tools object (not currently supported by this client).</param>
        /// <returns>An <see cref="AIResponse"/> containing the response text and token usage.</returns>
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

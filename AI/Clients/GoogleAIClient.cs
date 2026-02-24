using Google.GenAI;
using MAKER.AI.Models;
using MAKER.Configuration;

namespace MAKER.AI.Clients
{
    /// <summary>
    /// AI client implementation that communicates with Google's Generative AI (Gemini) API.
    /// Includes automatic retry logic for server errors and rate limiting (RESOURCE_EXHAUSTED).
    /// Tool/function calling is not currently supported by this client.
    /// </summary>
    internal class GoogleAIClient(ExecutorConfig config, string model) : IAIClient
    {
        private readonly Client _client = new(apiKey: config.AIProviderKeys.Google);

        /// <summary>
        /// Sends a prompt to the Google Gemini API and returns the response.
        /// Automatically retries on server errors (after 2 seconds) and rate-limit errors (after 80 seconds).
        /// </summary>
        /// <param name="prompt">The prompt text to send to the Gemini model.</param>
        /// <param name="toolsObject">Optional tools object (not currently supported by this client).</param>
        /// <returns>An <see cref="AIResponse"/> containing the response text and token usage.</returns>
        public async Task<AIResponse?> Request(string prompt, object? toolsObject = null)
        {
            var responseString = string.Empty;
            int inputTokens = 0;
            int outputTokens = 0;

            try
            {
                var response = await _client.Models.GenerateContentAsync(
                    model: model,
                    contents: prompt
                );
                inputTokens = response.UsageMetadata?.PromptTokenCount ?? 0;
                outputTokens = response.UsageMetadata?.TotalTokenCount - inputTokens ?? 0;

                responseString = response.Candidates?[0]?.Content?.Parts?[0]?.Text;
            }
            catch (ServerError)
            {
                await Task.Delay(2000);
                return await Request(prompt);
            }
            catch (ClientError ex)
            {
                if (ex.Status == "RESOURCE_EXHAUSTED")
                {
                    await Task.Delay(80000);
                    return await Request(prompt);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error during model request: {ex.Message}");
            }

            return new AIResponse()
            {
                Content = responseString,
                InputTokens = inputTokens,
                OutputTokens = outputTokens
            };
        }
    }
}

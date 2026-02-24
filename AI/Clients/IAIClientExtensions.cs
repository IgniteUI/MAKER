using MAKER.AI.Exceptions;
using MAKER.AI.Models;
using MAKER.AI.Redflag;
using System.Text.RegularExpressions;

namespace MAKER.AI.Clients
{
    /// <summary>
    /// Provides extension methods for <see cref="IAIClient"/> that add validation and retry behavior.
    /// </summary>
    public static class IAIClientExtensions
    {
        /// <summary>
        /// Sends a prompt to the AI client and validates the response against the provided red-flag validators.
        /// If the response is null, empty, or fails any validator, the method automatically retries by appending
        /// the rejection reason to the prompt, giving the AI model feedback to correct its output.
        /// JSON code-fence wrappers (```json ... ```) are automatically stripped from the response.
        /// </summary>
        /// <param name="client">The AI client to send the request to.</param>
        /// <param name="prompt">The prompt text to send to the AI model.</param>
        /// <param name="validators">The list of red-flag validators to run against the AI response.</param>
        /// <param name="tools">An optional tools object whose public methods are exposed as callable functions to the AI model.</param>
        /// <returns>A validated <see cref="AIResponse"/> with the cleaned content and token usage.</returns>
        /// <exception cref="AIRedFlagException">Caught internally to trigger retry with feedback; not surfaced to callers.</exception>
        public static async Task<AIResponse> GuardedRequest(this IAIClient client, string prompt, List<IAIRedFlagValidator> validators, object? tools = null)
        {
            try
            {
                var responseObj = await client.Request(prompt, tools) ?? throw new AIRedFlagException("Received null response from the model.");
                var response = responseObj.Content ?? throw new AIRedFlagException("Received response with null content from the model.");

                var jsonMatch = Regex.Match(response, @"```json\s*(.*?)\s*```", RegexOptions.Singleline);
                response = jsonMatch.Success ? jsonMatch.Groups[1].Value.Trim() : response.Trim();

                validators.ForEach(validator => validator.Validate(response));

                return new AIResponse()
                {
                    Content = response,
                    InputTokens = responseObj.InputTokens,
                    OutputTokens = responseObj.OutputTokens,
                };
            }
            catch (AIRedFlagException ex)
            {
                return await client.GuardedRequest($"{prompt}\n\nLast response was rejected:\n{ex.Message}", validators, tools);
            }
        }
    }
}

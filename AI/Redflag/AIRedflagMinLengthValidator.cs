using MAKER.AI.Exceptions;

namespace MAKER.AI.Redflag
{
    /// <summary>
    /// A red-flag validator that rejects AI output shorter than a specified minimum character length.
    /// Useful for detecting empty, truncated, or placeholder responses from AI models.
    /// </summary>
    /// <param name="minLength">The minimum number of characters required for the AI output to pass validation.</param>
    public class AIRedFlagMinLengthValidator(int minLength) : IAIRedFlagValidator
    {
        /// <summary>
        /// Validates that the AI output meets the minimum length requirement.
        /// </summary>
        /// <param name="aiOutput">The AI-generated text to validate.</param>
        /// <exception cref="AIRedFlagException">Thrown when the output is null, empty, or shorter than the configured minimum length.</exception>
        public void Validate(string aiOutput)
        {
            if (string.IsNullOrEmpty(aiOutput) || aiOutput.Length < minLength)
            {
                throw new AIRedFlagException($"AI output is too short. Minimum length is {minLength}.");
            }
        }
    }
}

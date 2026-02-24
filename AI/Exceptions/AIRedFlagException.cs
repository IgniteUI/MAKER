namespace MAKER.AI.Exceptions
{
    /// <summary>
    /// Exception thrown when an AI response fails a red-flag validation check.
    /// Red-flag validators inspect AI output for quality issues (e.g., too short, malformed)
    /// and throw this exception to trigger automatic retry with feedback to the AI model.
    /// </summary>
    public class AIRedFlagException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AIRedFlagException"/> class.
        /// </summary>
        public AIRedFlagException() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="AIRedFlagException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">A description of the validation failure that caused this exception.</param>
        public AIRedFlagException(string message) : base(message) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="AIRedFlagException"/> class with a specified error message
        /// and a reference to the inner exception that is the cause of this exception.
        /// </summary>
        /// <param name="message">A description of the validation failure.</param>
        /// <param name="inner">The exception that caused this exception.</param>
        public AIRedFlagException(string message, Exception inner) : base(message, inner) { }
    }
}

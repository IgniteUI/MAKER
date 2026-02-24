namespace MAKER.AI
{
    /// <summary>
    /// Attribute used to annotate methods and parameters on tool objects with human-readable descriptions.
    /// These descriptions are reflected at runtime and sent to AI models as part of function/tool definitions,
    /// enabling the AI to understand what each tool method does and what its parameters expect.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Parameter, Inherited = false)]
    public class AIDescription : Attribute
    {
        /// <summary>
        /// Gets the description text that will be exposed to the AI model for this method or parameter.
        /// </summary>
        public string Description { get; init; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AIDescription"/> attribute with the specified description.
        /// </summary>
        /// <param name="description">A human-readable description of the method or parameter for the AI model.</param>
        public AIDescription(string description)
        {
            this.Description = description;
        }
    }
}

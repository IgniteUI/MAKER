namespace MAKER.AI.Models
{
    /// <summary>
    /// Represents a single discrete step in an AI-generated plan. Each step describes a task
    /// to be executed by the execution orchestrator, along with its dependencies and metadata.
    /// </summary>
    public class Step
    {
        /// <summary>
        /// Gets or sets the description of the task to be performed in this step.
        /// The special value <c>"End"</c> signals that the planning phase is complete.
        /// </summary>
        public required string Task { get; set; }

        /// <summary>
        /// Gets or sets the list of zero-based indices referencing other steps that must be
        /// completed before this step can be executed. Used for dependency ordering.
        /// </summary>
        public List<int> RequiredSteps { get; set; } = [];

        /// <summary>
        /// Gets or sets additional context information to be included in the execution prompt
        /// when this step is processed. Can be used to provide supplementary instructions.
        /// </summary>
        public string ExtraContext { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether the execution of this step requires
        /// the output format specification to be injected into the prompt.
        /// </summary>
        public bool RequiresFormat { get; set; }
    }
}

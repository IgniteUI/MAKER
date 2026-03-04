namespace MAKER.AI.Models
{
    public class Step
    {
        /// <summary>
        /// A description of the step to be performed.
        /// </summary>
        public required string Task { get; set; }

        /// <summary>
        /// A list of indices representing steps that must be completed before this step can be executed.
        /// </summary>
        public List<int> RequiredSteps { get; set; } = [];

        /// <summary>
        /// Additional context to be included in the execution prompt for this step.
        /// </summary>
        public string ExtraContext { get; set; } = string.Empty;

        /// <summary>
        /// Whether the execution of the step requires information about the output format.
        /// </summary>
        public bool RequiresFormat { get; set; }
    }
}

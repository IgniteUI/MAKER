using MAKER.AI.Clients;
using MAKER.AI.Exceptions;
using MAKER.AI.Models;
using MAKER.AI.Orchestrators;
using MAKER.AI.Redflag;
using MAKER.Configuration;

namespace MAKER
{
    /// <summary>
    /// The main entry point for the MAKER AI orchestration pipeline. Coordinates the full
    /// Plan → Vote → Execute → Vote workflow by delegating to the <see cref="PlanningOrchestrator"/>
    /// and <see cref="ExecutionOrchestrator"/>. Instantiates the appropriate AI clients based on
    /// configuration and exposes event callbacks for monitoring pipeline progress.
    /// </summary>
    public class Executor
    {
        private readonly ExecutorConfig _config;
        private readonly PlanningOrchestrator _planningOrchestrator;
        private readonly ExecutionOrchestrator _executionOrchestrator;

        /// <summary>
        /// Gets or sets the output format label (e.g., "Standard XML", "plaintext") that is injected
        /// into prompt templates to instruct the AI model on the desired response format.
        /// </summary>
        public string Format { get; set; } = "plaintext";

        #region Events
        /// <summary>
        /// Raised when a batch of plan steps has been accepted by the voting system.
        /// The first parameter contains the newly accepted steps; the second contains previously accepted steps.
        /// </summary>
        public Action<IList<Step>, IList<Step>> OnStepsAdded
        {
            get => _planningOrchestrator.OnStepsAccepted;
            set => _planningOrchestrator.OnStepsAccepted = value;
        }

        /// <summary>
        /// Raised when a batch of plan steps has been proposed by the planning AI and is about to be voted on.
        /// </summary>
        public Action<IList<Step>> OnStepsProposed
        {
            get => _planningOrchestrator.OnStepsProposed;
            set => _planningOrchestrator.OnStepsProposed = value;
        }

        /// <summary>
        /// Raised when a batch of proposed plan steps has been rejected by the voting system.
        /// The exception contains the rejected steps and the reasons for rejection.
        /// </summary>
        public Action<AIVoteException> OnStepsRejected
        {
            get => _planningOrchestrator.OnStepsRejected;
            set => _planningOrchestrator.OnStepsRejected = value;
        }

        /// <summary>
        /// Raised whenever a plan voting round receives a new vote, providing the current vote tally.
        /// </summary>
        public Action<VoteState> OnPlanVoteChanged
        {
            get => _planningOrchestrator.OnVoteChanged;
            set => _planningOrchestrator.OnVoteChanged = value;
        }

        /// <summary>
        /// Raised when execution of a batch of steps begins.
        /// The first parameter contains the current batch; the second contains previously completed steps.
        /// </summary>
        public Action<IList<Step>, IList<Step>> OnExecutionStarted
        {
            get => _executionOrchestrator.OnExecutionStarted;
            set => _executionOrchestrator.OnExecutionStarted = value;
        }

        /// <summary>
        /// Raised when the cumulative execution state changes after a step batch is completed.
        /// The string parameter contains the full current state as returned by the execution AI.
        /// </summary>
        public Action<string> OnStateChanged
        {
            get => _executionOrchestrator.OnStateChanged;
            set => _executionOrchestrator.OnStateChanged = value;
        }

        /// <summary>
        /// Raised whenever an execution voting round receives a new vote, providing the current vote tally.
        /// </summary>
        public Action<VoteState> OnExecutionVoteChanged
        {
            get => _executionOrchestrator.OnVoteChanged;
            set => _executionOrchestrator.OnVoteChanged = value;
        }
        #endregion

        /// <summary>
        /// Gets or sets the default list of red-flag validators applied during planning.
        /// These validators check AI-generated plan output for quality issues before it proceeds to voting.
        /// </summary>
        public List<IAIRedFlagValidator> DefaultPlanningValidators
        {
            get => _planningOrchestrator.DefaultPlanningValidators;
            set => _planningOrchestrator.DefaultPlanningValidators = value;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Executor"/> class with the specified configuration and output format.
        /// Creates the AI clients for each pipeline stage (planning, plan voting, execution, execution voting)
        /// and instantiates the planning and execution orchestrators.
        /// </summary>
        /// <param name="config">The executor configuration containing AI provider keys, client assignments, and instruction paths.</param>
        /// <param name="format">The output format label injected into prompts (e.g., "Standard XML").</param>
        public Executor(ExecutorConfig config, string format)
        {
            _config = config;
            Format = format;

            var planningClient = InstantiateClient(_config.Clients.Planning);
            var planVotingClient = InstantiateClient(_config.Clients.PlanVoting);
            var executionClient = InstantiateClient(_config.Clients.Execution);
            var executionVotingClient = InstantiateClient(_config.Clients.ExecutionVoting);

            _planningOrchestrator = new PlanningOrchestrator(_config, planningClient, planVotingClient);
            _executionOrchestrator = new ExecutionOrchestrator(_config, executionClient, executionVotingClient);
        }

        /// <summary>
        /// Generates a sequence of plan Steps based on the specified prompt and configuration parameters.
        /// </summary>
        /// <param name="prompt">The input prompt that guides the planning process. This should clearly describe the objective or task to be planned.</param>
        /// <param name="batchSize">The number of Steps to generate in each batch. Must be greater than zero.</param>
        /// <param name="k">The difference in votes required for a voting decision to be made.</param>
        /// <param name="prependSteps">An optional list of steps to prepend to the generated plan. If provided, these steps will appear at the beginning of the returned sequence.</param>
        /// <param name="validators">An optional list of validators used to check for red flags in the generated steps. Each validator is applied
        /// to ensure the plan meets safety or compliance requirements.</param>
        /// <returns>A list of steps representing the generated plan.</returns>
        public async Task<IList<Step>> Plan(string prompt, int batchSize = 2, int k = 10, IList<Step> prependSteps = null!, List<IAIRedFlagValidator> validators = null!, object? tools = null!)
        {
            if (batchSize <= 0) throw new ArgumentOutOfRangeException($"{nameof(batchSize)} must be greater than zero");
            if (k <= 0) throw new ArgumentOutOfRangeException($"{nameof(k)} must be greater than zero");

            return await _planningOrchestrator.Plan(prompt, Format, batchSize, k, prependSteps, validators, tools);
        }

        /// <summary>
        /// Executes a sequence of steps using the specified prompt and returns the resulting state as a string.
        /// </summary>
        /// <param name="steps">The list of steps to execute. Each step defines an operation in the workflow.</param>
        /// <param name="prompt">The input prompt that guides the execution of the steps.</param>
        /// <param name="batchSize">The number of steps to process in each batch. Must be greater than zero.</param>
        /// <param name="k">The difference in votes required for a voting decision to be made.</param>
        /// <param name="validators">A list of validators used to check for AI-generated red flags during execution. If null, no validation is performed.</param>
        /// <returns>The resulting state of the Step execution.</returns>
        public async Task<string> Execute(IList<Step> steps, string prompt, int batchSize = 2, int k = 10, List<IAIRedFlagValidator> validators = null!, object? tools = null!)
        {
            if (batchSize <= 0) throw new ArgumentOutOfRangeException($"{nameof(batchSize)} must be greater than zero");
            if (k <= 0) throw new ArgumentOutOfRangeException($"{nameof(k)} must be greater than zero");

            return await _executionOrchestrator.Execute(steps, prompt, Format, batchSize, k, validators, tools);
        }

        /// <summary>
        /// Creates an <see cref="IAIClient"/> instance based on the provider name in the given configuration.
        /// Supports "OpenAI", "Google", and "Anthropic" providers.
        /// </summary>
        /// <param name="config">The client provider configuration specifying the provider name and model.</param>
        /// <returns>An <see cref="IAIClient"/> instance configured for the specified provider and model.</returns>
        /// <exception cref="NotImplementedException">Thrown when the provider name is not recognized.</exception>
        private IAIClient InstantiateClient(ClientProviderConfig config)
        {
            var type = config.Provider;
            var model = config.Model;

            return type switch
            {
                "OpenAI" => new OpenAIClient(_config, model, priority: false),
                "Google" => new GoogleAIClient(_config, model),
                "Anthropic" => new AnthropicAIClient(_config, model),
                _ => throw new NotImplementedException($"{type} IAIClient not implemented."),
            };
        }
    }
}

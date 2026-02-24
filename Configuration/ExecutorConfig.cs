using Microsoft.Extensions.Configuration;

namespace MAKER.Configuration
{
    /// <summary>
    /// Root configuration class for the MAKER executor. Contains all settings needed to initialize
    /// AI provider connections, client assignments for each pipeline stage, and instruction template paths.
    /// </summary>
    public class ExecutorConfig
    {
        /// <summary>
        /// Gets or sets the API keys for the supported AI providers (OpenAI, Google, Anthropic).
        /// </summary>
        public required AIProviderKeysConfig AIProviderKeys { get; set; }

        /// <summary>
        /// Gets or sets the client configurations that map each pipeline stage (planning, voting, execution)
        /// to a specific AI provider and model.
        /// </summary>
        public required ClientsConfig Clients { get; set; }

        /// <summary>
        /// Gets or sets the file paths to the instruction prompt templates used by the planning
        /// and execution orchestrators.
        /// </summary>
        public required InstructionsConfig Instructions { get; set; }

        /// <summary>
        /// Creates an <see cref="ExecutorConfig"/> instance by reading values from an <see cref="IConfigurationSection"/>.
        /// Expects a section structure with "AIProviderKeys", "Clients", and "Instructions" subsections.
        /// </summary>
        /// <param name="section">The configuration section (typically bound to the "MAKER" key in appsettings.json).</param>
        /// <returns>A fully populated <see cref="ExecutorConfig"/> instance.</returns>
        /// <exception cref="InvalidOperationException">Thrown when a required configuration value is missing.</exception>
        public static ExecutorConfig FromConfiguration(IConfigurationSection section)
        {
            var aiKeys = new AIProviderKeysConfig
            {
                Google = section["AIProviderKeys:Google"],
                OpenAI = section["AIProviderKeys:OpenAI"],
                Anthropic = section["AIProviderKeys:Anthropic"]
            };

            var clients = new ClientsConfig
            {
                Planning = new ClientProviderConfig
                {
                    Provider = section["Clients:Planning:Provider"] ?? throw new InvalidOperationException("Planning Provider is required"),
                    Model = section["Clients:Planning:Model"] ?? throw new InvalidOperationException("Planning Model is required")
                },
                PlanVoting = new ClientProviderConfig
                {
                    Provider = section["Clients:PlanVoting:Provider"] ?? throw new InvalidOperationException("PlanVoting Provider is required"),
                    Model = section["Clients:PlanVoting:Model"] ?? throw new InvalidOperationException("PlanVoting Model is required")
                },
                Execution = new ClientProviderConfig
                {
                    Provider = section["Clients:Execution:Provider"] ?? throw new InvalidOperationException("Execution Provider is required"),
                    Model = section["Clients:Execution:Model"] ?? throw new InvalidOperationException("Execution Model is required")
                },
                ExecutionVoting = new ClientProviderConfig
                {
                    Provider = section["Clients:ExecutionVoting:Provider"] ?? throw new InvalidOperationException("ExecutionVoting Provider is required"),
                    Model = section["Clients:ExecutionVoting:Model"] ?? throw new InvalidOperationException("ExecutionVoting Model is required")
                }
            };

            var instructions = new InstructionsConfig
            {
                Plan = section["Instructions:Plan"] ?? throw new InvalidOperationException("Plan instruction path is required"),
                PlanVote = section["Instructions:PlanVote"] ?? throw new InvalidOperationException("PlanVote instruction path is required"),
                PlanRules = section["Instructions:PlanRules"] ?? throw new InvalidOperationException("PlanRules instruction path is required"),
                PlanFormat = section["Instructions:PlanFormat"] ?? throw new InvalidOperationException("PlanFormat instruction path is required"),
                Execute = section["Instructions:Execute"] ?? throw new InvalidOperationException("Execute instruction path is required"),
                ExecuteVote = section["Instructions:ExecuteVote"] ?? throw new InvalidOperationException("ExecuteVote instruction path is required"),
                ExecuteRules = section["Instructions:ExecuteRules"] ?? throw new InvalidOperationException("ExecuteRules instruction path is required")
            };

            return new ExecutorConfig
            {
                AIProviderKeys = aiKeys,
                Clients = clients,
                Instructions = instructions
            };
        }
    }

    /// <summary>
    /// Holds the API keys for each supported AI provider. Keys may be <c>null</c> if the
    /// corresponding provider is not used in the current configuration.
    /// </summary>
    public class AIProviderKeysConfig
    {
        /// <summary>
        /// Gets or sets the Google Gemini API key.
        /// </summary>
        public string? Google { get; set; }

        /// <summary>
        /// Gets or sets the OpenAI API key.
        /// </summary>
        public string? OpenAI { get; set; }

        /// <summary>
        /// Gets or sets the Anthropic Claude API key.
        /// </summary>
        public string? Anthropic { get; set; }
    }

    /// <summary>
    /// Maps each stage of the MAKER pipeline to a specific AI provider and model.
    /// Different stages can use different providers/models to optimize for cost, speed, or quality.
    /// </summary>
    public class ClientsConfig
    {
        /// <summary>
        /// Gets or sets the provider and model used for generating plan steps.
        /// </summary>
        public required ClientProviderConfig Planning { get; set; }

        /// <summary>
        /// Gets or sets the provider and model used for voting on proposed plan steps.
        /// </summary>
        public required ClientProviderConfig PlanVoting { get; set; }

        /// <summary>
        /// Gets or sets the provider and model used for executing plan steps.
        /// </summary>
        public required ClientProviderConfig Execution { get; set; }

        /// <summary>
        /// Gets or sets the provider and model used for voting on execution results.
        /// </summary>
        public required ClientProviderConfig ExecutionVoting { get; set; }
    }

    /// <summary>
    /// Specifies which AI provider and model to use for a particular pipeline stage.
    /// </summary>
    public class ClientProviderConfig
    {
        /// <summary>
        /// Gets or sets the AI provider name (e.g., "OpenAI", "Google", "Anthropic").
        /// Used by the <see cref="MAKER.Executor"/> to instantiate the correct <see cref="AI.Clients.IAIClient"/>.
        /// </summary>
        public required string Provider { get; set; }

        /// <summary>
        /// Gets or sets the specific model identifier to use (e.g., "gpt-5.1", "gemini-2.5-flash", "ClaudeHaiku4_5").
        /// </summary>
        public required string Model { get; set; }
    }

    /// <summary>
    /// Contains the relative file paths to the prompt instruction templates used by the
    /// planning and execution orchestrators. Templates use placeholders like {TASK}, {STEPS}, etc.
    /// that are replaced at runtime with actual values.
    /// </summary>
    public class InstructionsConfig
    {
        /// <summary>
        /// Gets or sets the file path to the main planning prompt template.
        /// </summary>
        public required string Plan { get; set; }

        /// <summary>
        /// Gets or sets the file path to the plan voting prompt template.
        /// </summary>
        public required string PlanVote { get; set; }

        /// <summary>
        /// Gets or sets the file path to the planning rules template (injected into plan prompts).
        /// </summary>
        public required string PlanRules { get; set; }

        /// <summary>
        /// Gets or sets the file path to the plan format specification template.
        /// </summary>
        public required string PlanFormat { get; set; }

        /// <summary>
        /// Gets or sets the file path to the main execution prompt template.
        /// </summary>
        public required string Execute { get; set; }

        /// <summary>
        /// Gets or sets the file path to the execution voting prompt template.
        /// </summary>
        public required string ExecuteVote { get; set; }

        /// <summary>
        /// Gets or sets the file path to the execution rules template (injected into execution prompts).
        /// </summary>
        public required string ExecuteRules { get; set; }
    }
}

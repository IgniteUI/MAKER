using MAKER.AI.Clients;
using MAKER.AI.Exceptions;
using MAKER.AI.Models;
using MAKER.AI.Redflag;
using MAKER.Configuration;
using MAKER.Utils;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MAKER.AI.Orchestrators
{
    /// <summary>
    /// Orchestrates the AI-powered planning phase of the MAKER pipeline. Generates plan steps
    /// in batches using a planning AI client, then validates each batch through a k-margin
    /// consensus voting system using a separate voting AI client. Handles rejections with
    /// automatic retry and feedback to the planning model.
    /// </summary>
    public class PlanningOrchestrator(ExecutorConfig config, IAIClient planningClient, IAIClient planVotingClient)
    {
        #region Events
        /// <summary>
        /// Raised when a batch of proposed steps passes the voting round and is accepted into the plan.
        /// Parameters: (newly accepted steps, previously accepted steps).
        /// </summary>
        public Action<IList<Step>, IList<Step>> OnStepsAccepted { get; set; } = delegate { };

        /// <summary>
        /// Raised when the planning AI produces a batch of proposed steps, before they enter voting.
        /// </summary>
        public Action<IList<Step>> OnStepsProposed { get; set; } = delegate { };

        /// <summary>
        /// Raised whenever an individual vote is received during a plan voting round, providing the current tally.
        /// </summary>
        public Action<VoteState> OnVoteChanged { get; set; } = delegate { };

        /// <summary>
        /// Raised when a batch of proposed steps is rejected by the voting round.
        /// </summary>
        public Action<AIVoteException> OnStepsRejected { get; set; } = delegate { };
        #endregion

        /// <summary>
        /// Gets or sets the default list of red-flag validators applied to planning AI responses.
        /// By default, requires a minimum response length of 100 characters.
        /// </summary>
        public List<IAIRedFlagValidator> DefaultPlanningValidators { get; set; } =
        [
            new AIRedFlagMinLengthValidator(100),
        ];

        /// <summary>
        /// Gets or sets the list of red-flag validators applied to voting AI responses.
        /// By default, requires a minimum response length of 2 characters (enough for "Yes" / "No" / "End").
        /// </summary>
        protected static List<IAIRedFlagValidator> VoteValidators { get; set; } =
        [
            new AIRedFlagMinLengthValidator(2),
        ];

        /// <summary>
        /// Gets or sets the maximum number of consecutive voting rejections before the planner
        /// restarts the planning process from scratch (clearing all accumulated steps).
        /// </summary>
        public int MaxRetries { get; set; } = 5;

        /// <summary>
        /// Generates a complete plan by iteratively requesting step batches from the planning AI,
        /// voting on each batch, and accumulating accepted steps until the AI signals completion ("End").
        /// On voting rejection, the rejection reasons are fed back to the planner. If rejections exceed
        /// <see cref="MaxRetries"/>, the entire plan is discarded and planning restarts from scratch.
        /// </summary>
        /// <param name="prompt">The task description that guides the planning AI.</param>
        /// <param name="format">The output format label injected into the prompt template.</param>
        /// <param name="batchSize">The number of steps to request in each planning batch.</param>
        /// <param name="k">The k-margin threshold for the voting consensus system.</param>
        /// <param name="prependSteps">Optional pre-defined steps to include at the beginning of the plan.</param>
        /// <param name="validators">Optional red-flag validators; defaults to <see cref="DefaultPlanningValidators"/> if null.</param>
        /// <param name="tools">Optional tools object whose methods the planning AI can invoke during generation.</param>
        /// <returns>The complete list of accepted plan steps.</returns>
        public async Task<IList<Step>> Plan(string prompt, string format = "plaintext", int batchSize = 2, int k = 10, IList<Step> prependSteps = null!, List<IAIRedFlagValidator> validators = null!, object? tools = null!)
        {
            var step = string.Empty;
            AIVoteException lastRejection = null!;
            int votingRetryCount = 0;


            if (string.IsNullOrEmpty(prompt))
            {
                throw new ArgumentNullException($"{nameof(prompt)} must be a non-empty string");
            }

            var steps = new List<Step>();
            if (prependSteps != null && prependSteps.Count > 0)
            {
                steps.AddRange(prependSteps);
            }

            while (step != "End")
            {
                try
                {
                    var proposedSteps = await PlanInternal(prompt, steps, batchSize, format, k, validators, lastRejection!, tools);
                    OnStepsAccepted?.Invoke(proposedSteps, [.. steps]);

                    foreach (var stepObj in proposedSteps)
                    {
                        step = stepObj.Task;
                        if (!string.IsNullOrEmpty(step) && step != "End")
                        {
                            steps.Add(stepObj);
                        }

                        if (step == "End")
                        {
                            break;
                        }
                    }
                    
                    // Reset retry count on successful planning
                    votingRetryCount = 0;
                }
                catch (AIVoteException ex)
                {
                    if (ex.Reason == VoteCancellationReason.Rejected)
                    {
                        lastRejection = ex;
                        votingRetryCount++;
                        
                        // If max retries exceeded, restart planning from scratch
                        if (votingRetryCount >= MaxRetries)
                        {
                            steps.Clear();
                            if (prependSteps != null && prependSteps.Count > 0)
                            {
                                steps.AddRange(prependSteps);
                            }
                            lastRejection = null!;
                            votingRetryCount = 0;
                            step = string.Empty;
                        }
                    }
                    continue;
                }
                catch (AIRedFlagException ex)
                {
                    lastRejection = new(ex.Message, VoteCancellationReason.Rejected);
                    continue;
                }
                lastRejection = null!;
            }

            return steps;
        }

        /// <summary>
        /// Generates a single batch of plan steps by rendering the planning prompt template,
        /// sending it to the planning AI, deserializing the JSON response into <see cref="Step"/> objects,
        /// and submitting them for voting approval.
        /// </summary>
        /// <param name="task">The task description for the prompt template.</param>
        /// <param name="steps">The steps already accepted in the plan (provided as context to the AI).</param>
        /// <param name="batchSize">The number of steps to request in this batch.</param>
        /// <param name="format">The output format label for the prompt template.</param>
        /// <param name="k">The k-margin threshold for voting.</param>
        /// <param name="validators">Red-flag validators to apply to the AI response.</param>
        /// <param name="lastRejection">The previous rejection exception, if any, whose reasons are fed back to the AI.</param>
        /// <param name="tools">Optional tools object for the planning AI.</param>
        /// <returns>The list of proposed steps that passed the voting round.</returns>
        /// <exception cref="AIRedFlagException">Thrown when the AI response is malformed or fails validation.</exception>
        /// <exception cref="AIVoteException">Thrown when the proposed steps are rejected by the voting system.</exception>
        public async Task<List<Step>> PlanInternal(string task, IEnumerable<Step> steps, int batchSize = 2, string format = "plaintext", int k = 5, List<IAIRedFlagValidator> validators = null!, AIVoteException lastRejection = null!, object? tools = null!)
        {
            var planTemplate = await ReadPromptTemplate(config.Instructions.Plan);
            var planFormat = await ReadPromptTemplate(config.Instructions.PlanFormat);
            var rules = await ReadPromptTemplate(config.Instructions.PlanRules);

            var prompt = planTemplate
                .Replace("{TASK}", task)
                .Replace("{STEPS}", string.Join(Environment.NewLine, steps.Select(s => "- " + s.Task)))
                .Replace("{PLAN_RULES}", rules)
                .Replace("{OUTPUT_FORMAT}", format)
                .Replace("{PLAN_FORMAT}", planFormat)
                .Replace("{BATCH_SIZE}", batchSize.ToString());

            if (lastRejection != null)
            {
                var rejectedSteps = string.Join(Environment.NewLine, lastRejection.ProposedSteps.Select(s => " - " + s.Task));
                var rejectionReasons = string.Join(Environment.NewLine, lastRejection.RejectionReasons.Select(r => "- " + r));
                prompt = prompt.Replace("{LAST_REJECTION}", $"The last rejected Steps were:{Environment.NewLine}{rejectedSteps}{Environment.NewLine}Reasons:{Environment.NewLine}{rejectionReasons}");
            }

            prompt = ClearUnusedTemplateVariables(prompt);

            validators ??= DefaultPlanningValidators;

            var responseObj = await planningClient.GuardedRequest(prompt, validators, tools);
            var response = responseObj.Content ?? throw new AIRedFlagException("Received null response from the model.");

            if (response == "End")
            {
                response = JsonSerializer.Serialize(new List<Step>() { new() { Task = "End", RequiredSteps = [] } });
            }

            var deserializedSteps = new List<Step>();

            try
            {
                deserializedSteps = JsonSerializer.Deserialize<List<Step>>(response);
            }
            catch (JsonException)
            {
                try
                {
                    var singleStep = JsonSerializer.Deserialize<Step>(response);
                    if (singleStep != null)
                    {
                        deserializedSteps!.Add(singleStep);
                    }
                    else
                    {
                        throw new AIRedFlagException("Invalid Step format.");
                    }
                }
                catch (JsonException)
                {
                    throw new AIRedFlagException("Invalid Step format.");
                }
            }

            if (deserializedSteps == null || deserializedSteps.Count == 0)
            {
                throw new AIRedFlagException("Deserialized plan is empty.");
            }

            try
            {
                OnStepsProposed?.Invoke(deserializedSteps);
                var (vote, reasons, usage) = await VotePlanInternal(task, deserializedSteps, steps, batchSize, k);
                if (!vote)
                {
                    var rejection = new AIVoteException($"Proposed step was rejected by voting.", deserializedSteps, reasons);
                    OnStepsRejected?.Invoke(rejection);
                    throw rejection;
                }
            }
            catch (AIVoteException ex)
            {
                if (ex.Reason == VoteCancellationReason.Ended)
                {
                    return [new() { Task = "End", RequiredSteps = [] }];
                }
                else
                {
                    throw;
                }
            }

            return deserializedSteps;
        }

        /// <summary>
        /// Submits proposed plan steps to the voting AI for approval. Renders the vote prompt template
        /// and delegates to <see cref="RunVotingRound"/> to collect and tally votes.
        /// </summary>
        /// <param name="task">The task description for context in the vote prompt.</param>
        /// <param name="proposed">The proposed steps being voted on.</param>
        /// <param name="steps">The steps already accepted in the plan (for context).</param>
        /// <param name="batchSize">The batch size used in planning (included in the vote prompt).</param>
        /// <param name="k">The k-margin threshold for voting consensus.</param>
        /// <param name="tools">Optional tools object for the voting AI.</param>
        /// <returns>A tuple of (approved, rejection reasons, token usage).</returns>
        public async Task<(bool, IEnumerable<string>, AIResponse)> VotePlanInternal(string task, IEnumerable<Step> proposed, IEnumerable<Step> steps, int batchSize = 2, int k = 5, object? tools = null)
        {
            var voteTemplate = await ReadPromptTemplate(config.Instructions.PlanVote);
            var planFormat = await ReadPromptTemplate(config.Instructions.PlanFormat);
            var rules = await ReadPromptTemplate(config.Instructions.PlanRules);

            var prompt = voteTemplate
                .Replace("{TASK}", task)
                .Replace("{STEP}", string.Join(Environment.NewLine, proposed.Select(s => $"  <step>{s.Task}</step>")))
                .Replace("{STEPS}", string.Join(Environment.NewLine, steps.Select(s => $"  <step>{s.Task}</step>")))
                .Replace("{PLAN_RULES}", rules)
                .Replace("{BATCH_SIZE}", batchSize.ToString())
                .Replace("{PLAN_FORMAT}", planFormat);

            prompt = ClearUnusedTemplateVariables(prompt);

            try
            {
                var (vote, reasons, usage) = await this.RunVotingRound(k, prompt, planVotingClient, tools);
                return (vote, reasons, usage);
            }
            catch
            {
                var (vote, reasons, usage) = await this.RunVotingRound(k, prompt, planVotingClient, tools);
                return (vote, reasons, usage);
            }
        }

        /// <summary>
        /// Runs a k-margin consensus voting round by sending parallel vote requests to the voting AI.
        /// Votes are tallied as they arrive (using completion-order interleaving). The round ends when
        /// "Yes" leads "No" by k votes, "No" leads "Yes" by k votes, or all k votes are "End".
        /// If the total vote count exceeds 4×k without consensus, a contentious exception is thrown.
        /// </summary>
        /// <param name="k">The k-margin threshold for consensus.</param>
        /// <param name="prompt">The rendered vote prompt to send to each voting request.</param>
        /// <param name="client">The AI client to use for voting.</param>
        /// <param name="tools">Optional tools object for the voting AI.</param>
        /// <returns>A tuple of (approved, rejection reasons, cumulative token usage).</returns>
        /// <exception cref="AIVoteException">Thrown when voting ends (all "End" votes) or exceeds the maximum vote count.</exception>
        private async Task<(bool, IEnumerable<string>, AIResponse)> RunVotingRound(int k, string prompt, IAIClient client, object? tools = null)
        {
            int positive = 0;
            int negative = 0;
            int end = 0;

            var reasons = new List<string>();

            int inputTokens = 0;
            int outputTokens = 0;

            while (positive < negative + k && negative < positive + k && end != k)
            {
                end = 0;
                if (positive + negative >= k * 4)
                {
                    throw new AIVoteException("Voting round exceeded maximum number of votes without reaching consensus.", VoteCancellationReason.Contentious);
                }

                var votes = GenerateVoteRequests(prompt, k, client, tools);
                foreach (var bucket in TaskUtils.Interleaved(votes))
                {
                    var t = await bucket;
                    var voteResponseObj = await t;

                    inputTokens += voteResponseObj.InputTokens;
                    outputTokens += voteResponseObj.OutputTokens;
                    var voteResponse = voteResponseObj.Content!.ReplaceLineEndings().Trim();

                    if (voteResponse != null)
                    {
                        if (voteResponse == "Yes")
                        {
                            positive++;
                        }
                        else if (voteResponse.StartsWith("No"))
                        {
                            negative++;
                            var resp = voteResponse.ReplaceLineEndings().Split(Environment.NewLine);
                            if (resp.Length > 1)
                            {
                                reasons.Add(string.Join(Environment.NewLine, resp.Skip(1)));
                            }
                        }
                        else if (voteResponse == "End")
                        {
                            end++;
                        }
                        else
                        {
                            // Some other non-value
                            votes.AddRange(GenerateVoteRequests(prompt, 1, client, tools));
                            continue;
                        }

                        OnVoteChanged?.Invoke(new()
                        {
                            KValue = k,
                            Votes = {
                                { "Yes", positive },
                                { "No", negative },
                                { "End", end }
                            }
                        });
                        if (positive >= negative + k || negative >= positive + k || end == k)
                        {
                            break;
                        }
                    }
                    else
                    {
                        // Handle unexpected response
                        throw new Exception("Unexpected response from vote.");
                    }
                }
            }

            if (end == k)
            {
                throw new AIVoteException("Voters deemed the task finished.", VoteCancellationReason.Ended);
            }

            return (positive >= negative + k, reasons, new AIResponse
            {
                InputTokens = inputTokens,
                OutputTokens = outputTokens
            });
        }

        /// <summary>
        /// Creates the specified number of parallel vote request tasks using the given AI client and prompt.
        /// Each request is sent through <see cref="IAIClientExtensions.GuardedRequest"/> with vote validators.
        /// </summary>
        /// <param name="prompt">The vote prompt to send.</param>
        /// <param name="amount">The number of parallel vote requests to create.</param>
        /// <param name="client">The AI client to use for voting.</param>
        /// <param name="tools">Optional tools object for the voting AI.</param>
        /// <returns>A list of tasks representing the pending vote responses.</returns>
        private List<Task<AIResponse>> GenerateVoteRequests(string prompt, int amount, IAIClient client, object? tools = null)
        {
            var output = new List<Task<AIResponse>>();
            for (int i = 0; i < amount; i++)
            {
                output.Add(client.GuardedRequest(prompt, VoteValidators, tools));
            }

            return output;
        }

        /// <summary>
        /// Reads a prompt template file from disk. The path is resolved relative to the current working directory.
        /// </summary>
        /// <param name="path">The relative file path to the prompt template.</param>
        /// <returns>The full text content of the template file.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the template file does not exist.</exception>
        private async Task<string> ReadPromptTemplate(string path)
        {
            path = Path.Combine(Directory.GetCurrentDirectory(), path);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                throw new FileNotFoundException($"Prompt template file not found: {path}");
            }
            return await File.ReadAllTextAsync(path);
        }

        /// <summary>
        /// Removes any unreplaced template placeholders (e.g., {LAST_REJECTION}, {EXTRA_CONTEXT})
        /// from the prompt string. Placeholders follow the pattern <c>{UPPER_CASE_NAME}</c>.
        /// </summary>
        /// <param name="prompt">The prompt string potentially containing unreplaced placeholders.</param>
        /// <returns>The prompt with all unreplaced placeholders removed.</returns>
        private string ClearUnusedTemplateVariables(string prompt)
        {
            var regex = new Regex(@"\{[A-Z_]+\}");
            return regex.Replace(prompt, string.Empty);
        }
    }
}

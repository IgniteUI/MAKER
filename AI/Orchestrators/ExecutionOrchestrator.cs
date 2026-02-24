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
    /// Orchestrates the AI-powered execution phase of the MAKER pipeline. Processes plan steps
    /// in batches, sends each batch to an execution AI client with the accumulated state, and
    /// validates the output through a k-margin consensus voting system. Handles rejections with
    /// automatic retry and feedback to the execution model.
    /// </summary>
    public class ExecutionOrchestrator(ExecutorConfig config, IAIClient executionClient, IAIClient executionVotingClient)
    {

        #region Events
        /// <summary>
        /// Raised when execution of a step batch begins.
        /// Parameters: (current batch steps, previously completed steps).
        /// </summary>
        public Action<IList<Step>, IList<Step>> OnExecutionStarted { get; set; } = delegate { };

        /// <summary>
        /// Raised when the cumulative execution state is updated after a batch completes.
        /// The string parameter contains the full current state output from the AI.
        /// </summary>
        public Action<string> OnStateChanged { get; set; } = delegate { };

        /// <summary>
        /// Raised whenever an individual vote is received during an execution voting round,
        /// providing the current tally of Yes/No/End votes.
        /// </summary>
        public Action<VoteState> OnVoteChanged { get; set; } = delegate { };
        #endregion

        /// <summary>
        /// Gets or sets the maximum number of consecutive voting rejections before the executor
        /// resets the state and attempts execution from scratch.
        /// </summary>
        public int MaxRetries { get; set; } = 5;

        /// <summary>
        /// Gets or sets the list of red-flag validators applied to voting AI responses.
        /// By default, requires a minimum response length of 2 characters.
        /// </summary>
        protected static List<IAIRedFlagValidator> VoteValidators { get; set; } =
        [
            new AIRedFlagMinLengthValidator(2),
        ];

        /// <summary>
        /// Executes all plan steps in batches, accumulating state progressively.
        /// Each batch is processed through <see cref="ExecuteSteps"/> which handles voting and retries.
        /// Fires <see cref="OnExecutionStarted"/> before each batch and <see cref="OnStateChanged"/> after.
        /// </summary>
        /// <param name="steps">The full list of plan steps to execute.</param>
        /// <param name="prompt">The task description that guides the execution AI.</param>
        /// <param name="format">The output format label injected into prompts for steps that require it.</param>
        /// <param name="batchSize">The number of steps to process in each execution batch.</param>
        /// <param name="k">The k-margin threshold for the voting consensus system.</param>
        /// <param name="validators">Optional red-flag validators for execution AI responses.</param>
        /// <param name="tools">Optional tools object whose methods the execution AI can invoke.</param>
        /// <returns>The final accumulated state string after all steps have been executed.</returns>
        public async Task<string> Execute(IList<Step> steps, string prompt, string format, int batchSize = 2, int k = 10, List<IAIRedFlagValidator> validators = null!, object? tools = null!)
        {
            var completedSteps = new List<Step>();
            var stepsList = steps.ToList();
            var state = string.Empty;

            if (stepsList.Count > 0)
            {
                var totalBatches = (int)Math.Ceiling(stepsList.Count / (double)batchSize);

                for (int batchIndex = 0; batchIndex < totalBatches; batchIndex++)
                {
                    var batchSteps = stepsList.Skip(batchIndex * batchSize).Take(batchSize).ToList();

                    OnExecutionStarted?.Invoke(batchSteps, completedSteps);
                    state = await ExecuteSteps(prompt, format, state, batchSteps, k, validators, tools);

                    OnStateChanged?.Invoke(state);

                    completedSteps.AddRange(batchSteps);
                }
            }

            return state;
        }

        /// <summary>
        /// Executes a single batch of steps by rendering the execution prompt template with the current
        /// state, sending it to the execution AI, and then validating the result through voting.
        /// Injects output format, extra context, and rejection feedback as applicable.
        /// </summary>
        /// <param name="task">The task description for the prompt template.</param>
        /// <param name="state">The current accumulated state from previous step executions.</param>
        /// <param name="format">The output format specification (injected when steps require it).</param>
        /// <param name="steps">The batch of steps to execute.</param>
        /// <param name="k">The k-margin threshold for voting consensus.</param>
        /// <param name="validators">Red-flag validators for the execution AI response.</param>
        /// <param name="tools">Optional tools object for the execution AI.</param>
        /// <param name="lastRejection">The previous rejection exception, if any, whose reasons are fed back to the AI.</param>
        /// <returns>The updated state string after executing the step batch.</returns>
        /// <exception cref="AIRedFlagException">Thrown when the execution AI returns an empty response.</exception>
        /// <exception cref="AIVoteException">Thrown when the execution result is rejected by the voting system.</exception>
        public async Task<string> ExecuteStepsInternal(string task, string state, string format, IEnumerable<Step> steps, int k = 5, List<IAIRedFlagValidator> validators = null!, object? tools = null!, AIVoteException lastRejection = null!)
        {
            var executionTemplate = await ReadPromptTemplate(config.Instructions.Execute);
            var rules = await ReadPromptTemplate(config.Instructions.ExecuteRules);

            var prompt = executionTemplate
                .Replace("{TASK}", task)
                .Replace("{STEP}", string.Join(Environment.NewLine, steps.Select(s => $"  <step>{s.Task}</step>")))
                .Replace("{STATE}", state)
                .Replace("{RULES}", rules);

            if (!string.IsNullOrEmpty(format) && steps.Any(s => s.RequiresFormat))
            {
                prompt = prompt.Replace("{OUTPUT_FORMAT}", "Required output format:" + Environment.NewLine + format);
            }

            if (steps.Any(s => !string.IsNullOrEmpty(s.ExtraContext)))
            {
                prompt = prompt.Replace("{EXTRA_CONTEXT}", string.Join(Environment.NewLine, steps.Select(s => s.ExtraContext)));
            }

            if (lastRejection != null)
            {
                var rejectionReasons = string.Join(Environment.NewLine, lastRejection.RejectionReasons.Select(r => "- " + r));
                prompt = prompt.Replace("{LAST_REJECTION}", $"The last rejected execution had the following reasons:{Environment.NewLine}{rejectionReasons}");
            }

            prompt = ClearUnusedTemplateVariables(prompt);

            var response = await executionClient.GuardedRequest(prompt, validators ?? [], tools);
            if (string.IsNullOrEmpty(response.Content))
            {
                throw new AIRedFlagException("Execution client returned empty response.");
            }

            var (vote, reasons) = await VoteExecutionInternal(task, steps, response.Content, state, k, tools);
            if (!vote)
            {
                throw new AIVoteException($"Proposed step was rejected by voting.", steps, reasons);
            }

            return response.Content;
        }

        /// <summary>
        /// Submits an execution result to the voting AI for approval. Renders the execution vote
        /// prompt template with the current and previous states and delegates to <see cref="RunVotingRound"/>.
        /// </summary>
        /// <param name="task">The task description for context in the vote prompt.</param>
        /// <param name="proposed">The steps that were executed in this batch.</param>
        /// <param name="state">The new state produced by the execution AI.</param>
        /// <param name="prevState">The state before this execution batch (for comparison by voters).</param>
        /// <param name="k">The k-margin threshold for voting consensus.</param>
        /// <param name="tools">Optional tools object for the voting AI.</param>
        /// <returns>A tuple of (approved, rejection reasons).</returns>
        public async Task<(bool, IEnumerable<string>)> VoteExecutionInternal(string task, IEnumerable<Step> proposed, string state, string prevState, int k = 5, object? tools = null!)
        {
            var voteTemplate = await ReadPromptTemplate(config.Instructions.ExecuteVote);
            var rules = await ReadPromptTemplate(config.Instructions.ExecuteRules);

            var prompt = voteTemplate
                .Replace("{TASK}", task)
                .Replace("{STEP}", JsonSerializer.Serialize(proposed.Select(s => s.Task)))
                .Replace("{STATE}", state)
                .Replace("{CURRENT_STATE}", prevState)
                .Replace("{RULES}", rules);

            prompt = ClearUnusedTemplateVariables(prompt);

            try
            {
                var (vote, reasons) = await this.RunVotingRound(k, prompt, executionVotingClient, tools);
                return (vote, reasons);
            }
            catch
            {
                var (vote, reasons) = await this.RunVotingRound(k, prompt, executionVotingClient, tools);
                return (vote, reasons);
            }
        }

        /// <summary>
        /// Wrapper around <see cref="ExecuteStepsInternal"/> that implements retry logic.
        /// On voting rejection, retries with the rejection feedback up to <see cref="MaxRetries"/> times.
        /// If max retries are exceeded, resets the state and makes one final attempt.
        /// </summary>
        /// <param name="prompt">The task description for the execution prompt.</param>
        /// <param name="format">The output format label.</param>
        /// <param name="state">The current accumulated state.</param>
        /// <param name="steps">The batch of steps to execute.</param>
        /// <param name="k">The k-margin threshold for voting.</param>
        /// <param name="validators">Red-flag validators for execution responses.</param>
        /// <param name="tools">Optional tools object for the execution AI.</param>
        /// <param name="lastRejection">The previous rejection exception, if any.</param>
        /// <returns>The updated state string after successful execution and voting approval.</returns>
        private async Task<string> ExecuteSteps(string prompt, string format, string state, IEnumerable<Step> steps, int k, List<IAIRedFlagValidator> validators = null!, object? tools = null!, AIVoteException lastRejection = null!)
        {
            int votingRetryCount = 0;
            var currentState = state;
            
            while (votingRetryCount < MaxRetries)
            {
                try
                {
                    return await ExecuteStepsInternal(prompt, currentState, format, steps, k, validators, tools, lastRejection);
                }
                catch (AIVoteException ex)
                {
                    lastRejection = ex;
                    votingRetryCount++;

                    // If max retries exceeded, reset state and retry from scratch
                    if (votingRetryCount >= MaxRetries)
                    {
                        lastRejection = null!;
                        currentState = string.Empty;
                        return await ExecuteStepsInternal(prompt, currentState, format, steps, k, validators, tools, null!);
                    }
                }
            }
            
            // This should not be reached due to the final attempt above, but included for safety
            return await ExecuteStepsInternal(prompt, currentState, format, steps, k, validators, lastRejection);
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
        /// <returns>A tuple of (approved, rejection reasons).</returns>
        /// <exception cref="AIVoteException">Thrown when voting ends or exceeds the maximum vote count.</exception>
        private async Task<(bool, IEnumerable<string>)> RunVotingRound(int k, string prompt, IAIClient client, object? tools = null!)
        {
            int positive = 0;
            int negative = 0;
            int end = 0;

            var reasons = new List<string>();

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

            return (positive >= negative + k, reasons);
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
        private List<Task<AIResponse>> GenerateVoteRequests(string prompt, int amount, IAIClient client, object? tools = null!)
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

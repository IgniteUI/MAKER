using MAKER.AI.Models;

namespace MAKER.AI.Exceptions
{
    /// <summary>
    /// Exception thrown when a voting round results in a rejection, early termination, or a contentious
    /// outcome. Carries the proposed steps, rejection reasons, and the cancellation reason for retry logic.
    /// </summary>
    public class AIVoteException : Exception
    {
        /// <summary>
        /// Gets the reason the voting round was cancelled or failed (rejected, ended, or contentious).
        /// </summary>
        public VoteCancellationReason Reason { get; private set; } = VoteCancellationReason.Rejected;

        /// <summary>
        /// Gets the collection of human-readable reasons provided by "No" voters explaining why
        /// the proposed steps were rejected.
        /// </summary>
        public IEnumerable<string> RejectionReasons { get; private set; } = [];

        /// <summary>
        /// Gets the collection of steps that were proposed and subsequently rejected by the vote.
        /// </summary>
        public IEnumerable<Step> ProposedSteps { get; private set; } = [];

        /// <summary>
        /// Initializes a new instance of the <see cref="AIVoteException"/> class.
        /// </summary>
        public AIVoteException() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="AIVoteException"/> class with a specific cancellation reason.
        /// </summary>
        /// <param name="reason">The reason the vote was cancelled.</param>
        public AIVoteException(VoteCancellationReason reason) {
            Reason = reason;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AIVoteException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">A description of the vote failure.</param>
        public AIVoteException(string message) : base(message) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="AIVoteException"/> class with the proposed steps
        /// and the reasons they were rejected by the voting agents.
        /// </summary>
        /// <param name="message">A description of the vote failure.</param>
        /// <param name="proposedSteps">The steps that were proposed and rejected.</param>
        /// <param name="rejectionReasons">The reasons provided by "No" voters.</param>
        public AIVoteException(string message, IEnumerable<Step> proposedSteps, IEnumerable<string> rejectionReasons) : base(message) {
            ProposedSteps = proposedSteps;
            RejectionReasons = rejectionReasons;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AIVoteException"/> class with a message and cancellation reason.
        /// </summary>
        /// <param name="message">A description of the vote failure.</param>
        /// <param name="reason">The reason the vote was cancelled.</param>
        public AIVoteException(string message, VoteCancellationReason reason) : base(message) {
            Reason = reason;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AIVoteException"/> class with a message and inner exception.
        /// </summary>
        /// <param name="message">A description of the vote failure.</param>
        /// <param name="inner">The exception that caused this exception.</param>
        public AIVoteException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Specifies the reason a voting round was cancelled or concluded.
    /// </summary>
    public enum VoteCancellationReason
    {
        /// <summary>
        /// The proposed steps were rejected by the voting agents ("No" votes reached the k-margin threshold).
        /// </summary>
        Rejected,

        /// <summary>
        /// The voting agents determined the overall task is complete (all k votes were "End").
        /// </summary>
        Ended,

        /// <summary>
        /// The voting round exceeded the maximum number of votes without reaching consensus.
        /// </summary>
        Contentious
    }
}

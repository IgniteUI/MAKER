namespace MAKER.AI.Models
{
    /// <summary>
    /// Represents the current state of a voting round, tracking the number of votes
    /// received for each category ("Yes", "No", "End") and the k-margin threshold.
    /// </summary>
    public class VoteState
    {
        /// <summary>
        /// Gets the k-margin value used to determine voting consensus.
        /// A vote category wins when it leads the opposing category by at least <c>KValue</c> votes.
        /// </summary>
        public required int KValue { get; init; }

        /// <summary>
        /// Gets the dictionary of vote tallies keyed by category name ("Yes", "No", "End").
        /// Each value represents the current count of votes received for that category.
        /// </summary>
        public Dictionary<string, int> Votes { get; init; } = [];
    }
}

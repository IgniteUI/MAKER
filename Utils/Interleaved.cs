namespace MAKER.Utils
{
    /// <summary>
    /// Provides utility methods for working with collections of asynchronous tasks.
    /// </summary>
    public class TaskUtils
    {
        /// <summary>
        /// Returns tasks in the order they complete rather than the order they were started.
        /// This enables processing results as soon as any task finishes, rather than waiting
        /// for them sequentially. Used by the voting system to tally votes as they arrive.
        /// </summary>
        /// <typeparam name="T">The result type of the tasks.</typeparam>
        /// <param name="tasks">The collection of tasks to interleave by completion order.</param>
        /// <returns>
        /// An array of tasks that complete in the order the input tasks finish.
        /// Awaiting the first element yields the first task to complete, and so on.
        /// </returns>
        public static Task<Task<T>>[] Interleaved<T>(IEnumerable<Task<T>> tasks)
        {
            var inputTasks = tasks.ToList();

            var buckets = new TaskCompletionSource<Task<T>>[inputTasks.Count];
            var results = new Task<Task<T>>[buckets.Length];
            for (int i = 0; i < buckets.Length; i++)
            {
                buckets[i] = new TaskCompletionSource<Task<T>>();
                results[i] = buckets[i].Task;
            }

            int nextTaskIndex = -1;
            Action<Task<T>> continuation = completed =>
            {
                var bucket = buckets[Interlocked.Increment(ref nextTaskIndex)];
                bucket.TrySetResult(completed);
            };

            foreach (var inputTask in inputTasks)
                inputTask.ContinueWith(continuation, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            return results;
        }
    }
}

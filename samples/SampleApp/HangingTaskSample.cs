using System;
using System.Threading.Tasks;

namespace SampleApp
{
    internal class HangingTaskSample
    {
        internal static async Task<int> RunAsync()
        {
            Console.WriteLine("Starting task...");
            var tcs = new TaskCompletionSource<int>();
            return await tcs.Task;
        }
    }
}

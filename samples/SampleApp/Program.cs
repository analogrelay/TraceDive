using System;
using System.Threading.Tasks;

namespace SampleApp
{
    internal class Program
    {
        private static async Task<int> Main(string[] args)
        {
            Console.WriteLine("Press a key to launch the corresponding sample");
            Console.WriteLine("* Async (H)anging Task");
            Console.WriteLine("* E(x)it");

            var key = Console.ReadKey();
            switch (key.Key)
            {
                case ConsoleKey.H:
                    return await HangingTaskSample.RunAsync();
                default:
                    Console.Error.WriteLine("Unknown sample");
                    return 1;
                case ConsoleKey.X:
                    return 0;
            }
        }
    }
}

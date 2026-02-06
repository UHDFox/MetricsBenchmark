using MetricsBench;
using MetricsBench.Collectors;
using MetricsBenchmark.Models;
using MetricsBenchmark.Services;

namespace MetricsBenchmark
{
    internal class Program
    {
        static void PrintUsage()
        {
            Console.WriteLine("Usage: dotnet run -- [procfs|hybrid] [iterations=50] [intervalMs=1000] [--vms] [--threads] [--io]");
        }

        static void Main(string[] args)
        {
            if (args.Length == 0) { PrintUsage(); return; }

            var mode = args[0];
            int iterations = args.Length > 1 && int.TryParse(args[1], out var it) ? it : 50;
            int intervalMs = args.Length > 2 && int.TryParse(args[2], out var ms) ? ms : 1000;

            var opts = new CollectorOptions
            {
                IncludeVms = args.Contains("--vms"),
                IncludeThreads = args.Contains("--threads"),
                IncludeReadBytes = args.Contains("--io"),
            };

            IProcessCollector collector = mode switch
            {
                "procfs" => new ProcFsCollector(opts),
                "hybrid" => new HybridCollector(opts),
                _ => throw new ArgumentException("Unknown mode")
            };

            var runner = new BenchmarkRunner();
            var results = runner.Run(collector, iterations, TimeSpan.FromMilliseconds(intervalMs));

            Console.WriteLine($"Collector: {collector.Name}");
            Console.WriteLine("iter,procs,snapshot_ms,metrics_ms,alloc_bytes");

            foreach (var r in results)
            {
                Console.WriteLine($"{r.Iteration},{r.Processes},{r.SnapshotMs},{r.MetricsMs},{r.AllocBytes}");
            }
        }
    }
}

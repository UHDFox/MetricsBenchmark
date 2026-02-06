using MetricsBench;
using MetricsBench.Collectors;
using MetricsBenchmark.Models;
using MetricsBenchmark.Services;
using MetricsBenchmark.Models.Results;

namespace MetricsBenchmark
{
    internal class Program
    {
        static void PrintUsage()
        {
            Console.WriteLine("Usage: dotnet run -- [procfs|hybrid] [iterations=50] [intervalMs=1000] [--vms] [--threads] [--io]");
        }

        static void PrintSummary(string name, IReadOnlyList<IterationResult> results)
        {
            var n = results.Count;
            if (n == 0)
            {
                Console.WriteLine("No results.");
                return;
            }

            long[] snap = new long[n];
            long[] met = new long[n];
            long[] tot = new long[n];
            long[] alloc = new long[n];
            long procSum = 0;

            for (int i = 0; i < n; i++)
            {
                var r = results[i];
                snap[i] = r.SnapshotMs;
                met[i] = r.MetricsMs;
                tot[i] = r.TotalMs;
                alloc[i] = r.AllocBytes;
                procSum += r.Processes;
            }

            double avgProcs = procSum / (double)n;

            var sSnap = Stats.SummarizeLong(snap);
            var sMet  = Stats.SummarizeLong(met);
            var sTot  = Stats.SummarizeLong(tot);
            var sAlloc = Stats.SummarizeLong(alloc);

            double avgTotalSec = sTot.Mean / 1000.0;
            double procsPerSec = avgTotalSec > 0 ? avgProcs / avgTotalSec : 0;

            Console.WriteLine();
            Console.WriteLine($"==== {name} summary ====");
            Console.WriteLine($"Iterations: {n}, Avg processes: {avgProcs:F0}");
            Console.WriteLine();

            static string Line(string label, Stats.Summary s) =>
                $"{label,-10} mean {s.Mean,7:F2} ms | med {s.Median,6:F0} ms | p95 {s.P95,6:F0} ms | max {s.Max,6:F0} ms";

            Console.WriteLine(Line("snapshot", sSnap));
            Console.WriteLine(Line("metrics",  sMet));
            Console.WriteLine(Line("total",    sTot));
            Console.WriteLine();
            Console.WriteLine($"alloc      mean {sAlloc.Mean/1024.0,7:F1} KB | p95 {sAlloc.P95/1024.0,6:F1} KB | max {sAlloc.Max/1024.0,6:F1} KB");
            Console.WriteLine($"throughput ~ {procsPerSec:F0} processes/sec (avg)");
            Console.WriteLine("========================");
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

            // Console.WriteLine($"Collector: {collector.Name}");
            // Console.WriteLine("iter,procs,snapshot_ms,metrics_ms,alloc_bytes");

            // foreach (var r in results)
            // {
            //     Console.WriteLine($"{r.Iteration},{r.Processes},{r.SnapshotMs},{r.MetricsMs},{r.AllocBytes}");
            // }

            PrintSummary(collector.Name, results);
        }
    }
}

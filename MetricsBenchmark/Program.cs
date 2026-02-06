using MetricsBench;
using MetricsBench.Collectors;
using MetricsBenchmark.Models;
using MetricsBenchmark.Models.Results; // IterationResult
using MetricsBenchmark.Services;       // BenchmarkRunner, Stats (если у тебя тут)
using System;

namespace MetricsBenchmark
{
    internal class Program
    {
        static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run --                      (default: compare with defaults)");
            Console.WriteLine("  dotnet run -- compare [iterations=40] [intervalMs=500] [--vms] [--threads] [--io]");
            Console.WriteLine("  dotnet run -- [procfs|hybrid] [iterations=40] [intervalMs=500] [--vms] [--threads] [--io] [--csv]");
        }

        static (int iterations, int intervalMs, CollectorOptions opts, bool csv) ParseCommon(string[] args, int startIndex)
        {
            int iterations = args.Length > startIndex && int.TryParse(args[startIndex], out var it) ? it : 50;
            int intervalMs = args.Length > startIndex + 1 && int.TryParse(args[startIndex + 1], out var ms) ? ms : 1000;

            var opts = new CollectorOptions
            {
                IncludeVms = Array.IndexOf(args, "--vms") >= 0,
                IncludeThreads = Array.IndexOf(args, "--threads") >= 0,
                IncludeReadBytes = Array.IndexOf(args, "--io") >= 0,
            };

            bool csv = Array.IndexOf(args, "--csv") >= 0;
            return (iterations, intervalMs, opts, csv);
        }

        // --- Summary helpers (как у тебя было) ---
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
            var sMet = Stats.SummarizeLong(met);
            var sTot = Stats.SummarizeLong(tot);
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
            Console.WriteLine(Line("metrics", sMet));
            Console.WriteLine(Line("total", sTot));
            Console.WriteLine();
            Console.WriteLine($"alloc      mean {sAlloc.Mean / 1024.0,7:F1} KB | p95 {sAlloc.P95 / 1024.0,6:F1} KB | max {sAlloc.Max / 1024.0,6:F1} KB");
            Console.WriteLine($"throughput ~ {procsPerSec:F0} processes/sec (avg)");
            Console.WriteLine("========================");
        }

        static void PrintDelta(string aName, IReadOnlyList<IterationResult> a, string bName, IReadOnlyList<IterationResult> b)
        {
            // delta делаем по total mean/p95 и т.п.
            // Для простоты используем те же SummarizeLong, как в PrintSummary.

            static (Stats.Summary snap, Stats.Summary met, Stats.Summary tot, Stats.Summary alloc) Summ(IReadOnlyList<IterationResult> r)
            {
                int n = r.Count;
                long[] snap = new long[n];
                long[] met = new long[n];
                long[] tot = new long[n];
                long[] alloc = new long[n];

                for (int i = 0; i < n; i++)
                {
                    snap[i] = r[i].SnapshotMs;
                    met[i] = r[i].MetricsMs;
                    tot[i] = r[i].TotalMs;
                    alloc[i] = r[i].AllocBytes;
                }

                return (
                    Stats.SummarizeLong(snap),
                    Stats.SummarizeLong(met),
                    Stats.SummarizeLong(tot),
                    Stats.SummarizeLong(alloc)
                );
            }

            var sa = Summ(a);
            var sb = Summ(b);

            Console.WriteLine();
            Console.WriteLine("==== delta (B vs A) ====");
            Console.WriteLine($"A = {aName}");
            Console.WriteLine($"B = {bName}");
            Console.WriteLine();

            static string DeltaLine(string label, Stats.Summary A, Stats.Summary B)
            {
                double dMean = B.Mean - A.Mean;
                double dP95 = B.P95 - A.P95;

                double pctMean = A.Mean > 0 ? (B.Mean / A.Mean - 1) * 100 : 0;
                double pctP95 = A.P95 > 0 ? (B.P95 / A.P95 - 1) * 100 : 0;

                return $"{label,-10} mean {dMean,7:F2} ms ({pctMean,6:F1}%) | p95 {dP95,6:F0} ms ({pctP95,6:F1}%)";
            }

            Console.WriteLine(DeltaLine("snapshot", sa.snap, sb.snap));
            Console.WriteLine(DeltaLine("metrics", sa.met, sb.met));
            Console.WriteLine(DeltaLine("total", sa.tot, sb.tot));

            double allocPct = sa.alloc.Mean > 0 ? (sb.alloc.Mean / sa.alloc.Mean - 1) * 100 : 0;
            Console.WriteLine();
            Console.WriteLine($"alloc mean  {(sb.alloc.Mean - sa.alloc.Mean) / 1024.0:F1} KB ({allocPct:F1}%)");
            Console.WriteLine("========================");
        }

        static IReadOnlyList<IterationResult> RunCollector(IProcessCollector collector, int iterations, int intervalMs, bool csv)
        {
            var runner = new BenchmarkRunner();
            var results = runner.Run(collector, iterations, TimeSpan.FromMilliseconds(intervalMs));

            if (csv)
            {
                Console.WriteLine();
                Console.WriteLine($"Collector: {collector.Name}");
                Console.WriteLine("iter,procs,snapshot_ms,metrics_ms,total_ms,alloc_bytes");
                foreach (var r in results)
                    Console.WriteLine($"{r.Iteration},{r.Processes},{r.SnapshotMs},{r.MetricsMs},{r.TotalMs},{r.AllocBytes}");
            }

            return results;
        }

        static void RunCompare(int iterations, int intervalMs, CollectorOptions opts, bool csv)
        {
            var procfs = new ProcFsCollector(opts);
            var hybrid = new HybridCollector(opts);

            var resA = RunCollector(procfs, iterations, intervalMs, csv);
            var resB = RunCollector(hybrid, iterations, intervalMs, csv);

            PrintSummary(procfs.Name, resA);
            PrintSummary(hybrid.Name, resB);
            PrintDelta(procfs.Name, resA, hybrid.Name, resB);
        }

        static void Main(string[] args)
        {
            // IDE-friendly: без аргументов сразу compare
            if (args.Length == 0)
            {
                var opts = new CollectorOptions
                {
                    IncludeVms = true,
                    IncludeThreads = true,
                    IncludeReadBytes = false
                };

                RunCompare(iterations: 50, intervalMs: 100, opts: opts, csv: false);
                return;
            }

            if (args[0] == "help" || args[0] == "--help" || args[0] == "-h")
            {
                PrintUsage();
                return;
            }

            if (args[0] == "compare")
            {
                var (iterations, intervalMs, opts, csv) = ParseCommon(args, 1);
                RunCompare(iterations, intervalMs, opts, csv);
                return;
            }

            // Старый режим одиночного запуска сохраняется:
            // dotnet run -- procfs 50 1000 --vms --threads --io --csv
            {
                var mode = args[0];
                var (iterations, intervalMs, opts, csv) = ParseCommon(args, 1);

                IProcessCollector collector = mode switch
                {
                    "procfs" => new ProcFsCollector(opts),
                    "hybrid" => new HybridCollector(opts),
                    _ => throw new ArgumentException("Unknown mode: " + mode)
                };

                var results = RunCollector(collector, iterations, intervalMs, csv);
                PrintSummary(collector.Name, results);
            }
        }
    }
}

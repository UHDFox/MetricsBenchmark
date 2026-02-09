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
        static void PrintSummary(string name, IReadOnlyList<IterationResult> results, int intervalMs)
        {
            var n = results.Count;
            if (n == 0)
            {
                Console.WriteLine("Нет результатов.");
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

            // Производные метрики
            double msPerProc = avgProcs > 0 ? sTot.Mean / avgProcs : 0;
            double kbPerProc = avgProcs > 0 ? (sAlloc.Mean / 1024.0) / avgProcs : 0;

            double dutyMean = intervalMs > 0 ? (sTot.Mean / intervalMs) * 100.0 : 0;
            double dutyP95  = intervalMs > 0 ? (sTot.P95  / intervalMs) * 100.0 : 0;
            bool overrunP95 = intervalMs > 0 && sTot.P95 > intervalMs;

            double jitter = sTot.Median > 0 ? sTot.P95 / sTot.Median : 0;

            double avgTotalSec = sTot.Mean / 1000.0;
            double procsPerSec = avgTotalSec > 0 ? avgProcs / avgTotalSec : 0;

            Console.WriteLine();
            Console.WriteLine($"==== Сводка: {name} ====");
            Console.WriteLine($"Итераций: {n}, Среднее число процессов: {avgProcs:F0}, Интервал опроса: {intervalMs} мс");
            Console.WriteLine();

            // mean=среднее, med=медиана, p95=95-й перцентиль, max=максимум
            static string Line(string label, Stats.Summary s) =>
                $"{label,-24} среднее {s.Mean,7:F2} мс | медиана {s.Median,6:F0} мс | p95 {s.P95,6:F0} мс | max {s.Max,6:F0} мс";

            Console.WriteLine(Line("Снимок CPU (snapshot):", sSnap));
            Console.WriteLine(Line("Сбор метрик (metrics):", sMet));
            Console.WriteLine(Line("Итого за итерацию:", sTot));

            Console.WriteLine();
            Console.WriteLine($"На 1 процесс: {msPerProc:F4} мс/процесс | {kbPerProc:F1} КБ мусора/процесс (alloc)");
            Console.WriteLine($"Бюджет опроса: среднее {dutyMean:F1}% интервала | p95 {dutyP95:F1}% | p95 превышает интервал: {(overrunP95 ? "ДА" : "НЕТ")}");
            Console.WriteLine($"Стабильность: p95/медиана = {jitter:F2}x (чем ближе к 1.0 — тем ровнее)");
            Console.WriteLine($"Пропускная способность: ~ {procsPerSec:F0} процессов/сек (в среднем)");
            Console.WriteLine("========================");
        }



        static void PrintDelta(string aName, IReadOnlyList<IterationResult> a, string bName, IReadOnlyList<IterationResult> b)
        {
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

            static string Sign(double v) => v >= 0 ? "+" : "-";
            static double Abs(double v) => v >= 0 ? v : -v;

            var sa = Summ(a);
            var sb = Summ(b);

            Console.WriteLine();
            Console.WriteLine("==== Разница (B относительно A) ====");
            Console.WriteLine($"A = {aName}");
            Console.WriteLine($"B = {bName}");
            Console.WriteLine("Пояснение: '+X' означает, что B медленнее/тяжелее; '-X' — что B быстрее/легче.");
            Console.WriteLine();

            static string DeltaLine(string label, Stats.Summary A, Stats.Summary B)
            {
                double dMean = B.Mean - A.Mean;
                double dP95  = B.P95  - A.P95;

                double pctMean = A.Mean > 0 ? (B.Mean / A.Mean - 1) * 100 : 0;
                double pctP95  = A.P95  > 0 ? (B.P95  / A.P95  - 1) * 100 : 0;

                string meanPart = $"{Sign(dMean)}{Abs(dMean),6:F2} мс ({pctMean,6:F1}%)";
                string p95Part  = $"{Sign(dP95)}{Abs(dP95),6:F0} мс ({pctP95,6:F1}%)";

                return $"{label,-22} среднее {meanPart} | p95 {p95Part}";
            }

            Console.WriteLine(DeltaLine("Снимок CPU (snapshot):", sa.snap, sb.snap));
            Console.WriteLine(DeltaLine("Сбор метрик (metrics):", sa.met, sb.met));
            Console.WriteLine(DeltaLine("Итого за итерацию:",     sa.tot, sb.tot));

            double dAllocKb = (sb.alloc.Mean - sa.alloc.Mean) / 1024.0;
            double allocPct = sa.alloc.Mean > 0 ? (sb.alloc.Mean / sa.alloc.Mean - 1) * 100 : 0;

            Console.WriteLine();
            Console.WriteLine($"Память (alloc):          среднее {Sign(dAllocKb)}{Abs(dAllocKb),6:F1} КБ ({allocPct,6:F1}%)");
            Console.WriteLine("===============================");
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
            var seq = new ProcFsCollector(opts);
            var par = new ProcFsParallelCollector(opts);

            var resA = RunCollector(seq, iterations, intervalMs, csv);
            var resB = RunCollector(par, iterations, intervalMs, csv);

            PrintSummary(seq.Name, resA, intervalMs);
            PrintSummary(par.Name, resB, intervalMs);
            PrintDelta(seq.Name, resA, par.Name, resB);
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
                    _ => throw new ArgumentException("Unknown mode: " + mode)
                };

                var results = RunCollector(collector, iterations, intervalMs, csv);
                PrintSummary(collector.Name, results, intervalMs);
            }
        }
    }
}

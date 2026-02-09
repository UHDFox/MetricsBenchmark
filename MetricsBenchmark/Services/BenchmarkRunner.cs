using MetricsBenchmark.Models.Data;
using MetricsBenchmark.Services;
using System.Diagnostics;
using MetricsBenchmark.Models.Results;
using System.Collections.Concurrent;

namespace MetricsBench;

public sealed class BenchmarkRunner
{
    private readonly int _cpuCount;

    public BenchmarkRunner()
    {
        _cpuCount = Environment.ProcessorCount;
    }

     public IReadOnlyList<IterationResult> Run(
        IProcessCollector collector,
        int iterations,
        TimeSpan interval)
    {
        var results = new List<IterationResult>(iterations);

        IReadOnlyDictionary<int, CpuSnapshot>? prev = null;

        // warm-up 1 (без записи)
        var warm = collector.CollectCpuSnapshot();
        Thread.Sleep(interval);

        prev = warm.ToDictionary();

        for (int i = 1; i <= iterations; i++)
        {
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var sw1 = Stopwatch.StartNew();
            var curr = collector.CollectCpuSnapshot();
            sw1.Stop();

            var sw2 = Stopwatch.StartNew();
            var metrics = collector.CollectMetrics(prev, curr);
            sw2.Stop();

            var allocAfter = GC.GetAllocatedBytesForCurrentThread();

            var totalMs = sw1.ElapsedMilliseconds + sw2.ElapsedMilliseconds;

            results.Add(new IterationResult(
                Iteration: i,
                Processes: metrics.Count,
                SnapshotMs: sw1.ElapsedMilliseconds,
                MetricsMs: sw2.ElapsedMilliseconds,
                TotalMs: totalMs,
                AllocBytes: allocAfter - allocBefore
            ));

            prev = curr;

            Thread.Sleep(interval);
        }

        return results;
    }


    public IReadOnlyList<IterationResult> RunParallel(
        IProcessCollector collector,
        int iterations,
        TimeSpan interval,
        int topN = 350) // топ N процессов по CPU
    {
        var results = new List<IterationResult>(iterations);
        IReadOnlyDictionary<int, CpuSnapshot>? prev = null;

        // --- Warm-up ---
        var warm = collector.CollectCpuSnapshot();
        Thread.Sleep(interval);
        prev = warm;

        for (int i = 1; i <= iterations; i++)
        {
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            // --- 1. Снятие snapshot ---
            var sw1 = Stopwatch.StartNew();
            var currList = collector.CollectCpuSnapshot(); // внутри Parallel.ForEach
            sw1.Stop();

            var curr = currList;

            // --- 2. Определяем топ N по CPU ---
            var topPids = GetTopNProcesses(prev, curr, topN);

            // --- 3. Сбор метрик только для топ N ---
            var sw2 = Stopwatch.StartNew();
           // var metrics = collector.CollectMetrics(topPids, prev, curr); // параллельно
            var metrics = collector.CollectMetrics(prev, curr);
            sw2.Stop();

            var allocAfter = GC.GetAllocatedBytesForCurrentThread();
            var totalMs = sw1.ElapsedMilliseconds + sw2.ElapsedMilliseconds;

            results.Add(new IterationResult(
                Iteration: i,
                Processes: metrics.Count,
                SnapshotMs: sw1.ElapsedMilliseconds,
                MetricsMs: sw2.ElapsedMilliseconds,
                TotalMs: totalMs,
                AllocBytes: allocAfter - allocBefore
            ));

            prev = curr;

            Thread.Sleep(interval);
        }

        return results;
    }

    public List<int> GetTopNProcesses(
        IReadOnlyDictionary<int, CpuSnapshot> prev,
        IReadOnlyDictionary<int, CpuSnapshot> curr,
        int topN)
    {
        var cpuPercents = new ConcurrentDictionary<int, double>();

        Parallel.ForEach(curr, kvp =>
        {
            var pid = kvp.Key;
            var currSnap = kvp.Value;

            if (!prev.TryGetValue(pid, out var prevSnap)) return;

            var percent = CpuDelta.ComputeCpuPercent(prevSnap, currSnap, _cpuCount);
            cpuPercents[pid] = percent;
        });

        // Сортируем по убыванию CPU и берём топ N
        return cpuPercents.OrderByDescending(kvp => kvp.Value)
                          .Take(topN)
                          .Select(kvp => kvp.Key)
                          .ToList();
    }

    
}

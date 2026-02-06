using MetricsBenchmark.Models.Data;
using MetricsBenchmark.Services;
using System.Diagnostics;

namespace MetricsBench;

public sealed class BenchmarkRunner
{
    public sealed record IterationResult(
        int Iteration,
        int Processes,
        long SnapshotMs,
        long MetricsMs,
        long AllocBytes
    );

    public IReadOnlyList<IterationResult> Run(
        IProcessCollector collector,
        int iterations,
        TimeSpan interval)
    {
        var results = new List<IterationResult>(iterations);

        Dictionary<int, CpuSnapshot>? prev = null;

        // warm-up 1 (без записи)
        var warm = collector.CollectCpuSnapshot().ToDictionary(x => x.Pid);
        Thread.Sleep(interval);

        prev = warm;

        for (int i = 1; i <= iterations; i++)
        {
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();

            var sw1 = Stopwatch.StartNew();
            var currList = collector.CollectCpuSnapshot();
            sw1.Stop();

            var curr = currList.ToDictionary(x => x.Pid);

            var sw2 = Stopwatch.StartNew();
            var metrics = collector.CollectMetrics(prev, curr);
            sw2.Stop();

            var allocAfter = GC.GetAllocatedBytesForCurrentThread();

            results.Add(new IterationResult(
                Iteration: i,
                Processes: metrics.Count,
                SnapshotMs: sw1.ElapsedMilliseconds,
                MetricsMs: sw2.ElapsedMilliseconds,
                AllocBytes: allocAfter - allocBefore
            ));

            prev = curr;

            Thread.Sleep(interval);
        }

        return results;
    }
}

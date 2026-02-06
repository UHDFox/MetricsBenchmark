using MetricsBenchmark.Models.Results;
namespace MetricsBench;

public static class Compare
{
    public sealed record PerfSummary(
        string Name,
        int Iterations,
        double AvgProcesses,
        Stats.Summary Snapshot,
        Stats.Summary Metrics,
        Stats.Summary Total,
        Stats.Summary Alloc,
        double ThroughputProcPerSec
    );

    public static PerfSummary BuildSummary(string name, IReadOnlyList<IterationResult> results)
    {
        int n = results.Count;
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

        double avgProcs = n > 0 ? procSum / (double)n : 0;

        var sSnap = Stats.SummarizeLong(snap);
        var sMet  = Stats.SummarizeLong(met);
        var sTot  = Stats.SummarizeLong(tot);
        var sAlloc = Stats.SummarizeLong(alloc);

        double avgTotalSec = sTot.Mean / 1000.0;
        double tput = avgTotalSec > 0 ? avgProcs / avgTotalSec : 0;

        return new PerfSummary(
            Name: name,
            Iterations: n,
            AvgProcesses: avgProcs,
            Snapshot: sSnap,
            Metrics: sMet,
            Total: sTot,
            Alloc: sAlloc,
            ThroughputProcPerSec: tput
        );
    }

    public static void PrintSummary(PerfSummary s)
    {
        Console.WriteLine();
        Console.WriteLine($"==== {s.Name} summary ====");
        Console.WriteLine($"Iterations: {s.Iterations}, Avg processes: {s.AvgProcesses:F0}");
        Console.WriteLine();

        static string Line(string label, Stats.Summary st) =>
            $"{label,-10} mean {st.Mean,7:F2} ms | med {st.Median,6:F0} ms | p95 {st.P95,6:F0} ms | max {st.Max,6:F0} ms";

        Console.WriteLine(Line("snapshot", s.Snapshot));
        Console.WriteLine(Line("metrics",  s.Metrics));
        Console.WriteLine(Line("total",    s.Total));
        Console.WriteLine();
        Console.WriteLine($"alloc      mean {s.Alloc.Mean/1024.0,7:F1} KB | p95 {s.Alloc.P95/1024.0,6:F1} KB | max {s.Alloc.Max/1024.0,6:F1} KB");
        Console.WriteLine($"throughput ~ {s.ThroughputProcPerSec:F0} processes/sec");
        Console.WriteLine("========================");
    }

    public static void PrintDelta(PerfSummary a, PerfSummary b)
    {
        Console.WriteLine();
        Console.WriteLine("==== delta (B vs A) ====");
        Console.WriteLine($"A = {a.Name}");
        Console.WriteLine($"B = {b.Name}");
        Console.WriteLine();

        static string DeltaLine(string label, Stats.Summary sa, Stats.Summary sb)
        {
            double dMean = sb.Mean - sa.Mean;
            double dP95 = sb.P95 - sa.P95;

            double pctMean = sa.Mean > 0 ? (sb.Mean / sa.Mean - 1) * 100 : 0;
            double pctP95  = sa.P95  > 0 ? (sb.P95  / sa.P95  - 1) * 100 : 0;

            return $"{label,-10} mean {dMean,7:F2} ms ({pctMean,6:F1}%) | p95 {dP95,6:F0} ms ({pctP95,6:F1}%)";
        }

        Console.WriteLine(DeltaLine("snapshot", a.Snapshot, b.Snapshot));
        Console.WriteLine(DeltaLine("metrics",  a.Metrics,  b.Metrics));
        Console.WriteLine(DeltaLine("total",    a.Total,    b.Total));

        double allocPct = a.Alloc.Mean > 0 ? (b.Alloc.Mean / a.Alloc.Mean - 1) * 100 : 0;
        Console.WriteLine();
        Console.WriteLine($"alloc mean  {(b.Alloc.Mean - a.Alloc.Mean)/1024.0:F1} KB ({allocPct:F1}%)");

        double tputPct = a.ThroughputProcPerSec > 0 ? (b.ThroughputProcPerSec / a.ThroughputProcPerSec - 1) * 100 : 0;
        Console.WriteLine($"tput        {b.ThroughputProcPerSec - a.ThroughputProcPerSec:F0} proc/s ({tputPct:F1}%)");
        Console.WriteLine("========================");
    }
}

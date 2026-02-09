namespace MetricsBenchmark.Models.Results;
public sealed record IterationResult(
    int Iteration,
    int Processes,
    long SnapshotMs,
    long MetricsMs,
    long TotalMs,
    long AllocBytes
);

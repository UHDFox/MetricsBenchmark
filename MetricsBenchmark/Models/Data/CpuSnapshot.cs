namespace MetricsBenchmark.Models.Data
{
    public readonly record struct CpuSnapshot(
        int Pid,
        long CpuTimeTicks,              // utime+stime in clock ticks
        DateTimeOffset Timestamp
    );
}

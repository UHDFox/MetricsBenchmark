namespace MetricsBenchmark.Models.Data
{
    public sealed record ProcessMetrics(
        int Pid,
        string ProcessName,
        string? CmdLine,
        string User,
        DateTimeOffset StartTime,
        double CpuPercent,
        long RssBytes,
        long? VmsBytes,
        int? Threads,
        char State,
        long? ReadBytes
    );
}

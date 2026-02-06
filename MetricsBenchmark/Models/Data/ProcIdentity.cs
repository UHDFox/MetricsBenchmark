namespace MetricsBenchmark.Models.Data
{
    public readonly record struct ProcIdentity(
        int Pid,
        string ProcessName
    );
}

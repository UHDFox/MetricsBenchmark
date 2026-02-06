namespace MetricsBenchmark.Models
{
    public sealed class CollectorOptions
    {
        public bool IncludeVms { get; init; } = false;
        public bool IncludeThreads { get; init; } = false;
        public bool IncludeReadBytes { get; init; } = false;
    }

}

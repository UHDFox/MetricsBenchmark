namespace MetricsBenchmark.Models.Results;

public static class Stats
{
    public sealed record Summary(
        int N,
        double Mean,
        double Median,
        double P95,
        double Max
    );

    public static Summary SummarizeLong(long[] values)
    {
        if (values.Length == 0) return new Summary(0, 0, 0, 0, 0);

        Array.Sort(values);

        double mean = 0;
        for (int i = 0; i < values.Length; i++) mean += values[i];
        mean /= values.Length;

        double median = PercentileSorted(values, 50);
        double p95 = PercentileSorted(values, 95);
        double max = values[^1];

        return new Summary(values.Length, mean, median, p95, max);
    }

    public static double PercentileSorted(long[] sorted, int p)
    {
        // nearest-rank percentile, простая и понятная формула
        if (sorted.Length == 0) return 0;
        if (p <= 0) return sorted[0];
        if (p >= 100) return sorted[^1];

        int rank = (int)Math.Ceiling(p / 100.0 * sorted.Length);
        int idx = Math.Clamp(rank - 1, 0, sorted.Length - 1);
        return sorted[idx];
    }
}

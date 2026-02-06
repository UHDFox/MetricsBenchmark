namespace MetricsBenchmark.Models.Data
{
    public static class CpuDelta
    {
        public static double ComputeCpuPercent(
            CpuSnapshot prev,
            CpuSnapshot curr,
            int cpuCount)
        {
            var dt = (curr.Timestamp - prev.Timestamp).TotalSeconds;
            if (dt <= 0) return 0;

            var dTicks = curr.CpuTimeTicks - prev.CpuTimeTicks;
            if (dTicks <= 0) return 0;

            // ticks per second: Linux "HZ". Считаем динамически через sysconf? упрощенно: 100.
            // Для корректности лучше читать USER_HZ из sysconf, но в .NET без P/Invoke обычно берут 100.
            const double HZ = 100.0;

            var cpuSeconds = dTicks / HZ;
            var percent = (cpuSeconds / (dt * cpuCount)) * 100.0;

            // может слегка превышать из-за джиттера/тайминга
            return percent < 0 ? 0 : percent;
        }
    }
}

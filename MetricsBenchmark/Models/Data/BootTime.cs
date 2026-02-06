namespace MetricsBenchmark.Models.Data
{
    public sealed class BootTime
    {
        public DateTimeOffset BootTimeUtc { get; }

        private BootTime(DateTimeOffset bootTimeUtc) => BootTimeUtc = bootTimeUtc;

        public static BootTime Read()
        {
            // /proc/stat: строка "btime <seconds>"
            foreach (var line in File.ReadLines("/proc/stat"))
            {
                if (line.StartsWith("btime ", StringComparison.Ordinal))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2 && long.TryParse(parts[1], out var btimeSec))
                    {
                        var boot = DateTimeOffset.FromUnixTimeSeconds(btimeSec);
                        return new BootTime(boot);
                    }
                }
            }
            // fallback: сейчас - uptime (хуже). Но лучше упасть явно.
            throw new InvalidOperationException("Cannot read btime from /proc/stat");
        }
    }
}

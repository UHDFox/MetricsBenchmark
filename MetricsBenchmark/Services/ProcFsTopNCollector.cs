using MetricsBenchmark.Models;
using MetricsBenchmark.Models.Data;
using MetricsBenchmark.Services;
using MetricsBenchmark.Services.Infrastructure;
using System.Collections.Concurrent;

public sealed class ProcFsParallelCollector : IProcessCollector
{
    public string Name => "procfs-parallel";
    public CollectorOptions Options { get; }

    private readonly BootTime _bootTime;
    private readonly PasswdCache _passwd;
    private readonly int _cpuCount;
    private readonly int _pageSize;

    public ProcFsParallelCollector(CollectorOptions options)
    {
        Options = options;
        _bootTime = BootTime.Read();
        _passwd = PasswdCache.Load();
        _cpuCount = Environment.ProcessorCount;
        _pageSize = Environment.SystemPageSize;
    }

    public IReadOnlyDictionary<int, CpuSnapshot> CollectCpuSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        var result = new ConcurrentDictionary<int, CpuSnapshot>();

        var dirs = Directory.EnumerateDirectories("/proc")
                            .Where(d => int.TryParse(Path.GetFileName(d), out _));

        Parallel.ForEach(dirs, dir =>
        {
            if (!int.TryParse(Path.GetFileName(dir), out int pid)) return;

            var statPath = Path.Combine(dir, "stat");
            try
            {
                var statRaw = File.ReadAllText(statPath);
                var stat = ProcParsers.ParseStat(statRaw);
                if (stat is null)
                    return;

                result[pid] = new CpuSnapshot(pid, stat.TotalCpuTicks, now);
            }
            catch { }
        });

        return result;
    }

    public IReadOnlyList<ProcessMetrics> CollectMetrics(
        IReadOnlyDictionary<int, CpuSnapshot> prev,
        IReadOnlyDictionary<int, CpuSnapshot> curr)
    {
        var result = new ConcurrentBag<ProcessMetrics>();

        Parallel.ForEach(curr, kvp =>
        {
            var pid = kvp.Key;
            var currCpu = kvp.Value;

            if (!prev.TryGetValue(pid, out var prevCpu))
                return;

            var dir = $"/proc/{pid}";
            var statPath = Path.Combine(dir, "stat");
            try
            {
                var statRaw = File.ReadAllText(statPath);
                var stat = ProcParsers.ParseStat(statRaw);
                if (stat is null)
                    return;

                var cpuPercent = CpuDelta.ComputeCpuPercent(prevCpu, currCpu, _cpuCount);

                var cmdline = ProcParsers.ReadCmdline(Path.Combine(dir, "cmdline"));

                int uid;
                int? threads;
                long? vmRssBytes;
                long? vmSizeBytes;

                ProcParsers.ParseStatus(File.ReadLines(Path.Combine(dir, "status")),
                    out uid, out threads, out vmRssBytes, out vmSizeBytes);

                var user = uid >= 0 ? _passwd.Resolve(uid) : "unknown";

                var startTime = _bootTime.BootTimeUtc.AddSeconds(stat.StartTimeTicks / 100.0);
                var rssBytes = vmRssBytes ?? stat.ResidentSetPages * _pageSize;
                long? vmsBytes = Options.IncludeVms ? (vmSizeBytes ?? stat.VirtualMemoryBytes) : null;
                int? thr = Options.IncludeThreads ? threads : null;
                long? readBytes = Options.IncludeReadBytes
                    ? ProcParsers.ReadReadBytesFromIo(Path.Combine(dir, "io"))
                    : null;

                result.Add(new ProcessMetrics(
                    Pid: pid,
                    ProcessName: stat.ProcessName,
                    CmdLine: cmdline,
                    User: user,
                    StartTime: startTime,
                    CpuPercent: cpuPercent,
                    RssBytes: rssBytes,
                    VmsBytes: vmsBytes,
                    Threads: thr,
                    State: stat.State,
                    ReadBytes: readBytes
                ));
            }
            catch { }
        });

        return result.ToList();
    }
}

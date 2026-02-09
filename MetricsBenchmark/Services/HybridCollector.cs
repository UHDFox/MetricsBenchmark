using MetricsBenchmark.Models;
using MetricsBenchmark.Models.Data;
using MetricsBenchmark.Services;
using MetricsBenchmark.Services.Infrastructure;
using System.Diagnostics;

namespace MetricsBench.Collectors;

public sealed class HybridCollector 
{
    public string Name => "hybrid";
    public CollectorOptions Options { get; }

    private readonly BootTime _bootTime;
    private readonly PasswdCache _passwd;
    private readonly int _cpuCount;
    private readonly int _pageSize;

    public HybridCollector(CollectorOptions options)
    {
        Options = options;
        _bootTime = BootTime.Read();
        _passwd = PasswdCache.Load();
        _cpuCount = Environment.ProcessorCount;
        _pageSize = Environment.SystemPageSize;
    }

    public IReadOnlyList<CpuSnapshot> CollectCpuSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        var list = new List<CpuSnapshot>(1024);

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                // TotalProcessorTime в TimeSpan -> ticks 100ns, но нам нужны "HZ ticks" для унификации.
                // Чтобы CPU% считался одинаково, конвертируем в "HZ ticks": seconds * HZ
                const double HZ = 100.0;
                var hzTicks = (long)(p.TotalProcessorTime.TotalSeconds * HZ);
                list.Add(new CpuSnapshot(p.Id, hzTicks, now));
            }
            catch
            {
                // process may exit / access denied
            }
        }

        return list;
    }

    public IReadOnlyList<ProcessMetrics> CollectMetrics(
        IReadOnlyDictionary<int, CpuSnapshot> prevSnapshotByPid,
        IReadOnlyDictionary<int, CpuSnapshot> currSnapshotByPid)
    {
        var list = new List<ProcessMetrics>(currSnapshotByPid.Count);

        foreach (var (pid, currCpu) in currSnapshotByPid)
        {
            if (!prevSnapshotByPid.TryGetValue(pid, out var prevCpu))
                continue;

            try
            {
                using var p = Process.GetProcessById(pid);

                // processName: из Diagnostics
                var processName = SafeGet(() => p.ProcessName) ?? pid.ToString();

                // cmdline/user/rss/vms/state/io: из /proc (стабильнее на Linux)
                var dir = $"/proc/{pid}";

                var cmdline = ProcParsers.ReadCmdline(Path.Combine(dir, "cmdline"));

                int uid;
                int? threads;
                long? vmRssBytes;
                long? vmSizeBytes;
                ProcParsers.ParseStatus(File.ReadLines(Path.Combine(dir, "status")),
                    out uid, out threads, out vmRssBytes, out vmSizeBytes);

                var user = uid >= 0 ? _passwd.Resolve(uid) : "unknown";

                // startTime: можно попытаться из Process.StartTime, но на Linux это часто pain.
                // Берём из /proc/stat (унифицируем с ProcFS).
                var statLine = File.ReadAllText(Path.Combine(dir, "stat"));
                if (!ProcParsers.TryParseStat(statLine, out _, out var state,
                        out _, out _, out var startTicks, out var vsizeBytes, out var rssPages))
                    continue;

                const double HZ = 100.0;
                var start = _bootTime.BootTimeUtc.AddSeconds(startTicks / HZ);

                var cpuPercent = CpuDelta.ComputeCpuPercent(prevCpu, currCpu, _cpuCount);

                var rssBytes = vmRssBytes ?? (rssPages * (long)_pageSize);

                long? vmsBytes = null;
                if (Options.IncludeVms)
                    vmsBytes = vmSizeBytes ?? vsizeBytes;

                int? thr = Options.IncludeThreads ? threads : null;

                long? readBytes = null;
                if (Options.IncludeReadBytes)
                    readBytes = ProcParsers.ReadReadBytesFromIo(Path.Combine(dir, "io"));

                list.Add(new ProcessMetrics(
                    Pid: pid,
                    ProcessName: processName,
                    CmdLine: cmdline,
                    User: user,
                    StartTime: start,
                    CpuPercent: cpuPercent,
                    RssBytes: rssBytes,
                    VmsBytes: vmsBytes,
                    Threads: thr,
                    State: state,
                    ReadBytes: readBytes
                ));
            }
            catch
            {
                // pid disappeared / denied
            }
        }

        return list;
    }

    private static string? SafeGet(Func<string> f)
    {
        try { return f(); } catch { return null; }
    }
}

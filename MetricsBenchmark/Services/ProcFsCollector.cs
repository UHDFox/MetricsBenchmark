using MetricsBenchmark.Models;
using MetricsBenchmark.Models.Data;
using MetricsBenchmark.Services.Infrastructure;

namespace MetricsBenchmark.Services;

public sealed class ProcFsCollector : IProcessCollector
{
    public string Name => "procfs";
    public CollectorOptions Options { get; }

    private readonly BootTime _bootTime;
    private readonly PasswdCache _passwd;
    private readonly int _cpuCount;
    private readonly int _pageSize;

    public ProcFsCollector(CollectorOptions options)
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

        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out var pid))
                continue;

            var statPath = Path.Combine(dir, "stat");
            try
            {
                var stat = File.ReadAllText(statPath);
                if (!ProcParsers.TryParseStat(stat, out _, out _, out var ut, out var st, out _, out _, out _))
                    continue;

                list.Add(new CpuSnapshot(pid, ut + st, now));
            }
            catch
            {
                // PID мог исчезнуть — ок
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
                continue; // нет дельты => пропускаем CPU% (или можно 0)

            var dir = $"/proc/{pid}";
            try
            {
                var statLine = File.ReadAllText(Path.Combine(dir, "stat"));
                if (!ProcParsers.TryParseStat(statLine, out var comm, out var state,
                        out var ut, out var st, out var startTicks, out var vsizeBytes, out var rssPages))
                    continue;

                // CPU% — нормализовано
                var cpuPercent = CpuDelta.ComputeCpuPercent(prevCpu, currCpu, _cpuCount);

                // cmdline
                var cmdline = ProcParsers.ReadCmdline(Path.Combine(dir, "cmdline"));

                // user + threads + rss/vms (status)
                int uid;
                int? threads;
                long? vmRssBytes;
                long? vmSizeBytes;
                ProcParsers.ParseStatus(File.ReadLines(Path.Combine(dir, "status")),
                    out uid, out threads, out vmRssBytes, out vmSizeBytes);

                var user = uid >= 0 ? _passwd.Resolve(uid) : "unknown";

                // startTime: boot + ticks/HZ
                const double HZ = 100.0;
                var start = _bootTime.BootTimeUtc.AddSeconds(startTicks / HZ);

                // rss: предпочтительно VmRSS (bytes). иначе rssPages*pageSize
                var rssBytes = vmRssBytes ?? rssPages * _pageSize;

                long? vmsBytes = null;
                if (Options.IncludeVms)
                    vmsBytes = vmSizeBytes ?? vsizeBytes;

                int? thr = Options.IncludeThreads ? threads : null;

                long? readBytes = null;
                if (Options.IncludeReadBytes)
                    readBytes = ProcParsers.ReadReadBytesFromIo(Path.Combine(dir, "io"));

                // processName: comm из stat
                var processName = comm;

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
                // снова: PID мог исчезнуть/доступ запрещён
            }
        }

        return list;
    }
}

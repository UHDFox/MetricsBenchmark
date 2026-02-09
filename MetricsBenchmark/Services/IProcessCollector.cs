using MetricsBenchmark.Models;
using MetricsBenchmark.Models.Data;

namespace MetricsBenchmark.Services
{
    public interface IProcessCollector
    {
        string Name { get; }
        CollectorOptions Options { get; }

        // 1) быстрый снимок для CPU delta
        IReadOnlyDictionary<int, CpuSnapshot> CollectCpuSnapshot();

        // 2) полный сбор метрик, используя prev snapshot map (pid -> snapshot)
        IReadOnlyList<ProcessMetrics> CollectMetrics(
            IReadOnlyDictionary<int, CpuSnapshot> prevSnapshotByPid,
            IReadOnlyDictionary<int, CpuSnapshot> currSnapshotByPid);

        /*IReadOnlyList<ProcessMetrics> CollectMetricsForTopN(
            IReadOnlyList<int> topPids,
            IReadOnlyDictionary<int, CpuSnapshot> prevSnapshotByPid,
            IReadOnlyDictionary<int, CpuSnapshot> currSnapshotByPid);*/
    }
}

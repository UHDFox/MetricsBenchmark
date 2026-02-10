namespace MoSys.Agent.Core.Models.Process
{
    /// <summary>
    /// Индексы конкретных полей из /proc/[pid]/stat
    /// </summary>
    public enum ProcessStatIndex
    {
        Utime = 14,
        Stime = 15,
        StartTime = 22,
        Vsize = 23,
        Rss = 24
    }
}

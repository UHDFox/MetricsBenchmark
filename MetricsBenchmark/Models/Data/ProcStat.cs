public class ProcStat
{
    /// <summary>
    /// Имя процесса (comm) из /proc/[pid]/stat
    /// </summary>
    public string ProcessName { get; }

    /// <summary>
    /// Состояние процесса ('R', 'S', 'D', etc.)
    /// </summary>
    public char State { get; }

    /// <summary>
    /// CPU-время пользователя в тиках
    /// </summary>
    public long UserCpuTicks { get; }

    /// <summary>
    /// CPU-время ядра в тиках
    /// </summary>
    public long KernelCpuTicks { get; }

    /// <summary>
    /// Время старта процесса в тиках с момента загрузки системы
    /// </summary>
    public long StartTimeTicks { get; }

    /// <summary>
    /// Размер виртуальной памяти процесса (байты)
    /// </summary>
    public long VirtualMemoryBytes { get; }

    /// <summary>
    /// Resident Set Size в страницах памяти
    /// </summary>
    public long ResidentSetPages { get; }

    public ProcStat(string processName, char state, long userCpuTicks, long kernelCpuTicks,
        long startTimeTicks, long virtualMemoryBytes, long residentSetPages)
    {
        ProcessName = processName;
        State = state;
        UserCpuTicks = userCpuTicks;
        KernelCpuTicks = kernelCpuTicks;
        StartTimeTicks = startTimeTicks;
        VirtualMemoryBytes = virtualMemoryBytes;
        ResidentSetPages = residentSetPages;
    }

    /// <summary>
    /// Общие CPU-типы (user + kernel)
    /// </summary>
    public long TotalCpuTicks => UserCpuTicks + KernelCpuTicks;
}

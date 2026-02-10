namespace MoSys.Agent.Core.Models.Process
{
    /// <summary>
    /// Данные о процессе, получаемые из /proc/[pid]/stat
    /// </summary>
    public class ProcessStatData
    {
        /// <summary>
        /// comm, Имя процесса из /proc/[pid]/stat
        /// </summary>
        public string ProcessName { get; }

        /// <summary>
        /// Состояние процесса ('R', 'S', 'D', etc.)
        /// </summary>
        public char State { get; }

        /// <summary>
        /// utime, CPU-время пользователя в тиках
        /// </summary>
        public long UserCpuTicks { get; }

        /// <summary>
        /// stime, CPU-время ядра в тиках
        /// </summary>
        public long KernelCpuTicks { get; }

        /// <summary>
        /// StartTime, Время старта процесса в тиках с момента загрузки системы
        /// </summary>
        public long StartTimeTicks { get; }

        /// <summary>
        /// vSize, Размер виртуальной памяти процесса (байты)
        /// </summary>
        public long VirtualMemoryBytes { get; }

        /// <summary>
        /// Rss, Resident Set Size в страницах памяти
        /// </summary>
        public long ResidentSetPages { get; }

        public ProcessStatData(string processName, char state, long userCpuTicks, long kernelCpuTicks,
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
        /// Общие CPU-типы (utime + stime)
        /// </summary>
        public long TotalCpuTicks => UserCpuTicks + KernelCpuTicks;
    }
}

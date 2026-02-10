using MetricsBenchmark.Models.Data;

namespace MetricsBenchmark.Services.Infrastructure
{
        public static class ProcParsers
    {
        // После извлечения comm (2) и state (3) первый токен соответствует полю №4 (ppid)
        private const int FirstTokenFieldIndex = 4;
        // Смещение между tokenIndex (начинается с 1) и реальным номером поля в /proc/[pid]/stat
        private const int FieldOffset = FirstTokenFieldIndex - 1;
        private const int UnknownUid = -1;

        public static ProcStat? ParseStat(string statLine)
        {
            int openParenIndex = statLine.IndexOf('(');
            int closeParenIndex = statLine.LastIndexOf(')');

            if (openParenIndex < 0 || closeParenIndex <= openParenIndex) 
            {
                return null;
            }
            var processNameLength = closeParenIndex - openParenIndex - 1;
            string processName = statLine.Substring(openParenIndex + 1, processNameLength);
            if (closeParenIndex + 2 >= statLine.Length) 
            {
                return null;
            }

            // Строка после "comm) "
            var afterCommSpan = statLine.AsSpan(closeParenIndex + 2);
            if (afterCommSpan.Length < 1) 
            {
                return null;
            }

            char state = afterCommSpan[0];

            // Пропускаем "state " и получаем только числовые поля
            ReadOnlySpan<char> tokenSpan =
                afterCommSpan.Length > 2 ? afterCommSpan.Slice(2) : ReadOnlySpan<char>.Empty;

            long utime = 0;
            long stime = 0;
            long startTime = 0;
            long vsize = 0;
            long rss = 0;

            EnumerateTokens(tokenSpan, (token, tokenIndex) =>
            {
                // tokenIndex начинается с 1 и соответствует полю №4 (ppid)
                int fieldIndex = FirstTokenFieldIndex + FieldOffset;

                switch ((ProcessStatField)fieldIndex)
                {
                    case ProcessStatField.Utime:
                        long.TryParse(token, out utime);
                        break;

                    case ProcessStatField.Stime:
                        long.TryParse(token, out stime);
                        break;

                    case ProcessStatField.StartTime:
                        long.TryParse(token, out startTime);
                        break;

                    case ProcessStatField.Vsize:
                        long.TryParse(token, out vsize);
                        break;

                    case ProcessStatField.Rss:
                        long.TryParse(token, out rss);
                        break;
                }
            });

            return new ProcStat(processName, state, utime, stime, startTime, vsize, rss);
        }

        /// <summary>
        /// Разбивает ReadOnlySpan<char> на токены по пробелам и вызывает callback для каждого токена.
        /// </summary>
        /// <param name="input">Входной span с текстом для токенизации.</param>
        /// <param name="onToken">Callback, принимающий токен и его порядковый номер (начиная с 1).</param>
        public static void EnumerateTokens(
            ReadOnlySpan<char> input,
            Action<ReadOnlySpan<char>, int> onToken)
        {
            int tokenStart = 0;
            int currentPosition = 0;
            int tokenIndex = 0;

            while (currentPosition < input.Length)
            {
                var isSeparator = input[currentPosition].Equals(' ');
                if (isSeparator)
                {
                    int tokenLength = currentPosition - tokenStart;

                    if (tokenLength > 0)
                    {
                        tokenIndex++;
                        onToken(input.Slice(tokenStart, tokenLength), tokenIndex);
                    }

                    tokenStart = currentPosition + 1; // переходим к следующему токену
                }

                currentPosition++;
            }

            // Обрабатываем последний токен, если он не пустой
            int lastTokenLength = input.Length - tokenStart;

            if (lastTokenLength > 0)
            {
                tokenIndex++;
                onToken(input.Slice(tokenStart, lastTokenLength), tokenIndex);
            }
        }

        /// <summary>
        /// Парсит строки из /proc/[pid]/status и извлекает UID, количество потоков, RSS и виртуальную память.
        /// </summary>
        /// <param name="lines">Строки файла status.</param>
        /// <param name="uid">UID процесса.</param>
        /// <param name="threads">Количество потоков (если найдено).</param>
        /// <param name="vmRssBytes">Resident Set Size в байтах (если найдено).</param>
        /// <param name="vmSizeBytes">Виртуальная память в байтах (если найдено).</param>
        public static void ParseStatus(
            IEnumerable<string> lines,
            out int uid,
            out int? threads,
            out long? vmRssBytes,
            out long? vmSizeBytes)
        {
            uid = UnknownUid;
            threads = null;
            vmRssBytes = null;
            vmSizeBytes = null;

            foreach (var line in lines)
            {
                var parts = line.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) 
                {
                    continue;
                }

                // Определяем нужное поле по префиксу строки
                switch (parts[0])
                {
                    case "Uid:":
                        if (int.TryParse(parts[1], out var parsedUid))
                            uid = parsedUid;
                        break;

                    case "Threads:":
                        if (int.TryParse(parts[1], out var parsedThreads))
                            threads = parsedThreads;
                        break;

                    case "VmRSS:":
                        if (long.TryParse(parts[1], out var rssKb))
                            vmRssBytes = rssKb * 1024; // переводим в байты
                        break;

                    case "VmSize:":
                        if (long.TryParse(parts[1], out var vmsKb))
                            vmSizeBytes = vmsKb * 1024; // переводим в байты
                        break;
                }
            }
        }

        /// <summary>
        /// Читает командную строку процесса из /proc/[pid]/cmdline. 
        /// NUL-символы заменяются пробелами.
        /// </summary>
        /// <param name="path">Путь к cmdline файла.</param>
        /// <returns>Командная строка процесса или null.</returns>
        public static string? ReadCmdline(string path)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length == 0) return null;

                // Заменяем NUL-символы пробелами
                for (int i = 0; i < bytes.Length; i++)
                    if (bytes[i] == 0) bytes[i] = (byte)' ';

                var result = System.Text.Encoding.UTF8.GetString(bytes).Trim();
                return result;
            }
            catch
            {
                return null;
            }
        }


        public static long? ReadReadBytesFromIo(string path)
        {
            try
            {
                foreach (var line in File.ReadLines(path))
                {
                    // read_bytes: 123
                    if (line.StartsWith("read_bytes:", StringComparison.Ordinal))
                    {
                        var parts = line.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 2 && long.TryParse(parts[1], out var v)) return v;
                    }
                }
                return null;
            }
            catch { return null; }
        }
    }
}

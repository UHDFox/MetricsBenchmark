using MetricsBenchmark.Models.Data;

namespace MetricsBenchmark.Services.Infrastructure
{
    public static class ProcParsers
    {
        private const int StatBase = 4;
        // /proc/[pid]/stat tricky: comm in parentheses may contain spaces.
        // Fields (1-based): 1 pid, 2 comm, 3 state, 14 utime, 15 stime, 22 starttime, 23 vsize, 24 rss
        public static ProcStat? ParseStat(string statLine)
        {
            int open = statLine.IndexOf('(');
            int close = statLine.LastIndexOf(')');
            if (open < 0 || close <= open) return null;

            string comm = statLine.Substring(open + 1, close - open - 1);
            if (close + 2 >= statLine.Length) return null;

            var after = statLine.AsSpan(close + 2);
            if (after.Length < 1) return null;

            char state = after[0];
            ReadOnlySpan<char> tokenSpan = after.Length > 2 ? after.Slice(2) : ReadOnlySpan<char>.Empty;

            long utime = 0, stime = 0, start = 0, vsize = 0, rss = 0;

            int tokenIndex = 0;
            for (int pos = 0, startPos = 0; pos <= tokenSpan.Length; pos++)
            {
                bool atEnd = pos == tokenSpan.Length;
                bool isSpace = !atEnd && tokenSpan[pos] == ' ';
                if (atEnd || isSpace)
                {
                    int len = pos - startPos;
                    if (len > 0)
                    {
                        tokenIndex++;
                        var token = tokenSpan.Slice(startPos, len);
                        switch ((ProcessStatField)(tokenIndex + 4 - 1))
                        {
                            case ProcessStatField.Utime: TryParseInt64(token, out utime); break;
                            case ProcessStatField.Stime: TryParseInt64(token, out stime); break;
                            case ProcessStatField.StartTime: TryParseInt64(token, out start); break;
                            case ProcessStatField.Vsize: TryParseInt64(token, out vsize); break;
                            case ProcessStatField.Rss: TryParseInt64(token, out rss); break;
                        }
                    }
                    startPos = pos + 1;
                }
            }

            return new ProcStat(comm, state, utime, stime, start, vsize, rss);
        }



        public static void EnumerateTokens(
        ReadOnlySpan<char> input,
        Action<ReadOnlySpan<char>, int> onToken)
        {
            int tokenStart = 0;
            int position = 0;
            int tokenIndex = 0;

            while (position < input.Length)
            {
                char currentChar = input[position];
                bool isSeparator = currentChar == ' ';

                if (isSeparator)
                {
                    int tokenLength = position - tokenStart;

                    if (tokenLength > 0)
                    {
                        tokenIndex++;
                        onToken(input.Slice(tokenStart, tokenLength), tokenIndex);
                    }

                    tokenStart = position + 1;
                }

                position++;
            }

            int lastTokenLength = input.Length - tokenStart;
            if (lastTokenLength > 0)
            {
                tokenIndex++;
                onToken(input.Slice(tokenStart, lastTokenLength), tokenIndex);
            }
        }

        // Fast-ish parser for positive/negative integer in span
        private static bool TryParseInt64(ReadOnlySpan<char> s, out long value)
        {
            value = 0;
            if (s.Length == 0) return false;

            int i = 0;
            bool neg = false;

            if (s[0] == '-')
            {
                neg = true;
                i = 1;
                if (i >= s.Length) return false;
            }

            long v = 0;
            for (; i < s.Length; i++)
            {
                char c = s[i];
                if ((uint)(c - '0') > 9) return false;
                v = checked(v * 10 + (c - '0'));
            }

            value = neg ? -v : v;
            return true;
        }

        // /proc/[pid]/status for Uid, Threads, VmRSS, VmSize
        public static void ParseStatus(
            IEnumerable<string> lines,
            out int uid,
            out int? threads,
            out long? vmRssBytes,
            out long? vmSizeBytes)
        {
            uid = -1;
            threads = null;
            vmRssBytes = null;
            vmSizeBytes = null;

            foreach (var line in lines)
            {
                if (line.StartsWith("Uid:", StringComparison.Ordinal))
                {
                    var parts = line.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && int.TryParse(parts[1], out var u)) uid = u;
                }
                else if (line.StartsWith("Threads:", StringComparison.Ordinal))
                {
                    var parts = line.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2 && int.TryParse(parts[1], out var t)) threads = t;
                }
                else if (line.StartsWith("VmRSS:", StringComparison.Ordinal))
                {
                    var parts = line.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && long.TryParse(parts[1], out var kb)) vmRssBytes = kb * 1024;
                }
                else if (line.StartsWith("VmSize:", StringComparison.Ordinal))
                {
                    var parts = line.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && long.TryParse(parts[1], out var kb)) vmSizeBytes = kb * 1024;
                }
            }
        }

        // /proc/[pid]/cmdline is NUL-separated
        public static string? ReadCmdline(string path)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length == 0) return null;
                for (int i = 0; i < bytes.Length; i++)
                    if (bytes[i] == 0) bytes[i] = (byte)' ';
                return System.Text.Encoding.UTF8.GetString(bytes).Trim();
            }
            catch { return null; }
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

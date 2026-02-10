using MetricsBenchmark.Models.Data;

namespace MetricsBenchmark.Services.Infrastructure
{
    public static class ProcParsers
    {
        private const int StatBase = 4;
        // /proc/[pid]/stat tricky: comm in parentheses may contain spaces.
        // Fields (1-based): 1 pid, 2 comm, 3 state, 14 utime, 15 stime, 22 starttime, 23 vsize, 24 rss
        public static bool TryParseStat(
         string statLine,
         out string comm,
         out char state,
         out long utimeTicks,
         out long stimeTicks,
         out long startTimeTicks,
         out long vsizeBytes,
         out long rssPages)
        {
            comm = "";
            state = '?';
            utimeTicks = stimeTicks = startTimeTicks = 0;
            vsizeBytes = 0;
            rssPages = 0;

            int left = statLine.IndexOf('(');
            int right = statLine.LastIndexOf(')');
            if (left < 0 || right <= left)
            {
                return false;
            }

            comm = statLine.Substring(left + 1, right - left - 1);

            if (right + 2 >= statLine.Length)
            {
                return false;
            }

            var after = statLine.AsSpan(right + 2);
            if (after.Length < 1) return false;

            state = after[0];
            ReadOnlySpan<char> nums = after.Length > 2 ? after.Slice(2) : ReadOnlySpan<char>.Empty;

            int tokenIndex = 0;
            for (int pos = 0, tokenStart = 0; pos <= nums.Length; pos++)
            {
                bool atEnd = pos == nums.Length;
                bool isSpace = !atEnd && nums[pos] == ' ';

                if (atEnd || isSpace)
                {
                    int length = pos - tokenStart;
                    if (length > 0)
                    {
                        tokenIndex++;
                        var token = nums.Slice(tokenStart, length);

                        switch ((ProcessStatField)tokenIndex + StatBase - 1)
                        {
                            case ProcessStatField.Utime:
                                TryParseInt64(token, out utimeTicks);
                                break;

                            case ProcessStatField.Stime:
                                TryParseInt64(token, out stimeTicks);
                                break;

                            case ProcessStatField.StartTime:
                                TryParseInt64(token, out startTimeTicks);
                                break;

                            case ProcessStatField.Vsize:
                                TryParseInt64(token, out vsizeBytes);
                                break;

                            case ProcessStatField.Rss:
                                TryParseInt64(token, out rssPages);
                                return utimeTicks >= 0 && stimeTicks >= 0;
                        }
                    }
                    tokenStart = pos + 1;
                }
            }

            return utimeTicks >= 0 && stimeTicks >= 0;
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

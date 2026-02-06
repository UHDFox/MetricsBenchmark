namespace MetricsBenchmark.Services.Infrastructure
{
    public static class ProcParsers
    {
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

            // locate "(comm)"
            int l = statLine.IndexOf('(');
            int r = statLine.LastIndexOf(')');
            if (l < 0 || r < 0 || r <= l) return false;

            comm = statLine.Substring(l + 1, r - l - 1);

            // after ") " starts: state and then numeric tokens separated by spaces
            // Expected: ") <state> <field4> <field5> ..."
            if (r + 2 >= statLine.Length) return false;

            var after = statLine.AsSpan(r + 2);
            if (after.Length < 1) return false;

            state = after[0];

            // nums begins after "state " (state + space)
            ReadOnlySpan<char> nums = after.Length > 2 ? after.Slice(2) : ReadOnlySpan<char>.Empty;

            // We need tokens relative to stat field 4:
            // token #1 => field4
            // utime (field14) => token 11
            // stime (field15) => token 12
            // starttime (field22) => token 19
            // vsize (field23) => token 20
            // rss (field24) => token 21
            const int utimeTok = 11, stimeTok = 12, startTok = 19, vsizeTok = 20, rssTok = 21;

            int tokenIdx = 0;
            int i = 0;

            while (i < nums.Length)
            {
                // skip spaces
                while (i < nums.Length && nums[i] == ' ') i++;
                if (i >= nums.Length) break;

                int start = i;
                while (i < nums.Length && nums[i] != ' ') i++;
                var slice = nums.Slice(start, i - start);

                tokenIdx++;

                // parse only what we need
                if (tokenIdx == utimeTok)
                {
                    if (!TryParseInt64(slice, out utimeTicks)) utimeTicks = 0;
                }
                else if (tokenIdx == stimeTok)
                {
                    if (!TryParseInt64(slice, out stimeTicks)) stimeTicks = 0;
                }
                else if (tokenIdx == startTok)
                {
                    if (!TryParseInt64(slice, out startTimeTicks)) startTimeTicks = 0;
                }
                else if (tokenIdx == vsizeTok)
                {
                    if (!TryParseInt64(slice, out vsizeBytes)) vsizeBytes = 0;
                }
                else if (tokenIdx == rssTok)
                {
                    if (!TryParseInt64(slice, out rssPages)) rssPages = 0;
                    break; // дальше не нужно
                }
            }

            return utimeTicks >= 0 && stimeTicks >= 0;
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

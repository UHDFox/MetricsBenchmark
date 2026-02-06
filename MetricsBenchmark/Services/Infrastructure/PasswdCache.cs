namespace MetricsBenchmark.Services.Infrastructure
{
    public sealed class PasswdCache
    {
        private readonly Dictionary<int, string> _uidToUser;

        private PasswdCache(Dictionary<int, string> uidToUser) => _uidToUser = uidToUser;

        public static PasswdCache Load()
        {
            var map = new Dictionary<int, string>(256);
            foreach (var line in File.ReadLines("/etc/passwd"))
            {
                // name:x:uid:gid:...
                if (string.IsNullOrEmpty(line) || line[0] == '#') continue;
                var parts = line.Split(':');
                if (parts.Length >= 3 && int.TryParse(parts[2], out var uid))
                    map[uid] = parts[0];
            }
            return new PasswdCache(map);
        }

        public string Resolve(int uid) => _uidToUser.TryGetValue(uid, out var u) ? u : uid.ToString();
    }

}

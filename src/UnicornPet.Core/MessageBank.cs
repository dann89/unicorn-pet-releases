using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UnicornPet.Core
{
    public record ScheduledSlot(string Id, TimeSpan Time, IReadOnlyList<string> Pool);

    public class MessageBank
    {
        public string Name { get; }
        public IReadOnlyList<ScheduledSlot> Scheduled { get; }
        public IReadOnlyList<string> Random { get; }
        public IReadOnlyList<string> Cheeky { get; }
        public IReadOnlyList<string> Welcome { get; }
        public IReadOnlyList<string> Hello { get; }

        public MessageBank(string name, IReadOnlyList<ScheduledSlot> scheduled, IReadOnlyList<string> random, IReadOnlyList<string> cheeky,
            IReadOnlyList<string> welcome = null, IReadOnlyList<string> hello = null)
        {
            Name = name ?? "";
            Scheduled = scheduled;
            Random = random;
            Cheeky = cheeky;
            Welcome = welcome ?? new List<string>();
            Hello = hello ?? new List<string>();
        }

        public MessageBank WithName(string name) => new MessageBank(name, Scheduled, Random, Cheeky, Welcome, Hello);

        static readonly Lazy<MessageBank> _default = new Lazy<MessageBank>(LoadEmbeddedDefault);
        public static MessageBank Default => _default.Value;

        static MessageBank LoadEmbeddedDefault()
        {
            using (var s = typeof(MessageBank).Assembly.GetManifestResourceStream("messages.default.json"))
            using (var r = new StreamReader(s))
                return Parse(r.ReadToEnd(), null) ?? throw new InvalidOperationException("Bad embedded default messages");
        }

        /// <summary>Loads one file; anything missing, unreadable or invalid gives the built-in defaults.</summary>
        public static MessageBank Load(string path)
        {
            return TryReadFile(path) ?? Default;
        }

        /// <summary>Returns the first file in the list that exists and is valid, otherwise the built-in defaults.</summary>
        public static MessageBank LoadFirstValid(params string[] paths)
        {
            foreach (var p in paths ?? new string[0])
            {
                var b = TryReadFile(p);
                if (b != null) return b;
            }
            return Default;
        }

        /// <summary>
        /// Work-safe loading order: a hand-edited list beside the program wins (and may set the name), then the verified downloaded list
        /// (whose own name is always dropped), then the built-in defaults. A source chosen in Settings is laid over this result by MessageComposer.
        /// </summary>
        public static MessageBank LoadWithRemote(string localPath, string remotePath)
        {
            var local = TryReadFile(localPath);
            if (local != null) return local;
            var remote = TryReadFile(remotePath);
            if (remote != null) return remote.WithName("");
            return Default;
        }

        /// <summary>Returns the parsed file, or null when it is missing, unreadable or not a usable list (no fallback to the defaults).</summary>
        public static MessageBank TryReadFile(string path)
        {
            try
            {
                if (path == null || !File.Exists(path)) return null;
                return Parse(File.ReadAllText(path), Default);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Parses a message list. Missing pools take the fallback's when one is given. Returns null if the text is not usable.</summary>
        public static MessageBank Parse(string json, MessageBank fallback)
        {
            try
            {
                var root = MiniJson.Parse(json) as Dictionary<string, object>;
                if (root == null) return null;
                string name = MiniJson.GetString(root, "name") ?? "";

                var slots = new List<ScheduledSlot>();
                object schObj;
                if (root.TryGetValue("scheduled", out schObj) && schObj is List<object>)
                {
                    foreach (var item in (List<object>)schObj)
                    {
                        var e = item as Dictionary<string, object>;
                        if (e == null) continue;
                        string id = MiniJson.GetString(e, "id");
                        var timeText = MiniJson.GetString(e, "time");
                        TimeSpan time;
                        if (timeText == null || !TimeSpan.TryParse(timeText, out time) || time < TimeSpan.Zero || time >= TimeSpan.FromDays(1)) continue;
                        var pool = MiniJson.GetStrings(e, "pool");
                        if (pool.Count == 0 && fallback != null)
                        {
                            var match = fallback.Scheduled.FirstOrDefault(f => f.Id == id) ?? fallback.Scheduled.OrderBy(f => Math.Abs((f.Time - time).TotalMinutes)).FirstOrDefault();
                            if (match != null) pool = match.Pool.ToList();
                        }
                        if (pool.Count == 0) continue;
                        slots.Add(new ScheduledSlot(id ?? time.ToString("hhmm"), time, pool));
                    }
                }
                if (slots.Count == 0 && fallback != null) slots = fallback.Scheduled.ToList();
                slots = slots.OrderBy(s => s.Time).ToList();

                var random = MiniJson.GetStrings(root, "random");
                if (random.Count == 0 && fallback != null) random = fallback.Random.ToList();
                var cheeky = MiniJson.GetStrings(root, "cheeky");
                if (cheeky.Count == 0 && fallback != null) cheeky = fallback.Cheeky.ToList();
                var welcome = MiniJson.GetStrings(root, "welcome");
                if (welcome.Count == 0 && fallback != null) welcome = fallback.Welcome.ToList();
                var hello = MiniJson.GetStrings(root, "hello");
                if (hello.Count == 0 && fallback != null) hello = fallback.Hello.ToList();
                if (slots.Count == 0 || random.Count == 0) return null;
                return new MessageBank(name, slots, random, cheeky, welcome, hello);
            }
            catch
            {
                return null;
            }
        }
    }
}

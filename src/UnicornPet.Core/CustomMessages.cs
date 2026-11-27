using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UnicornPet.Core
{
    /// <summary>A named set of lines with its own tick box in Settings. Ticked lines join the random pool.</summary>
    public sealed class MessageSection
    {
        public string Id { get; }
        public string Title { get; }
        public IReadOnlyList<string> Lines { get; }

        public MessageSection(string id, string title, IReadOnlyList<string> lines)
        {
            Id = id ?? "";
            Title = title ?? "";
            Lines = lines ?? new string[0];
        }
    }

    /// <summary>
    /// A validated custom message list (the person's own file or web address, or the built-in sections). Pools that were not in the
    /// file are null, so they come from the base list.
    /// </summary>
    public sealed class CustomMessages
    {
        /// <summary>"" unless the source may set it and it passes the name rules.</summary>
        public string Name { get; }
        /// <summary>null = not in the file.</summary>
        public IReadOnlyList<ScheduledSlot> Scheduled { get; }
        /// <summary>null = not in the file (the same for Cheeky, Welcome and Hello).</summary>
        public IReadOnlyList<string> Random { get; }
        public IReadOnlyList<string> Cheeky { get; }
        public IReadOnlyList<string> Welcome { get; }
        public IReadOnlyList<string> Hello { get; }
        /// <summary>Never null.</summary>
        public IReadOnlyList<MessageSection> Sections { get; }
        /// <summary>Every line in every pool, slot and section.</summary>
        public int LineCount { get; }

        internal CustomMessages(string name, IReadOnlyList<ScheduledSlot> scheduled, IReadOnlyList<string> random, IReadOnlyList<string> cheeky,
            IReadOnlyList<string> welcome, IReadOnlyList<string> hello, IReadOnlyList<MessageSection> sections)
        {
            Name = name ?? "";
            Scheduled = scheduled; Random = random; Cheeky = cheeky; Welcome = welcome; Hello = hello;
            Sections = sections ?? new MessageSection[0];
            int n = 0;
            if (scheduled != null) foreach (var s in scheduled) n += s.Pool.Count;
            foreach (var pool in new[] { random, cheeky, welcome, hello }) if (pool != null) n += pool.Count;
            foreach (var s in Sections) n += s.Lines.Count;
            LineCount = n;
        }
    }

    /// <summary>
    /// Checks a custom message list. Same line rules and limits as the signed list, but pools may be left out and named sections are
    /// allowed. Unknown keys are refused, so a typo is reported rather than ignored. The signed list's validator is NOT relaxed: signed
    /// lists never carry sections, because 2.0.0 copies refuse unknown keys.
    /// </summary>
    public static class CustomMessageValidator
    {
        public const int MaxSections = 8, MaxSectionLines = 100, MaxTitleLength = 40;

        static readonly HashSet<string> Keys = new HashSet<string> { "name", "scheduled", "random", "cheeky", "welcome", "hello", "sections" };
        static readonly HashSet<string> SectionKeys = new HashSet<string> { "id", "title", "lines" };

        /// <summary>null when usable, otherwise a short fixed reason that never quotes the file.</summary>
        public static string Validate(string json, bool allowName, out CustomMessages result)
        {
            result = null;
            Dictionary<string, object> root;
            try { root = MiniJson.Parse(json) as Dictionary<string, object>; }
            catch (Exception) { return "not valid JSON"; }
            if (root == null) return "not a JSON object";

            foreach (var key in root.Keys) if (!Keys.Contains(key)) return "unknown key";
            object nameObj;
            if (root.TryGetValue("name", out nameObj) && !(nameObj is string)) return "name must be text";

            bool any = false;
            List<ScheduledSlot> slots = null;
            object slotsObj;
            if (root.TryGetValue("scheduled", out slotsObj))
            {
                var list = slotsObj as List<object>;
                if (list == null || list.Count == 0) return "scheduled must be a non-empty list";
                if (list.Count > MessageValidator.MaxSlots) return "too many scheduled slots";
                var err = MessageValidator.CheckSlots(list, false);
                if (err != null) return err;
                slots = ReadSlots(list);
                any = true;
            }

            string e;
            if ((e = Pool(root, "random", MessageValidator.MaxRandom, ref any)) != null) return "random: " + e;
            if ((e = Pool(root, "cheeky", MessageValidator.MaxCheeky, ref any)) != null) return "cheeky: " + e;
            if ((e = Pool(root, "welcome", MessageValidator.MaxWelcome, ref any)) != null) return "welcome: " + e;
            if ((e = Pool(root, "hello", MessageValidator.MaxHello, ref any)) != null) return "hello: " + e;

            var sections = new List<MessageSection>();
            object secObj;
            if (root.TryGetValue("sections", out secObj))
            {
                var list = secObj as List<object>;
                if (list == null) return "sections must be a list";
                if (list.Count > MaxSections) return "too many sections";
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in list)
                {
                    var sec = item as Dictionary<string, object>;
                    if (sec == null || sec.Count != SectionKeys.Count || sec.Keys.Any(k => !SectionKeys.Contains(k))) return "a section is not usable";
                    var id = MiniJson.GetString(sec, "id");
                    if (id == null || !MessageValidator.IdRx.IsMatch(id)) return "a section id is not usable";
                    if (!ids.Add(id)) return "two sections have the same id";
                    var title = MiniJson.GetString(sec, "title");
                    if (title == null || title.Length < 1 || title.Length > MaxTitleLength || MessageValidator.CheckLine(title) != null
                        || title.IndexOf('{') >= 0 || title.IndexOf('}') >= 0) return "a section title is not usable";
                    var lerr = MessageValidator.CheckPool(sec, "lines", MaxSectionLines);
                    if (lerr != null) return "a section: " + lerr;
                    sections.Add(new MessageSection(id, title, MiniJson.GetStrings(sec, "lines")));
                }
                if (sections.Count > 0) any = true;
            }
            if (!any) return "nothing to use";

            string name = "";
            string typed;
            if (allowName && nameObj is string && NameRules.TryNormalize((string)nameObj, out typed)) name = typed;

            result = new CustomMessages(name, slots,
                Strings(root, "random"), Strings(root, "cheeky"), Strings(root, "welcome"), Strings(root, "hello"), sections);
            return null;
        }

        static string Pool(Dictionary<string, object> root, string key, int max, ref bool any)
        {
            if (!root.ContainsKey(key)) return null;
            any = true;
            return MessageValidator.CheckPool(root, key, max);
        }

        static IReadOnlyList<string> Strings(Dictionary<string, object> root, string key)
        {
            return root.ContainsKey(key) ? MiniJson.GetStrings(root, key) : null;
        }

        // the slots were checked by MessageValidator.CheckSlots: an object each, "HH:mm" times, a usable pool
        static List<ScheduledSlot> ReadSlots(List<object> list)
        {
            var slots = new List<ScheduledSlot>();
            foreach (var item in list)
            {
                var e = (Dictionary<string, object>)item;
                var timeText = MiniJson.GetString(e, "time");
                var time = new TimeSpan(int.Parse(timeText.Substring(0, 2), System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(timeText.Substring(3, 2), System.Globalization.CultureInfo.InvariantCulture), 0);
                var id = MiniJson.GetString(e, "id");
                slots.Add(new ScheduledSlot(id ?? time.ToString("hhmm"), time, MiniJson.GetStrings(e, "pool")));
            }
            return slots.OrderBy(s => s.Time).ToList();
        }
    }

    /// <summary>The five sections that ship inside the program (Office banter, Pep talks, Unicorn facts, Random facts, Office roasts).</summary>
    public static class BuiltInSections
    {
        static readonly Lazy<IReadOnlyList<MessageSection>> _all = new Lazy<IReadOnlyList<MessageSection>>(Load);

        /// <summary>From the embedded sections.default.json, validated once; empty if it is ever unusable (the pet must still start).</summary>
        public static IReadOnlyList<MessageSection> All => _all.Value;

        static IReadOnlyList<MessageSection> Load()
        {
            try
            {
                using (var s = typeof(BuiltInSections).Assembly.GetManifestResourceStream("sections.default.json"))
                {
                    if (s == null) return new MessageSection[0];
                    using (var r = new StreamReader(s))
                    {
                        CustomMessages m;
                        return CustomMessageValidator.Validate(r.ReadToEnd(), false, out m) == null ? m.Sections : new MessageSection[0];
                    }
                }
            }
            catch
            {
                return new MessageSection[0];
            }
        }
    }

    /// <summary>Lays a custom list and the sections over the base list (messages.json beside the exe, then the signed list, then the embedded lines).</summary>
    public static class MessageComposer
    {
        /// <summary>The built-in sections in their order; a custom section with the same id replaces one in place; new ids are appended in file order.</summary>
        public static IReadOnlyList<MessageSection> MergeSections(IReadOnlyList<MessageSection> builtIn, CustomMessages custom)
        {
            var list = builtIn == null ? new List<MessageSection>() : builtIn.ToList();
            if (custom == null) return list;
            foreach (var s in custom.Sections)
            {
                int at = list.FindIndex(x => string.Equals(x.Id, s.Id, StringComparison.Ordinal));
                if (at >= 0) list[at] = s; else list.Add(s);
            }
            return list;
        }

        /// <summary>
        /// The file's pools replace the base's, pool by pool; the lines of every merged section whose id is not unticked are appended to
        /// the random pool. When nothing changes the base itself is returned. The result is a plain MessageBank.
        /// </summary>
        public static MessageBank Compose(MessageBank baseBank, IReadOnlyList<MessageSection> builtIn, CustomMessages custom, IEnumerable<string> offSectionIds)
        {
            if (baseBank == null) baseBank = MessageBank.Default;
            var off = new HashSet<string>(offSectionIds ?? new string[0], StringComparer.Ordinal);
            var extra = new List<string>();
            foreach (var s in MergeSections(builtIn, custom))
                if (!off.Contains(s.Id)) extra.AddRange(s.Lines);
            if (custom == null && extra.Count == 0) return baseBank;

            var name = custom != null && custom.Name.Length > 0 ? custom.Name : baseBank.Name;
            var random = (custom?.Random ?? baseBank.Random).Concat(extra).ToList();
            return new MessageBank(name, custom?.Scheduled ?? baseBank.Scheduled, random, custom?.Cheeky ?? baseBank.Cheeky,
                custom?.Welcome ?? baseBank.Welcome, custom?.Hello ?? baseBank.Hello);
        }
    }
}

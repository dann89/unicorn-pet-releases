using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace UnicornPet.Core
{
    /// <summary>
    /// Strict check of a downloaded message list. Only plain text lines are accepted: bounded counts and lengths, no control or
    /// invisible characters, and "{name}" as the only brace token. Returns null when the list is acceptable, otherwise a short reason.
    /// </summary>
    public static class MessageValidator
    {
        public const int MaxLineLength = 140;
        public const int NameWorstCase = 30;
        public const int MaxSlots = 12, MaxSlotPool = 60, MaxRandom = 300, MaxCheeky = 200, MaxWelcome = 20, MaxHello = 40;

        static readonly HashSet<string> TopKeys = new HashSet<string> { "name", "scheduled", "random", "cheeky", "welcome", "hello" };
        static readonly HashSet<string> SlotKeys = new HashSet<string> { "id", "time", "pool" };
        static readonly Regex TimeRx = new Regex(@"^([01][0-9]|2[0-3]):[0-5][0-9]\z", RegexOptions.CultureInvariant);
        internal static readonly Regex IdRx = new Regex(@"^[A-Za-z0-9_-]{1,40}\z", RegexOptions.CultureInvariant);

        public static string Validate(string json, out MessageBank bank)
        {
            bank = null;
            Dictionary<string, object> root;
            try { root = MiniJson.Parse(json) as Dictionary<string, object>; }
            catch (FormatException) { return "not valid JSON"; }
            if (root == null) return "not a JSON object";

            foreach (var key in root.Keys) if (!TopKeys.Contains(key)) return "unknown key '" + Short(key) + "'";
            object nameObj;
            if (root.TryGetValue("name", out nameObj) && !(nameObj is string)) return "name must be text";

            object slotsObj;
            var slots = root.TryGetValue("scheduled", out slotsObj) ? slotsObj as List<object> : null;
            if (slots == null || slots.Count == 0) return "scheduled must be a non-empty list";
            if (slots.Count > MaxSlots) return "too many scheduled slots";
            var slotError = CheckSlots(slots);
            if (slotError != null) return slotError;

            string e;
            if ((e = CheckPool(root, "random", MaxRandom)) != null) return "random: " + e;
            if ((e = CheckPool(root, "cheeky", MaxCheeky)) != null) return "cheeky: " + e;
            if ((e = CheckPool(root, "welcome", MaxWelcome)) != null) return "welcome: " + e;
            if ((e = CheckPool(root, "hello", MaxHello)) != null) return "hello: " + e;

            var parsed = MessageBank.Parse(json, null);
            if (parsed == null) return "list could not be used";
            bank = parsed.WithName(""); // a downloaded list never sets the person's name
            return null;
        }

        /// <summary>Checks each scheduled slot. With quoteKeys false an unknown slot key is reported without its name (custom files).</summary>
        internal static string CheckSlots(List<object> slots, bool quoteKeys = true)
        {
            foreach (var item in slots)
            {
                var slot = item as Dictionary<string, object>;
                if (slot == null) return "a scheduled slot is not an object";
                foreach (var k in slot.Keys) if (!SlotKeys.Contains(k)) return quoteKeys ? "unknown slot key '" + Short(k) + "'" : "unknown slot key";
                object idObj;
                if (slot.TryGetValue("id", out idObj) && !(idObj is string && IdRx.IsMatch((string)idObj))) return "bad slot id";
                var time = MiniJson.GetString(slot, "time");
                if (time == null || !TimeRx.IsMatch(time)) return "bad slot time";
                var err = CheckPool(slot, "pool", MaxSlotPool);
                if (err != null) return "slot pool: " + err;
            }
            return null;
        }

        internal static string CheckPool(Dictionary<string, object> parent, string key, int max)
        {
            object v;
            var list = parent.TryGetValue(key, out v) ? v as List<object> : null;
            if (list == null || list.Count == 0) return "missing or empty";
            if (list.Count > max) return "too many lines";
            foreach (var x in list)
            {
                var s = x as string;
                if (s == null) return "a line is not text";
                var err = CheckLine(s);
                if (err != null) return err;
            }
            return null;
        }

        /// <summary>
        /// Checks one line of text. Decisions are made per code point with explicit block lists, not with the runtime's Unicode tables,
        /// because .NET Framework 4.8 has old tables (an emoji from 2016 would count as "unassigned" there).
        /// </summary>
        public static string CheckLine(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "blank line";
            if (s.Length > MaxLineLength) return "line longer than " + MaxLineLength;
            // each {name} may expand to a full-length name (30), so it counts as 30 characters, not 6
            int tokens = 0;
            for (int at = s.IndexOf("{name}", StringComparison.Ordinal); at >= 0; at = s.IndexOf("{name}", at + 6, StringComparison.Ordinal)) tokens++;
            if (s.Length + tokens * (NameWorstCase - 6) > MaxLineLength) return "line longer than " + MaxLineLength + " once names are filled in";

            var cps = new List<int>(s.Length);
            var idx = new List<int>(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1])) return "broken character";
                    cps.Add(char.ConvertToUtf32(c, s[i + 1])); idx.Add(i); i++;
                }
                else if (char.IsLowSurrogate(c)) return "broken character";
                else { cps.Add(c); idx.Add(i); }
            }

            int marks = 0;
            for (int k = 0; k < cps.Count; k++)
            {
                int cp = cps[k];
                if (IsBlocked(cp)) return "disallowed character";

                if (cp == 0x200D)
                {
                    if (k == 0 || k == cps.Count - 1 || !EmojiLike(cps[k - 1], s, idx[k - 1]) || !EmojiLike(cps[k + 1], s, idx[k + 1])) return "stray joiner";
                    marks = 0; continue;
                }
                if (cp == 0xFE0F)
                {
                    if (k == 0 || cps[k - 1] == 0xFE0F || cps[k - 1] == 0x200D) return "stray variation selector";
                    if (IsKeycapBase(cps[k - 1])) { if (k + 1 >= cps.Count || cps[k + 1] != 0x20E3) return "stray variation selector"; continue; }
                    if (!EmojiLike(cps[k - 1], s, idx[k - 1])) return "stray variation selector";
                    continue;
                }
                if (cp >= 0x1F000 && cp <= 0x1FAFF) { marks = 0; continue; } // emoji and symbol blocks: allowed whatever this runtime's tables say

                var cat = cp < 0x10000 ? char.GetUnicodeCategory((char)cp) : CharUnicodeInfo.GetUnicodeCategory(s, idx[k]);
                switch (cat)
                {
                    case UnicodeCategory.Control:
                    case UnicodeCategory.Format:
                    case UnicodeCategory.LineSeparator:
                    case UnicodeCategory.ParagraphSeparator:
                    case UnicodeCategory.PrivateUse:
                    case UnicodeCategory.Surrogate:
                        return "disallowed character";
                    case UnicodeCategory.OtherNotAssigned:
                        return "disallowed character"; // outside the emoji blocks (handled above) an unassigned code point is refused on every runtime
                }
                if (cat == UnicodeCategory.NonSpacingMark || cat == UnicodeCategory.SpacingCombiningMark || cat == UnicodeCategory.EnclosingMark)
                {
                    if (++marks > 3) return "too many combining marks";
                }
                else marks = 0;
            }

            var withoutName = s.Replace("{name}", "");
            if (withoutName.IndexOf('{') >= 0 || withoutName.IndexOf('}') >= 0) return "only {name} may use braces";
            return null;
        }

        static bool IsBlocked(int cp)
        {
            return cp < 0x20 || (cp >= 0x7F && cp <= 0x9F)                  // C0 and C1 controls
                || cp == 0x00AD || cp == 0x061C || cp == 0x034F               // soft hyphen, Arabic letter mark, combining grapheme joiner
                || cp == 0x115F || cp == 0x1160 || cp == 0x3164 || cp == 0xFFA0 // Hangul fillers (render as blank)
                || cp == 0x17B4 || cp == 0x17B5                               // Khmer inherent vowels (invisible)
                || (cp >= 0x180B && cp <= 0x180F)                             // Mongolian free variation selectors and vowel separator
                || (cp >= 0xFDD0 && cp <= 0xFDEF)                             // noncharacters
                || (cp >= 0xFFF0 && cp <= 0xFFF8)                             // reserved, default-ignorable
                || (cp >= 0x1BCA0 && cp <= 0x1BCA3) || (cp >= 0x1D173 && cp <= 0x1D17A) // shorthand and musical format controls
                || (cp >= 0x13430 && cp <= 0x1343F)                           // Egyptian hieroglyph format controls
                || (cp >= 0x10000 && (cp & 0xFFFE) == 0xFFFE)                 // the last two code points of every plane are noncharacters
                || cp == 0x200B || cp == 0x200C || (cp >= 0x200E && cp <= 0x200F) // zero-width space, non-joiner, direction marks (ZWJ 200D is handled separately)
                || (cp >= 0x2028 && cp <= 0x202E)                             // line and paragraph separators, bidi embeddings and overrides
                || (cp >= 0x2060 && cp <= 0x206F)                             // word joiner, invisible operators, bidi isolates, deprecated format characters
                || cp == 0xFEFF                                               // byte order mark
                || (cp >= 0xFE00 && cp <= 0xFE0E)                             // variation selectors other than the emoji one
                || (cp >= 0xFFF9 && cp <= 0xFFFC) || cp == 0xFFFE || cp == 0xFFFF
                || (cp >= 0xE000 && cp <= 0xF8FF)                             // private use
                || (cp >= 0xE0000 && cp <= 0xE0FFF)                           // tag characters and variation selector supplement
                || (cp >= 0xF0000 && cp <= 0x10FFFF);                         // supplementary private use planes
        }

        static bool IsKeycapBase(int cp) { return (cp >= '0' && cp <= '9') || cp == '#' || cp == '*'; }

        static bool EmojiLike(int cp, string s, int index)
        {
            if (cp >= 0x1F000 && cp <= 0x1FAFF) return true;
            if (cp == 0xFE0F) return true;
            return cp < 0x10000 && char.GetUnicodeCategory((char)cp) == UnicodeCategory.OtherSymbol;
        }
        static string Short(string s) => s.Length > 20 ? s.Substring(0, 20) : s;
    }
}

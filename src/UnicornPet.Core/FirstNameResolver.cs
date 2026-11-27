using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace UnicornPet.Core
{
    public interface IUserNameSource
    {
        string DisplayName { get; }
        string AccountName { get; }
    }

    /// <summary>Best-effort first name from the Windows display name or account name. Returns "" when unsure.</summary>
    public static class FirstNameResolver
    {
        static readonly HashSet<string> Titles = new HashSet<string> { "dr", "mr", "mrs", "ms", "miss", "prof" };

        static readonly HashSet<string> Generic = new HashSet<string>
        {
            "admin", "administrator", "user", "owner", "guest", "default", "test", "temp", "support",
            "it", "service", "pc", "laptop", "desktop", "root", "student"
        };

        /// <summary>
        /// Looks the name up only when the person allows it. With "Use my Windows first name" off the source is never touched,
        /// so no display-name lookup (which on a domain PC can make Windows contact the domain controller) is made at all.
        /// </summary>
        public static string ResolveIfAllowed(Settings settings, IUserNameSource src)
        {
            if (settings != null && !settings.UseWindowsName) return "";
            return Resolve(src);
        }

        public static string Resolve(IUserNameSource src)
        {
            try
            {
                if (src == null) return "";
                var c = FromDisplay(src.DisplayName);
                if (c.Length == 0) c = FromAccount(src.AccountName);
                return c;
            }
            catch { return ""; }
        }

        static string FromDisplay(string display)
        {
            if (string.IsNullOrWhiteSpace(display)) return "";
            var d = display.Trim();
            if (d.Any(char.IsDigit)) return "";
            int comma = d.IndexOf(',');
            if (comma >= 0) d = d.Substring(comma + 1);
            var tokens = d.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            while (tokens.Count > 0 && Titles.Contains(tokens[0].Trim('.', ',').ToLowerInvariant())) tokens.RemoveAt(0);
            if (tokens.Count == 0) return "";
            return Finish(tokens[0].Trim('.', ','));
        }

        static string FromAccount(string account)
        {
            if (string.IsNullOrWhiteSpace(account)) return "";
            var a = account.Trim();
            int slash = a.LastIndexOf('\\');
            if (slash >= 0) a = a.Substring(slash + 1);
            int at = a.IndexOf('@');
            if (at >= 0) a = a.Substring(0, at);
            var tokens = a.Split(new[] { '.', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return "";
            var first = tokens[0];
            bool hadSeparator = tokens.Length > 1;
            var stripped = Regex.Replace(first, @"\d+$", "");
            bool hadDigits = stripped.Length != first.Length;
            if (stripped.Length == 0) return "";
            // a single run of letters this long is most likely initial + surname (initial plus surname run together); better to say nothing than guess
            if (!hadSeparator && !hadDigits && stripped.Length > 7) return "";
            return Finish(stripped);
        }

        static string Finish(string candidate)
        {
            if (candidate.Length < 2) return "";
            if (candidate.Any(char.IsDigit)) return "";
            if (Generic.Contains(candidate.ToLowerInvariant())) return "";
            var letters = candidate.Where(char.IsLetter).ToList();
            if (letters.Count < 2) return "";
            if (letters.All(char.IsLower) || letters.All(char.IsUpper)) candidate = TitleCase(candidate);
            return NameRules.TryNormalize(candidate, out var n) ? n : "";
        }

        static string TitleCase(string s)
        {
            var chars = s.ToLowerInvariant().ToCharArray();
            bool up = true;
            for (int i = 0; i < chars.Length; i++)
            {
                if (up && char.IsLetter(chars[i])) { chars[i] = char.ToUpperInvariant(chars[i]); up = false; }
                if (chars[i] == '-' || chars[i] == '\'' || chars[i] == '\u2019' || chars[i] == ' ') up = true;
            }
            return new string(chars);
        }
    }

    /// <summary>Name used for {name}: typed name, then messages.json name, then detected Windows name, then none.</summary>
    public static class NameProvider
    {
        public static string Resolve(Settings settings, string messagesFileName, string detectedFirstName)
        {
            string n;
            if (settings != null && NameRules.TryNormalize(settings.CustomName, out n) && n.Length > 0) return n;
            if (NameRules.TryNormalize(messagesFileName, out n) && n.Length > 0) return n;
            if ((settings == null || settings.UseWindowsName) && NameRules.TryNormalize(detectedFirstName, out n) && n.Length > 0) return n;
            return "";
        }
    }
}


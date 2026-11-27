using System;
using System.Text.RegularExpressions;

namespace UnicornPet.Core.Updater
{
    /// <summary>
    /// Canonical version identifiers used as folder names and in the state file.
    /// Exactly 3 or 4 numeric parts, ASCII digits only; always rendered as four parts (e.g. 1.2.3.0).
    /// </summary>
    public static class VersionId
    {
        static readonly Regex Strict = new Regex(@"^[0-9]{1,6}(\.[0-9]{1,6}){2,3}$", RegexOptions.CultureInvariant);

        public static string Canonical(string text)
        {
            if (text == null || !Strict.IsMatch(text)) return null;
            var p = text.Split('.');
            int Get(int i) => i < p.Length ? int.Parse(p[i], System.Globalization.CultureInfo.InvariantCulture) : 0;
            return new Version(Get(0), Get(1), Get(2), Get(3)).ToString();
        }

        public static string Canonical(Version v)
        {
            if (v == null) return null;
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0)).ToString();
        }

        public static Version ToVersion(string canonical)
        {
            var c = Canonical(canonical);
            return c == null ? null : Version.Parse(c);
        }
    }
}

using System;
using System.Text.RegularExpressions;

namespace UnicornPet.Core
{
    /// <summary>
    /// Version of a published message list: one to three numbers (1, 1.2 or 1.2.3), each 0 to 999999 with no leading zeros.
    /// The publishing script uses the very same pattern; a test keeps the two in step.
    /// </summary>
    public static class MessageVersion
    {
        public const string Pattern = @"^(0|[1-9][0-9]{0,5})(\.(0|[1-9][0-9]{0,5})){0,2}\z";
        static readonly Regex Rx = new Regex(Pattern, RegexOptions.CultureInvariant);

        public static int[] Parse(string text)
        {
            if (text == null || !Rx.IsMatch(text)) return null;
            var parts = text.Split('.');
            var v = new int[3];
            for (int i = 0; i < parts.Length; i++) v[i] = int.Parse(parts[i], System.Globalization.CultureInfo.InvariantCulture);
            return v;
        }

        /// <summary>Numeric comparison (1.10 is higher than 1.9). An invalid or missing version sorts lowest.</summary>
        public static int Compare(string a, string b)
        {
            var x = Parse(a); var y = Parse(b);
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;
            for (int i = 0; i < 3; i++) if (x[i] != y[i]) return x[i] < y[i] ? -1 : 1;
            return 0;
        }
    }
}

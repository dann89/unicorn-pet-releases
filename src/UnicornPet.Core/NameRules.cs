using System;
using System.Linq;

namespace UnicornPet.Core
{
    /// <summary>Validation for a user-typed or detected first name. Plain letters only, so it can never inject placeholders or markup.</summary>
    public static class NameRules
    {
        public const int MaxLength = 30;

        public static bool TryNormalize(string input, out string name)
        {
            name = "";
            if (input == null) return true;
            // compose first, so "e" + combining acute is judged as the letter it displays as; a lone surrogate cannot be normalised
            try { input = input.Normalize(System.Text.NormalizationForm.FormC); }
            catch (ArgumentException) { return false; }
            var t = input.Trim(' ');
            if (t.Length == 0) return true;
            while (t.Contains("  ")) t = t.Replace("  ", " ");
            if (t.Length > MaxLength) return false;
            bool anyLetter = false;
            foreach (var c in t)
            {
                if (char.IsLetter(c)) anyLetter = true;
                else if (c != ' ' && c != '-' && c != '\'' && c != '\u2019') return false;
            }
            if (!anyLetter) return false;
            name = t;
            return true;
        }
    }
}

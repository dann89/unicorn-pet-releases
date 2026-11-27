using System;

namespace UnicornPet.Core
{
    /// <summary>How big the pet is drawn. Large is the original size; it is the enum's zero value, so an unset value means Large.</summary>
    public enum PetSize { Large = 0, Medium = 1, Small = 2 }

    /// <summary>Everything that depends on the pet's size, derived from one scale factor so every size is consistent.</summary>
    public static class PetSizes
    {
        /// <summary>The original (Large) sprite size and pet window size, device-independent pixels.</summary>
        public const int LargeSprite = 200, LargeWindowHeight = 250;

        /// <summary>1.0, 0.7 or 0.5. Anything that is not a known size counts as Large.</summary>
        public static double Scale(PetSize size)
        {
            switch (size)
            {
                case PetSize.Small: return 0.5;
                case PetSize.Medium: return 0.7;
                default: return 1.0;
            }
        }

        /// <summary>Drawn sprite size (square): 200, 140 or 100.</summary>
        public static int SpritePx(PetSize size) { return (int)Math.Round(LargeSprite * Scale(size)); }

        /// <summary>Pet window width: the sprite's width.</summary>
        public static int WindowWidth(PetSize size) { return SpritePx(size); }

        /// <summary>Pet window height: the sprite plus head room for the hop and the sparkles (250, 175 or 125).</summary>
        public static int WindowHeight(PetSize size) { return (int)Math.Round(LargeWindowHeight * Scale(size)); }

        /// <summary>How the size is written in settings.json.</summary>
        public static string ToText(PetSize size)
        {
            switch (size)
            {
                case PetSize.Small: return "Small";
                case PetSize.Medium: return "Medium";
                default: return "Large";
            }
        }

        /// <summary>Reads a size written by <see cref="ToText"/> (any letter case). Anything else, numbers included, gives Large.</summary>
        public static PetSize Parse(string text)
        {
            var t = (text ?? "").Trim();
            if (string.Equals(t, "Small", StringComparison.OrdinalIgnoreCase)) return PetSize.Small;
            if (string.Equals(t, "Medium", StringComparison.OrdinalIgnoreCase)) return PetSize.Medium;
            return PetSize.Large;
        }
    }

    /// <summary>Text that uses the unicorn's own name (Settings.PetName). Built in code, never from messages.json.</summary>
    public static class PetNameText
    {
        public const string Product = "Unicorn Pet";

        /// <summary>What the unicorn is called until the person changes or clears it.</summary>
        public const string DefaultName = "Fifi";

        /// <summary>The pet's name after the name rules, or "" when it has none (or an invalid one).</summary>
        public static string Clean(string petName)
        {
            string n;
            return NameRules.TryNormalize(petName, out n) ? n : "";
        }

        /// <summary>The one-off hello after the pet is first given a name, or null when it has no name.</summary>
        public static string Intro(string userName, string petName)
        {
            var pet = Clean(petName);
            if (pet.Length == 0) return null;
            var user = Clean(userName);
            return user.Length > 0 ? "Hi " + user + ", I'm " + pet + "!" : "Hi, I'm " + pet + "!";
        }

        /// <summary>The tray icon's tooltip: the pet's name, or the product name when it has none.</summary>
        public static string TrayText(string petName)
        {
            var pet = Clean(petName);
            return pet.Length > 0 ? pet : Product;
        }

        /// <summary>The Settings window title.</summary>
        public static string SettingsTitle(string petName)
        {
            var pet = Clean(petName);
            return pet.Length > 0 ? pet + " – settings" : Product + " settings";
        }
    }
}

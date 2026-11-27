using System;

namespace UnicornPet.Core.Updater
{
    /// <summary>The one place that decides which URLs the updater may touch: absolute https only (file:// in Debug builds only).</summary>
    public static class UrlRules
    {
        public const int MaxLength = 2048;

        public static bool IsAllowed(string url)
        {
            if (string.IsNullOrWhiteSpace(url) || url.Length > MaxLength) return false;
            foreach (var c in url) if (c < 0x20 || c == 0x7F) return false;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) return false;
            if (u.Scheme == Uri.UriSchemeHttps)
                return !string.IsNullOrEmpty(u.Host) && string.IsNullOrEmpty(u.UserInfo);
#if DEBUG
            if (u.Scheme == Uri.UriSchemeFile && url.Trim().StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return true;
#endif
            return false;
        }
    }
}


namespace UnicornPet.Core.Updater
{
    public static class UpdateConfig
    {
        /// <summary>
        /// Where the app looks for new releases. Not hard-coded: the app sets it at start-up from the "UpdateManifestUrl"
        /// setting in UnicornPet.exe.config. Empty means no default, so only a URL saved in settings.json is used.
        /// </summary>
        public static string DefaultManifestUrl { get; set; } = "";

        /// <summary>
        /// The manifest URL to check: a non-empty URL from settings must itself be valid (otherwise no updates, no fallback);
        /// an empty one falls back to the config default. Anything that is not an allowed https URL means no updates (null).
        /// </summary>
        public static string ChooseManifestUrl(string settingsUrl)
        {
            var s = settingsUrl == null ? "" : settingsUrl.Trim();
            var candidate = s.Length > 0 ? s : (DefaultManifestUrl ?? "").Trim();
            return UrlRules.IsAllowed(candidate) ? candidate : null;
        }

        /// <summary>Alias of ReleaseKey.PublicKeyXml (kept so the full build's code keeps compiling).</summary>
        public const string PublicKeyXml = ReleaseKey.PublicKeyXml;
    }
}


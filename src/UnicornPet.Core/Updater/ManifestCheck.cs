namespace UnicornPet.Core.Updater
{
    /// <summary>Checks a manifest file's text exactly the way the running app would: parse, https URL, signature.</summary>
    public static class ManifestCheck
    {
        /// <summary>Returns null when the manifest would be accepted, otherwise a short reason.</summary>
        public static string Evaluate(string json, ManifestVerifier verifier)
        {
            var m = Manifest.Parse(json);
            if (m == null) return "not a valid manifest";
            if (!UrlRules.IsAllowed(m.Url) || !m.Url.Trim().StartsWith("https://", System.StringComparison.OrdinalIgnoreCase)) return "the zip URL is not an https URL";
            if (verifier == null || !verifier.Verify(m)) return "signature does not verify against the embedded public key";
            return null;
        }
    }
}

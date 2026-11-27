using System;
using System.Collections.Generic;

namespace UnicornPet.Core.Updater
{
    public record Manifest(Version Version, string Url, string Sha256, string Signature)
    {
        /// <summary>The exact string the release signature covers.</summary>
        public string SignedPayload => Version + "|" + Url + "|" + Sha256;

        public static Manifest Parse(string json)
        {
            try
            {
                var r = MiniJson.Parse((json ?? "").TrimStart('﻿')) as Dictionary<string, object>;
                if (r == null) return null;
                var v = MiniJson.GetString(r, "version");
                var u = MiniJson.GetString(r, "url");
                var s = MiniJson.GetString(r, "sha256");
                var g = MiniJson.GetString(r, "signature");
                if (string.IsNullOrWhiteSpace(v) || string.IsNullOrWhiteSpace(u) || string.IsNullOrWhiteSpace(s) || string.IsNullOrWhiteSpace(g)) return null;
                Version ver;
                if (!Version.TryParse(v, out ver)) return null;
                return new Manifest(ver, u, s, g);
            }
            catch { return null; }
        }

        public static Version Normalize(Version v) =>
            new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnicornPet.Core.Updater;

namespace UnicornPet.Core
{
    /// <summary>
    /// Refreshes the message list from the internet. Text only: it never downloads or runs code.
    /// One small signed manifest, then one small signed list; both are checked and the list is validated before a single byte is kept.
    /// Any failure is silent and leaves the current messages exactly as they were.
    /// </summary>
    public sealed class MessageUpdater
    {
        /// <summary>Where the signed manifest is published (assets of a release in the releases repository).</summary>
        public const string DefaultManifestUrl = "https://github.com/dann89/unicorn-pet-releases/releases/latest/download/messages.manifest.json";

        /// <summary>
        /// Prefix of everything a message manifest signs, so a code-release signature can never be passed off as a message signature
        /// (and the other way round). The signed text is: unicornpet-messages-v1|version|url|sha256
        /// </summary>
        public const string SigningDomain = "unicornpet-messages-v1";

        public const long MaxBytes = 64 * 1024;
        public static readonly TimeSpan MinInterval = TimeSpan.FromHours(24);
        public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

        static readonly Regex Sha256Rx = new Regex(@"^[0-9a-fA-F]{64}\z", RegexOptions.CultureInvariant);

        readonly FetchFunc _fetch;
        readonly ManifestVerifier _verifier;
        readonly string _remotePath;
        readonly Func<DateTime> _now;
        int _busy;

        public MessageUpdater(FetchFunc fetch, ManifestVerifier verifier, string remoteFilePath, Func<DateTime> now = null)
        {
            _fetch = fetch; _verifier = verifier; _remotePath = remoteFilePath; _now = now ?? (() => DateTime.UtcNow);
        }

        public bool IsBusy => Volatile.Read(ref _busy) != 0;

        /// <summary>
        /// Returns true only when a newer, signed, valid list was written. Never throws. At most one check runs at a time and at most
        /// one every 24 hours; the time of the attempt is saved (persist returns true) BEFORE any request is made.
        /// </summary>
        public async Task<bool> CheckAsync(string manifestUrl, Settings settings, Func<bool> persist, bool force = false)
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return false;
            try
            {
                if (settings == null || persist == null) return false;
                if (!settings.FetchMessages && !force) return false;
                if (!UrlRules.IsAllowed(manifestUrl)) return false;

                var now = _now();
                var last = settings.LastMessageCheck;
                // a recorded check time in the future means the clock went backwards: clamp it to now, save it, and wait a full interval
                if (!force && last.HasValue && last.Value > now) { settings.LastMessageCheck = now; persist(); return false; }
                if (!force && last.HasValue && now - last.Value < MinInterval) return false;
                settings.LastMessageCheck = now;
                if (!persist()) return false;

                var rawManifest = await _fetch(manifestUrl.Trim(), MaxBytes, Deadline).ConfigureAwait(false);
                if (rawManifest == null || rawManifest.Length == 0 || rawManifest.Length > MaxBytes) return false;
                var manifest = ParseManifest(Strict.GetString(rawManifest));
                if (manifest == null) return false;

                if (!_verifier.VerifyPayload(SigningDomain + "|" + manifest.Version + "|" + manifest.Url + "|" + manifest.Sha256, manifest.Signature)) return false;

                // replay floor: the higher of what settings.json remembers and what sits next to the list (settings can be lost or reset)
                var floor = Higher(settings.RemoteMessagesVersion, ReadSidecar());
                if (floor != null && MessageVersion.Compare(manifest.Version, floor) <= 0) return false;

                var rawList = await _fetch(manifest.Url, MaxBytes, Deadline).ConfigureAwait(false);
                if (rawList == null || rawList.Length == 0 || rawList.Length > MaxBytes) return false;
                if (!string.Equals(Hex(rawList), manifest.Sha256, StringComparison.OrdinalIgnoreCase)) return false;

                MessageBank bank;
                if (MessageValidator.Validate(Strict.GetString(rawList), out bank) != null) return false;

                // commit order: the replay floor FIRST (beside the list, then in settings.json). If either cannot be saved the list is not
                // replaced and the answer is false; a crash between the steps leaves the old list with a floor at least as high as the new version.
                if (!AtomicFile.WriteText(SidecarPath, manifest.Version)) return false;
                settings.RemoteMessagesVersion = manifest.Version;
                if (!persist()) return false;
                if (!AtomicFile.WriteBytes(_remotePath, rawList)) return false;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                Volatile.Write(ref _busy, 0);
            }
        }

        string SidecarPath => _remotePath + ".version";

        string ReadSidecar()
        {
            try
            {
                if (!File.Exists(SidecarPath)) return null;
                var text = File.ReadAllText(SidecarPath).Trim();
                return text.Length <= 20 && MessageVersion.Parse(text) != null ? text : null;
            }
            catch { return null; }
        }

        /// <summary>The higher of two versions; an invalid or missing one is ignored. Null when neither is valid.</summary>
        static string Higher(string a, string b)
        {
            bool va = MessageVersion.Parse(a) != null, vb = MessageVersion.Parse(b) != null;
            if (va && vb) return MessageVersion.Compare(a, b) >= 0 ? a : b;
            return va ? a : vb ? b : null;
        }

        static readonly UTF8Encoding Strict = new UTF8Encoding(false, true);

        sealed class ManifestData { public string Version, Url, Sha256, Signature; }

        static ManifestData ParseManifest(string json)
        {
            try
            {
                var o = MiniJson.Parse(json) as Dictionary<string, object>;
                if (o == null) return null;
                var m = new ManifestData
                {
                    Version = MiniJson.GetString(o, "version"),
                    Url = MiniJson.GetString(o, "url"),
                    Sha256 = MiniJson.GetString(o, "sha256"),
                    Signature = MiniJson.GetString(o, "signature"),
                };
                if (MessageVersion.Parse(m.Version) == null) return null;
                if (!UrlRules.IsAllowed(m.Url)) return null;
                if (m.Url.Trim() != m.Url) return null;
                if (m.Sha256 == null || !Sha256Rx.IsMatch(m.Sha256)) return null;
                if (string.IsNullOrEmpty(m.Signature)) return null;
                return m;
            }
            catch { return null; }
        }

        static string Hex(byte[] data)
        {
            using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(data)).Replace("-", "");
        }
    }
}

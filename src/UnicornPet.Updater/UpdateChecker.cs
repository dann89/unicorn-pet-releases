using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UnicornPet.Core.Updater
{

    public class UpdateChecker
    {
        public static readonly TimeSpan MinInterval = TimeSpan.FromHours(6);
        public const long MaxManifestBytes = 64 * 1024;
        public const long MaxZipBytes = 100L * 1024 * 1024;
        public static readonly TimeSpan ManifestDeadline = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan ZipDeadline = TimeSpan.FromSeconds(120);

        readonly FetchFunc _fetch;
        readonly ManifestVerifier _verifier;
        readonly UpdateStager _stager;
        readonly Version _current;
        readonly Func<DateTime> _now;
        int _busy;

        public UpdateChecker(FetchFunc fetch, ManifestVerifier verifier, UpdateStager stager, Version current, Func<DateTime> now = null)
        {
            _fetch = fetch; _verifier = verifier; _stager = stager; _current = current; _now = now ?? (() => DateTime.UtcNow);
        }

        public UpdateChecker(Func<string, Task<byte[]>> http, ManifestVerifier verifier, UpdateStager stager, Version current, Func<DateTime> now = null)
            : this((url, max, deadline) => http(url), verifier, stager, current, now)
        {
        }

        public bool IsBusy => Volatile.Read(ref _busy) != 0;

        static bool IsAllowedUrl(string url) => UrlRules.IsAllowed(url);

        /// <summary>
        /// Returns true only when a newer, signed, hash-verified release was staged. Never throws, and only one check runs at a time
        /// (an overlapping call returns false and changes nothing). <paramref name="persist"/> is called right after the attempt time
        /// is recorded and before any network request, so a crash cannot cause a re-check inside the interval.
        /// </summary>
        public async Task<bool> CheckAsync(string manifestUrl, Settings settings = null, bool force = false, Action persist = null)
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return false;
            try
            {
                if (!IsAllowedUrl(manifestUrl)) return false;
                var now = _now();
                if (settings != null)
                {
                    var last = settings.LastUpdateCheck;
                    if (!force && last.HasValue && last.Value <= now && now - last.Value < MinInterval) return false;
                    settings.LastUpdateCheck = now;
                    try { persist?.Invoke(); } catch { }
                }

                var raw = await _fetch(manifestUrl.Trim(), MaxManifestBytes, ManifestDeadline).ConfigureAwait(false);
                if (raw == null || raw.Length == 0 || raw.Length > MaxManifestBytes) return false;
                var m = Manifest.Parse(Encoding.UTF8.GetString(raw));
                if (m == null) return false;
                if (!_verifier.Verify(m)) return false;
                if (Manifest.Normalize(m.Version) <= Manifest.Normalize(_current)) return false;
                if (_stager.ShouldSkip(m.Version, _current)) return false;
                if (!IsAllowedUrl(m.Url)) return false;

                var zip = await _fetch(m.Url, MaxZipBytes, ZipDeadline).ConfigureAwait(false);
                return _stager.Stage(m, zip, _current) == StageResult.Staged;
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
    }
}

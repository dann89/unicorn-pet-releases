using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnicornPet.Core.Updater;

namespace UnicornPet.Core
{
    /// <summary>Where the person's messages come from (Settings, Messages). Built-in keeps the 2.0.0 chain.</summary>
    public enum MessageSourceKind { BuiltIn = 0, File = 1, Web = 2 }

    /// <summary>How the message source is written in settings.json.</summary>
    public static class MessageSources
    {
        public static string ToText(MessageSourceKind k)
        {
            switch (k)
            {
                case MessageSourceKind.File: return "File";
                case MessageSourceKind.Web: return "Web";
                default: return "BuiltIn";
            }
        }

        /// <summary>Reads a value written by <see cref="ToText"/> (any letter case). Anything else, numbers included, gives BuiltIn.</summary>
        public static MessageSourceKind Parse(string text)
        {
            var t = (text ?? "").Trim();
            if (string.Equals(t, "File", StringComparison.OrdinalIgnoreCase)) return MessageSourceKind.File;
            if (string.Equals(t, "Web", StringComparison.OrdinalIgnoreCase)) return MessageSourceKind.Web;
            return MessageSourceKind.BuiltIn;
        }
    }

    /// <summary>
    /// The rules for a person's own message file or address. Pure string checks: nothing here touches the disk or the network.
    /// </summary>
    public static class CustomSourceRules
    {
        public const long MaxBytes = 64 * 1024;
        public const int MaxPathLength = 260;

        /// <summary>An absolute https address that also passes the updater's URL rules (no user info, no control characters).</summary>
        public static bool IsAllowedUrl(string url)
        {
            if (!UrlRules.IsAllowed(url)) return false;
            return url.Trim().StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A full local path (X:\...) or a share path (\\server\share\...) ending in .json, at most 260 characters.
        /// Never a device or pipe (\\.\), never a raw path (\\?\), never a stream name, never forward slashes.
        /// </summary>
        public static bool IsAllowedFile(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > MaxPathLength) return false;
            foreach (var c in path)
            {
                if (c < 0x20 || c == 0x7F) return false;
                if (c == '/' || c == '<' || c == '>' || c == '"' || c == '|' || c == '?' || c == '*') return false;
            }
            if (path.Length > 2 && path.IndexOf(':', 2) >= 0) return false;   // only the drive colon at index 1
            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;

            bool drive = path.Length >= 3 && IsLetter(path[0]) && path[1] == ':' && path[2] == '\\';
            if (drive) return path.IndexOf(':') == 1 && NoEmptyParts(path.Substring(3), 1);

            if (!path.StartsWith(@"\\", StringComparison.Ordinal) || path.IndexOf(':') >= 0) return false;
            if (path.StartsWith(@"\\.\", StringComparison.Ordinal) || path.StartsWith(@"\\?\", StringComparison.Ordinal)) return false;
            // \\server\share\file.json at least: server, share and a file name, none empty
            var rest = path.Substring(2);
            var first = rest.Split('\\')[0];
            if (first == "." || first == "?") return false;
            if (rest.Split('\\').Length > 1 && string.Equals(rest.Split('\\')[1], "pipe", StringComparison.OrdinalIgnoreCase)) return false;   // \\server\pipe\...
            return NoEmptyParts(rest, 3);
        }

        static readonly string[] DeviceNames = { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$", "CLOCK$" };

        /// <summary>A reserved device name (CON, NUL, COM1, ...) with any extension: Windows opens the device, not a file.</summary>
        static bool IsDeviceName(string part)
        {
            int dot = part.IndexOf('.');
            var stem = (dot < 0 ? part : part.Substring(0, dot)).TrimEnd(' ');
            foreach (var d in DeviceNames) if (string.Equals(stem, d, StringComparison.OrdinalIgnoreCase)) return true;
            if (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && stem[3] >= '0' && stem[3] <= '9') return true;
            return false;
        }

        static bool IsLetter(char c) { return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'); }

        static bool NoEmptyParts(string rest, int minParts)
        {
            var parts = rest.Split('\\');
            if (parts.Length < minParts) return false;
            foreach (var p in parts) if (p.Length == 0 || p == "." || p == ".." || IsDeviceName(p)) return false;
            return true;
        }

        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>Strict UTF-8 text without one leading byte order mark; null when empty, over <see cref="MaxBytes"/> or not UTF-8.</summary>
        public static string Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxBytes) return null;
            int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            if (start == bytes.Length) return null;
            try { return StrictUtf8.GetString(bytes, start, bytes.Length - start); }
            catch (ArgumentException) { return null; }   // DecoderFallbackException
        }

        /// <summary>The line shown under the box when Save is pressed with an unusable path or address; "" when fine.</summary>
        public static string FormError(MessageSourceKind kind, string file, string url)
        {
            if (kind == MessageSourceKind.File && !IsAllowedFile(file)) return "Type the full path of a .json file.";
            if (kind == MessageSourceKind.Web && !IsAllowedUrl(url)) return "Type an https address.";
            return "";
        }

        /// <summary>
        /// What the person typed or pasted, tidied: surrounding blanks and one pair of surrounding double quotes
        /// (Explorer's "Copy as path") are removed. Never null.
        /// </summary>
        public static string CleanPath(string typed)
        {
            var t = (typed ?? "").Trim();
            if (t.Length >= 2 && t[0] == '"' && t[t.Length - 1] == '"') t = t.Substring(1, t.Length - 2).Trim();
            return t;
        }
    }

    public enum CustomLoadOutcome { Loaded, Missing, TooLarge, Unreadable, Invalid, TimedOut, Refused, NetworkFailed, CacheFailed, Busy }

    public sealed class CustomLoadResult
    {
        public CustomLoadOutcome Outcome { get; }
        /// <summary>The validated list when Outcome is Loaded, otherwise null.</summary>
        public CustomMessages Messages { get; }
        /// <summary>The validator's fixed reason when Outcome is Invalid (never file content), otherwise "".</summary>
        public string Problem { get; }

        public CustomLoadResult(CustomLoadOutcome outcome, CustomMessages messages = null, string problem = "")
        {
            Outcome = outcome; Messages = messages; Problem = problem ?? "";
        }
    }

    /// <summary>Reads at most maxBytes + 1 bytes of a file. null = no such file; throws on an I/O error.</summary>
    public delegate byte[] ReadFileFunc(string path, long maxBytes);

    /// <summary>
    /// Loads the person's own list from a file or an https address. Every read runs off the calling thread and is raced against a time
    /// limit (a hung share or server is abandoned, never waited for); at most one load runs at a time; nothing here ever throws.
    /// A web list is kept in the cache file only after it passed every check, and a web list never sets the person's name.
    /// </summary>
    public sealed class CustomMessageLoader
    {
        public static readonly TimeSpan FileLimit = TimeSpan.FromSeconds(5), WebDeadline = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan MinInterval = TimeSpan.FromHours(24), RetryInterval = TimeSpan.FromHours(1), ReloadGap = TimeSpan.FromSeconds(10);

        readonly FetchFunc _fetch;
        readonly ReadFileFunc _readFile;
        readonly string _cachePath;
        readonly TimeSpan _fileLimit;
        int _busy;

        public CustomMessageLoader(FetchFunc fetch, ReadFileFunc readFile, string cachePath, TimeSpan? fileLimit = null)
        {
            _fetch = fetch; _readFile = readFile; _cachePath = cachePath; _fileLimit = fileLimit ?? FileLimit;
        }

        /// <summary>A load is in flight (including a file read that timed out but has not returned yet).</summary>
        public bool IsBusy => Volatile.Read(ref _busy) != 0;

        static Task<CustomLoadResult> Done(CustomLoadOutcome outcome) => Task.FromResult(new CustomLoadResult(outcome));

        /// <summary>Reads the person's own file (it may set the name). Never throws.</summary>
        public Task<CustomLoadResult> LoadFileAsync(string path)
        {
            if (!CustomSourceRules.IsAllowedFile(path)) return Done(CustomLoadOutcome.Refused);
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return Done(CustomLoadOutcome.Busy);
            try
            {
                return Task.Run(() => LoadFileBody(path));
            }
            catch
            {
                Volatile.Write(ref _busy, 0);
                return Done(CustomLoadOutcome.Unreadable);
            }
        }

        async Task<CustomLoadResult> LoadFileBody(string path)
        {
            bool release = true;
            try
            {
                var read = Task.Run(() => _readFile(path, CustomSourceRules.MaxBytes));
                var first = await Task.WhenAny(read, Task.Delay(_fileLimit)).ConfigureAwait(false);
                if (first != read)
                {
                    // abandoned: the flag is released only when the stuck read finally returns, so no second read piles up behind it
                    release = false;
                    var ignored = read.ContinueWith(t => { var observed = t.Exception; Volatile.Write(ref _busy, 0); }, TaskScheduler.Default);
                    return new CustomLoadResult(CustomLoadOutcome.TimedOut);
                }
                byte[] raw;
                try { raw = await read.ConfigureAwait(false); }
                catch (Exception) { return new CustomLoadResult(CustomLoadOutcome.Unreadable); }
                return Check(raw, true);
            }
            catch
            {
                return new CustomLoadResult(CustomLoadOutcome.Unreadable);
            }
            finally
            {
                if (release) Volatile.Write(ref _busy, 0);
            }
        }

        /// <summary>Fetches the web list (it never sets the name) and keeps it in the cache before reporting Loaded. Never throws.</summary>
        public Task<CustomLoadResult> FetchWebAsync(string url)
        {
            if (!CustomSourceRules.IsAllowedUrl(url)) return Done(CustomLoadOutcome.Refused);
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return Done(CustomLoadOutcome.Busy);
            try
            {
                // the whole fetch runs off the calling thread: HttpClient on .NET Framework can do proxy discovery and DNS synchronously
                // a thread of its own: a pool thread may be the caller's, free again by the time the work is picked up
                return Task.Factory.StartNew(() => FetchWebBody(url.Trim()), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
            }
            catch
            {
                Volatile.Write(ref _busy, 0);
                return Done(CustomLoadOutcome.NetworkFailed);
            }
        }

        async Task<CustomLoadResult> FetchWebBody(string url)
        {
            try
            {
                byte[] raw;
                try { raw = await _fetch(url, CustomSourceRules.MaxBytes, WebDeadline).ConfigureAwait(false); }
                catch (TimeoutException) { return new CustomLoadResult(CustomLoadOutcome.TimedOut); }
                catch (InvalidDataException) { return new CustomLoadResult(CustomLoadOutcome.TooLarge); }
                catch (HttpRequestException) { return new CustomLoadResult(CustomLoadOutcome.NetworkFailed); }
                catch (Exception) { return new CustomLoadResult(CustomLoadOutcome.NetworkFailed); }
                if (raw == null || raw.Length == 0) return new CustomLoadResult(CustomLoadOutcome.NetworkFailed);
                var result = Check(raw, false);
                if (result.Outcome != CustomLoadOutcome.Loaded) return result;
                if (!AtomicFile.WriteBytes(_cachePath, raw)) return new CustomLoadResult(CustomLoadOutcome.CacheFailed);
                return result;
            }
            catch
            {
                return new CustomLoadResult(CustomLoadOutcome.NetworkFailed);
            }
            finally
            {
                Volatile.Write(ref _busy, 0);
            }
        }

        /// <summary>The cached web list (a small local read, as messages.remote.json is read today). It never sets the name. Never throws.</summary>
        public CustomLoadResult ReadCache()
        {
            try
            {
                var raw = ReadCapped(_cachePath, CustomSourceRules.MaxBytes);
                if (raw == null) return new CustomLoadResult(CustomLoadOutcome.Missing);
                return Check(raw, false);
            }
            catch
            {
                return new CustomLoadResult(CustomLoadOutcome.Unreadable);
            }
        }

        static CustomLoadResult Check(byte[] raw, bool allowName)
        {
            if (raw == null) return new CustomLoadResult(CustomLoadOutcome.Missing);
            if (raw.Length > CustomSourceRules.MaxBytes) return new CustomLoadResult(CustomLoadOutcome.TooLarge);
            if (raw.Length == 0) return new CustomLoadResult(CustomLoadOutcome.Invalid, null, "empty");
            var text = CustomSourceRules.Decode(raw);
            if (text == null) return new CustomLoadResult(CustomLoadOutcome.Invalid, null, "not UTF-8 text");
            CustomMessages m;
            var problem = CustomMessageValidator.Validate(text, allowName, out m);
            if (problem != null) return new CustomLoadResult(CustomLoadOutcome.Invalid, null, problem);
            return new CustomLoadResult(CustomLoadOutcome.Loaded, m);
        }

        /// <summary>
        /// Whether a web fetch may start now. A last time in the future (the clock went back) is set to now and waits. Forced (Reload now)
        /// fetches keep a 10-second gap; otherwise one a day.
        /// </summary>
        public static bool Due(DateTime utcNow, ref DateTime? lastUtc, bool force)
        {
            if (!lastUtc.HasValue) return true;
            if (lastUtc.Value > utcNow)
            {
                lastUtc = utcNow;
                return force;   // the clock went back: wait a full interval, but "Reload now" still works
            }
            return utcNow - lastUtc.Value >= (force ? ReloadGap : MinInterval);
        }

        /// <summary>The time to save after an attempt that brought no list, so the next try is allowed one hour later.</summary>
        public static DateTime RetryStamp(DateTime attemptUtc) { return attemptUtc - (MinInterval - RetryInterval); }

        /// <summary>The real <see cref="ReadFileFunc"/>: at most maxBytes + 1 bytes, shared with writers, null when there is no such file.</summary>
        public static byte[] ReadCapped(string path, long maxBytes)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan))
                using (var ms = new MemoryStream())
                {
                    var buf = new byte[16384];
                    long limit = maxBytes + 1;
                    while (ms.Length < limit)
                    {
                        int n = fs.Read(buf, 0, (int)Math.Min(buf.Length, limit - ms.Length));
                        if (n <= 0) break;
                        ms.Write(buf, 0, n);
                    }
                    return ms.ToArray();
                }
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }
    }

    /// <summary>One plain status line for Settings, built from fixed text only (never from file content).</summary>
    public static class CustomStatus
    {
        public static string Describe(CustomLoadOutcome outcome, string problem, bool haveGoodCopy)
        {
            if (outcome == CustomLoadOutcome.Loaded) return "Loaded.";
            string reason;
            switch (outcome)
            {
                case CustomLoadOutcome.Missing: reason = "File not found."; break;
                case CustomLoadOutcome.TooLarge: reason = "Too big (the limit is 64 KB)."; break;
                case CustomLoadOutcome.Unreadable: reason = "Couldn't read the file."; break;
                case CustomLoadOutcome.Invalid: reason = string.IsNullOrEmpty(problem) ? "Not usable." : "Not usable: " + problem + "."; break;
                case CustomLoadOutcome.TimedOut: reason = "Took too long."; break;
                case CustomLoadOutcome.Refused: reason = "That address or path can't be used."; break;
                case CustomLoadOutcome.NetworkFailed: reason = "Couldn't reach the address."; break;
                case CustomLoadOutcome.CacheFailed: reason = "Couldn't keep a copy."; break;
                case CustomLoadOutcome.Busy: reason = "Already loading."; break;
                default: reason = "Not loaded."; break;
            }
            return reason + (haveGoodCopy ? " Still using the last good copy." : " Using the built-in lines.");
        }
    }
}

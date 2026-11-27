using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace UnicornPet.Core.Updater
{
    public enum StageResult { Staged, HashMismatch, BadArchive, AlreadyPresent, Rejected, Error }

    /// <summary>
    /// Stages verified releases under versionsDir and decides which folder to launch.
    /// All state lives in one file, state.json, in the parent of versionsDir. Every transition is a single atomic write
    /// made while holding an exclusive lock file, so interruption or concurrent launches cannot leave a half-applied state.
    /// </summary>
    public class UpdateStager
    {
        public const string ExeName = "UnicornPet.exe";
        public const string VerifiedMarker = ".verified";
        public const int MaxLaunchAttempts = 2;

        const long MaxUnpackedDefault = 200L * 1024 * 1024;
        const int MaxEntriesDefault = 2000;

        readonly string _versions;
        readonly string _root;

        public UpdateStager(string versionsDir)
        {
            _versions = versionsDir;
            _root = Path.GetDirectoryName(versionsDir.TrimEnd('\\', '/')) ?? versionsDir;
        }

        string StatePath => Path.Combine(_root, "state.json");
        string LockPath => Path.Combine(_root, "state.lock");
        string DirOf(string id) => Path.Combine(_versions, id);

        // ---------- locking and persistence ----------

        FileStream AcquireLock()
        {
            try { Directory.CreateDirectory(_root); } catch { return null; }
            for (int i = 0; i < 100; i++)
            {
                try { return new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) { Thread.Sleep(50); }
                catch (UnauthorizedAccessException) { Thread.Sleep(50); }
            }
            return null;
        }

        T Locked<T>(Func<T> body, T onLockFailure)
        {
            using (var l = AcquireLock())
            {
                if (l == null) return onLockFailure;
                return body();
            }
        }

        static string Normalise(string id) => VersionId.Canonical(id);

        bool IsValidDirName(string id)
        {
            if (id == null || Normalise(id) != id) return false;
            try
            {
                var full = Path.GetFullPath(Path.Combine(_versions, id));
                var parent = Path.GetDirectoryName(full);
                var root = Path.GetFullPath(_versions).TrimEnd('\\', '/');
                return string.Equals(parent, root, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        bool HasExe(string id) => IsValidDirName(id) && File.Exists(Path.Combine(DirOf(id), ExeName));

        static bool Newer(string a, string b)
        {
            var va = VersionId.ToVersion(a); var vb = VersionId.ToVersion(b);
            if (va == null) return false;
            if (vb == null) return true;
            return va > vb;
        }

        static string MaxOf(params string[] ids)
        {
            string best = null;
            foreach (var i in ids) if (i != null && (best == null || Newer(i, best))) best = i;
            return best;
        }

        /// <summary>Reads state tolerantly. Missing or corrupt state yields a recovered state and Recovered=true.</summary>
        UpdateState Load(out bool recovered)
        {
            recovered = false;
            try
            {
                if (File.Exists(StatePath))
                {
                    var s = JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(StatePath));
                    if (s != null) { bool ch; var clean = Sanitise(s, out ch); recovered = ch; return clean; }
                }
            }
            catch { }
            recovered = true;
            return Recover(new HashSet<string>());
        }

        static UpdateState Sanitise(UpdateState s, out bool changedOut)
        {
            bool changed = false;
            Func<string, string> C = v => { var c = Normalise(v); if (c != v) changed = true; return c; };
            var r = new UpdateState
            {
                Current = C(s.Current),
                Previous = C(s.Previous),
                Pending = C(s.Pending),
                Rejected = (s.Rejected ?? new List<string>()).Select(Normalise).Where(x => x != null).Distinct().ToList()
            };
            if (s.Trial != null)
            {
                var tv = Normalise(s.Trial.Version);
                if (tv != null) r.Trial = new TrialState { Version = tv, Attempts = Math.Max(0, Math.Min(10, s.Trial.Attempts)), StartedUtc = s.Trial.StartedUtc, Token = s.Trial.Token };
                else changed = true;
            }
            changedOut = changed;
            return r;
        }

        UpdateState Recover(HashSet<string> rejected)
        {
            var best = ValidDirs().Where(d => !rejected.Contains(d)).OrderByDescending(d => VersionId.ToVersion(d)).FirstOrDefault();
            return new UpdateState { Current = best, Rejected = rejected.ToList() };
        }

        IEnumerable<string> ValidDirs()
        {
            try
            {
                if (!Directory.Exists(_versions)) return new string[0];
                return Directory.GetDirectories(_versions).Select(Path.GetFileName).Where(HasExe).ToList();
            }
            catch { return new string[0]; }
        }

        bool Save(UpdateState s)
        {
            string tmp = null;
            try
            {
                Directory.CreateDirectory(_root);
                tmp = StatePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
                if (File.Exists(StatePath)) File.Replace(tmp, StatePath, null); else File.Move(tmp, StatePath);
                tmp = null;
                return true;
            }
            catch { return false; }
            finally { if (tmp != null) { try { File.Delete(tmp); } catch { } } }
        }

        /// <summary>Current state as the launcher would see it (read-only; used by diagnostics and tests).</summary>
        public UpdateState ReadState() { bool r; return Load(out r); }

        public string CurrentVersion => ReadState().Current;

        // ---------- install / reinstall ----------

        /// <summary>Resets state for a fresh install of one version: no pending release, no trial, previous cleared. Rolled-back versions stay rejected.</summary>
        public bool InitState(string version)
        {
            var id = Normalise(version);
            if (id == null) return false;
            return Locked(() =>
            {
                bool rec;
                var old = Load(out rec);
                var rejected = old.Rejected.Where(x => x != id).ToList();
                bool saved = Save(new UpdateState { Current = id, Rejected = rejected });
                if (saved) RemoveOtherVersionDirs(id);
                return saved;
            }, false);
        }

        void RemoveOtherVersionDirs(string keep)
        {
            try
            {
                if (!Directory.Exists(_versions)) return;
                foreach (var d in Directory.GetDirectories(_versions))
                {
                    var n = Path.GetFileName(d);
                    if (n == keep || n.StartsWith(".") || !IsValidDirName(n)) continue;
                    try { Directory.Delete(d, true); } catch { }
                }
            }
            catch { }
        }

        // ---------- staging ----------

        public bool ShouldSkip(Version v, Version running = null)
        {
            var id = VersionId.Canonical(v);
            if (id == null) return true;
            bool rec;
            var st = Load(out rec);
            if (st.Rejected.Contains(id)) return true;
            var floor = MaxOf(st.Current, st.Pending, running == null ? null : VersionId.Canonical(running));
            if (floor != null && !Newer(id, floor)) return true;
            return false;
        }

        public StageResult Stage(Manifest m, byte[] zip, Version running = null)
        {
            string staging = null;
            try
            {
                if (m == null || zip == null || zip.Length == 0) return StageResult.Rejected;
                var id = VersionId.Canonical(m.Version);
                if (id == null || !IsValidDirName(id)) return StageResult.Rejected;
                if (ShouldSkip(m.Version, running)) return StageResult.Rejected;

                string sha;
                using (var h = SHA256.Create()) sha = BitConverter.ToString(h.ComputeHash(zip)).Replace("-", "");
                if (!string.Equals(sha, m.Sha256, StringComparison.OrdinalIgnoreCase)) return StageResult.HashMismatch;

                var target = DirOf(id);
                bool alreadyExtracted = File.Exists(Path.Combine(target, ExeName)) && File.Exists(Path.Combine(target, VerifiedMarker));
                if (!alreadyExtracted)
                {
                    Directory.CreateDirectory(_versions);
                    staging = Path.Combine(_versions, ".staging-" + id + "-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(staging);
                    try { ZipExtractor.Extract(zip, staging, MaxUnpackedDefault, MaxEntriesDefault); }
                    catch (InvalidDataException) { return StageResult.BadArchive; }
                    catch (UnauthorizedAccessException) { return StageResult.BadArchive; }
                    catch (ArgumentException) { return StageResult.BadArchive; }
                    catch (IOException) { return StageResult.BadArchive; }
                    if (!File.Exists(Path.Combine(staging, ExeName))) return StageResult.BadArchive;
                    File.WriteAllText(Path.Combine(staging, VerifiedMarker), id);

                    if (Directory.Exists(target))
                    {
                        // leftover without the verified marker: never current, previous or pending (ShouldSkip guarantees id is newer)
                        Directory.Delete(target, true);
                    }
                    Directory.Move(staging, target);
                    staging = null;
                }

                return Locked(() =>
                {
                    bool rec;
                    var st = Load(out rec);
                    if (st.Rejected.Contains(id)) return StageResult.Rejected;
                    var floor = MaxOf(st.Current, running == null ? null : VersionId.Canonical(running));
                    if (floor != null && !Newer(id, floor)) return StageResult.Rejected;
                    if (st.Pending == id) return StageResult.AlreadyPresent;
                    if (st.Pending != null && !Newer(id, st.Pending)) return StageResult.Rejected;
                    st.Pending = id;
                    return Save(st) ? StageResult.Staged : StageResult.Error;
                }, StageResult.Error);
            }
            catch { return StageResult.Error; }
            finally
            {
                if (staging != null) { try { Directory.Delete(staging, true); } catch { } }
            }
        }

        // ---------- launch selection ----------

        public string ResolveLaunchDir(string fallbackDir) => ResolveLaunch(fallbackDir).Dir;

        /// <summary>Promotes a pending release, rolls back one that keeps failing to start, and says which folder to launch.</summary>
        public LaunchPlan ResolveLaunch(string fallbackDir)
        {
            string badToDelete = null;
            LaunchPlan plan = null;
            try
            {
                plan = Locked(() =>
                {
                    CleanStaging();
                    bool dirty;
                    var st = Load(out dirty);
                    var rejected = new HashSet<string>(st.Rejected);

                    // finish scheduling a verified, fully extracted release that never got recorded as pending
                    if (st.Pending == null)
                    {
                        var cand = ValidDirs().Where(d => !rejected.Contains(d) && File.Exists(Path.Combine(DirOf(d), VerifiedMarker)) && (st.Current == null || Newer(d, st.Current)))
                                              .OrderByDescending(d => VersionId.ToVersion(d)).FirstOrDefault();
                        if (cand != null) { st.Pending = cand; dirty = true; }
                    }

                    // forward-only promotion
                    if (st.Pending != null)
                    {
                        if (HasExe(st.Pending) && !rejected.Contains(st.Pending) && (st.Current == null || Newer(st.Pending, st.Current)))
                        {
                            st.Previous = st.Current;
                            st.Current = st.Pending;
                            st.Trial = new TrialState { Version = st.Current, Attempts = 0, StartedUtc = DateTime.UtcNow };
                        }
                        st.Pending = null;
                        dirty = true;
                    }

                    // a current folder that is gone falls back to the previous one, then to the best valid folder
                    if (st.Current == null || !HasExe(st.Current) || rejected.Contains(st.Current))
                    {
                        var alt = HasExe(st.Previous) && !rejected.Contains(st.Previous) ? st.Previous
                                  : ValidDirs().Where(d => !rejected.Contains(d)).OrderByDescending(d => VersionId.ToVersion(d)).FirstOrDefault();
                        st.Current = alt;
                        st.Trial = null;
                        dirty = true;
                    }

                    if (st.Trial != null && st.Current != null && st.Trial.Version == st.Current)
                    {
                        st.Trial.Attempts++;
                        if (st.Trial.Attempts > MaxLaunchAttempts)
                        {
                            var bad = st.Current;
                            if (!rejected.Contains(bad)) st.Rejected.Add(bad);
                            rejected.Add(bad);
                            var prev = HasExe(st.Previous) && !rejected.Contains(st.Previous) ? st.Previous
                                       : ValidDirs().Where(d => !rejected.Contains(d)).OrderByDescending(d => VersionId.ToVersion(d)).FirstOrDefault();
                            st.Current = prev;
                            st.Previous = null;
                            st.Pending = null;
                            st.Trial = null;
                            badToDelete = bad;
                        }
                        else
                        {
                            st.Trial.Token = Guid.NewGuid().ToString("N");
                            st.Trial.StartedUtc = DateTime.UtcNow;
                        }
                        dirty = true;
                    }
                    else if (st.Trial != null)
                    {
                        st.Trial = null;
                        dirty = true;
                    }

                    if (dirty) Save(st);

                    return new LaunchPlan
                    {
                        Dir = st.Current != null ? DirOf(st.Current) : fallbackDir,
                        Version = st.Current,
                        Token = st.Trial != null && st.Trial.Version == st.Current ? st.Trial.Token : null
                    };
                }, new LaunchPlan { Dir = fallbackDir });
            }
            catch { plan = new LaunchPlan { Dir = fallbackDir }; }

            if (badToDelete != null && IsValidDirName(badToDelete))
            {
                try { Directory.Delete(DirOf(badToDelete), true); } catch { }
            }
            return plan;
        }

        /// <summary>Ends the trial for the running version, but only when the version and the launch token both match.</summary>
        public bool MarkLaunchOk(string version, string token)
        {
            var id = Normalise(version);
            if (id == null || string.IsNullOrEmpty(token)) return false;
            return Locked(() =>
            {
                bool rec;
                var st = Load(out rec);
                if (st.Trial == null || st.Trial.Version != id || st.Trial.Token != token) return false;
                st.Trial = null;
                if (!Save(st)) return false;
                Prune(st);
                return true;
            }, false);
        }

        void Prune(UpdateState st)
        {
            try
            {
                if (!Directory.Exists(_versions)) return;
                foreach (var d in Directory.GetDirectories(_versions))
                {
                    var n = Path.GetFileName(d);
                    if (n.StartsWith(".") || n == st.Current || n == st.Previous || n == st.Pending) continue;
                    if (!IsValidDirName(n)) continue;
                    try { Directory.Delete(d, true); } catch { }
                }
            }
            catch { }
        }

        void CleanStaging()
        {
            try
            {
                if (!Directory.Exists(_versions)) return;
                foreach (var d in Directory.GetDirectories(_versions, ".staging-*"))
                    if (DateTime.UtcNow - Directory.GetCreationTimeUtc(d) > TimeSpan.FromHours(24)) { try { Directory.Delete(d, true); } catch { } }
            }
            catch { }
        }
    }
}



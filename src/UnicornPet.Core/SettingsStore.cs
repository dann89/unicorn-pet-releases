using System;
using System.Collections.Generic;
using System.IO;

namespace UnicornPet.Core
{
    public class SettingsStore
    {
        static readonly object Gate = new object();
        readonly string _path;
        readonly Func<DateTime> _now;

        public SettingsStore(string path, Func<DateTime> now = null) { _path = path; _now = now ?? (() => DateTime.Now); }

        /// <summary>False until the first successful save; used to tell a first-ever run from a later launch.</summary>
        public bool Exists
        {
            get { try { return File.Exists(_path); } catch { return false; } }
        }

        public Settings Load()
        {
            lock (Gate) return LoadLocked();
        }

        Settings LoadLocked()
        {
            try
            {
                if (!File.Exists(_path)) return new Settings();
                var parsed = MiniJson.Parse(File.ReadAllText(_path)) as Dictionary<string, object>;
                if (parsed == null) return new Settings();
                var o = new Dictionary<string, object>(parsed, StringComparer.OrdinalIgnoreCase);
                var s = new Settings();
                s.CheekyMode = MiniJson.GetBool(o, "CheekyMode") ?? s.CheekyMode;
                s.ScheduledEnabled = MiniJson.GetBool(o, "ScheduledEnabled") ?? s.ScheduledEnabled;
#if !WORKSAFE
                s.StartWithWindows = MiniJson.GetBool(o, "StartWithWindows") ?? s.StartWithWindows;
#endif
                s.UseWindowsName = MiniJson.GetBool(o, "UseWindowsName") ?? s.UseWindowsName;
                s.FetchMessages = MiniJson.GetBool(o, "FetchMessages") ?? s.FetchMessages;
#if !WORKSAFE
                s.UpdateUrl = MiniJson.GetString(o, "UpdateUrl") ?? s.UpdateUrl;
#endif
                s.CustomName = MiniJson.GetString(o, "CustomName") ?? s.CustomName;
                s.RemoteMessagesVersion = MiniJson.GetString(o, "RemoteMessagesVersion") ?? s.RemoteMessagesVersion;
                s.MessageSource = MessageSources.Parse(MiniJson.GetString(o, "MessageSource"));      // missing or unknown: BuiltIn
                var messageFile = MiniJson.GetString(o, "MessageFile");
                if (CustomSourceRules.IsAllowedFile(messageFile)) s.MessageFile = messageFile;
                var messageUrl = MiniJson.GetString(o, "MessageUrl");
                if (CustomSourceRules.IsAllowedUrl(messageUrl)) s.MessageUrl = messageUrl.Trim();
                var cachedUrl = MiniJson.GetString(o, "MessageUrlCached");
                if (CustomSourceRules.IsAllowedUrl(cachedUrl)) s.MessageUrlCached = cachedUrl.Trim();
                s.SectionsOff = ReadSectionIds(o, "SectionsOff");
                s.IdlePoop = MiniJson.GetBool(o, "IdlePoop") ?? s.IdlePoop;
                s.RandomZoomies = MiniJson.GetBool(o, "RandomZoomies") ?? s.RandomZoomies;
                s.GameHighScore = MiniJson.GetInt(o, "GameHighScore", 0, MiniGame.HighScore.Max) ?? 0;
                s.PetSize = PetSizes.Parse(MiniJson.GetString(o, "PetSize"));            // missing or unknown: Large
                var petName = MiniJson.GetString(o, "PetName");
                if (petName != null) s.PetName = PetNameText.Clean(petName);            // missing: Fifi; saved blank or breaking the name rules: unnamed
                s.Sparkles = MiniJson.GetBool(o, "Sparkles") ?? s.Sparkles;
                object v;
                if (o.TryGetValue("QuietUntil", out v)) s.QuietUntil = MiniJson.ToDate(v);
#if !WORKSAFE
                if (o.TryGetValue("LastUpdateCheck", out v)) s.LastUpdateCheck = MiniJson.ToDate(v);
#endif
                if (o.TryGetValue("LastMessageCheck", out v)) s.LastMessageCheck = MiniJson.ToDate(v);
                if (o.TryGetValue("LastCustomFetch", out v)) s.LastCustomFetch = MiniJson.ToDate(v);
                if (s.QuietUntil.HasValue && s.QuietUntil.Value <= _now()) s.QuietUntil = null;
                return s;
            }
            catch
            {
                return new Settings();
            }
        }

        public const int MaxSectionsOff = 32;
        static readonly System.Text.RegularExpressions.Regex SectionIdRx = new System.Text.RegularExpressions.Regex(@"^[A-Za-z0-9_-]{1,40}\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>The text entries of a JSON list that are valid section ids, without repeats, at most 32. Anything else gives an empty list.</summary>
        static IReadOnlyList<string> ReadSectionIds(Dictionary<string, object> o, string key)
        {
            var ids = new List<string>();
            object v;
            if (!o.TryGetValue(key, out v) || !(v is List<object>)) return ids;
            foreach (var x in (List<object>)v)
            {
                var id = x as string;
                if (id == null || !SectionIdRx.IsMatch(id) || ids.Contains(id)) continue;
                ids.Add(id);
                if (ids.Count == MaxSectionsOff) break;
            }
            return ids;
        }

        /// <summary>Writes the settings through a temporary file in the same folder and replaces the old file in one step. Returns false on any failure.</summary>
        public bool Save(Settings s)
        {
            lock (Gate) return SaveLocked(s);
        }

        bool SaveLocked(Settings s)
        {
            string tmp = null;
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                tmp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(tmp, MiniJson.Serialize(new List<KeyValuePair<string, object>>
                {
                    new KeyValuePair<string, object>("CheekyMode", s.CheekyMode),
                    new KeyValuePair<string, object>("ScheduledEnabled", s.ScheduledEnabled),
#if !WORKSAFE
                    new KeyValuePair<string, object>("StartWithWindows", s.StartWithWindows),
#endif
                    new KeyValuePair<string, object>("QuietUntil", s.QuietUntil),
#if !WORKSAFE
                    new KeyValuePair<string, object>("UpdateUrl", s.UpdateUrl ?? ""),
#endif
#if !WORKSAFE
                    new KeyValuePair<string, object>("LastUpdateCheck", s.LastUpdateCheck),
#endif
                    new KeyValuePair<string, object>("CustomName", s.CustomName ?? ""),
                    new KeyValuePair<string, object>("UseWindowsName", s.UseWindowsName),
                    new KeyValuePair<string, object>("FetchMessages", s.FetchMessages),
                    new KeyValuePair<string, object>("LastMessageCheck", s.LastMessageCheck),
                    new KeyValuePair<string, object>("RemoteMessagesVersion", s.RemoteMessagesVersion ?? ""),
                    new KeyValuePair<string, object>("MessageSource", MessageSources.ToText(s.MessageSource)),
                    new KeyValuePair<string, object>("MessageFile", s.MessageFile ?? ""),
                    new KeyValuePair<string, object>("MessageUrl", s.MessageUrl ?? ""),
                    new KeyValuePair<string, object>("MessageUrlCached", s.MessageUrlCached ?? ""),
                    new KeyValuePair<string, object>("LastCustomFetch", s.LastCustomFetch),
                    new KeyValuePair<string, object>("SectionsOff", s.SectionsOff ?? new string[0]),
                    new KeyValuePair<string, object>("IdlePoop", s.IdlePoop),
                    new KeyValuePair<string, object>("RandomZoomies", s.RandomZoomies),
                    // the pet options go before GameHighScore, which stays the last key (as the 2.0.0 first-run file is checked)
                    new KeyValuePair<string, object>("PetSize", PetSizes.ToText(s.PetSize)),
                    new KeyValuePair<string, object>("PetName", s.PetName ?? ""),
                    new KeyValuePair<string, object>("Sparkles", s.Sparkles),
                    new KeyValuePair<string, object>("GameHighScore", s.GameHighScore),
                }));
                if (File.Exists(_path)) File.Replace(tmp, _path, null);
                else File.Move(tmp, _path);
                tmp = null;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (tmp != null) { try { File.Delete(tmp); } catch { } }
            }
        }
    }
}

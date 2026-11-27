using System;
using System.IO;
using System.Text;

namespace UnicornPet.Core
{
    /// <summary>Writes a file through a temporary file in the same folder and replaces the old file in one step.</summary>
    public static class AtomicFile
    {
        /// <summary>
        /// Removes leftover temporary files from an interrupted save, but only files whose names are exactly
        /// "&lt;one of the given names&gt;.&lt;32 lowercase hex characters&gt;.tmp" (the names this class creates). Never throws.
        /// </summary>
        public static void CleanStaleTemp(string dir, params string[] baseNames)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir) || baseNames == null || baseNames.Length == 0) return;
                var rx = new System.Text.RegularExpressions.Regex("^(?:" + string.Join("|", Array.ConvertAll(baseNames, System.Text.RegularExpressions.Regex.Escape)) + @")\.[0-9a-f]{32}\.tmp\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
                foreach (var f in Directory.GetFiles(dir, "*.tmp"))
                {
                    if (!rx.IsMatch(Path.GetFileName(f))) continue;
                    try { File.Delete(f); } catch { }
                }
            }
            catch { }
        }

        public static bool WriteText(string path, string text) => WriteBytes(path, new UTF8Encoding(false).GetBytes(text ?? ""));

        /// <summary>Returns false (and leaves any existing file untouched, with no temporary file left behind) on any failure.</summary>
        public static bool WriteBytes(string path, byte[] bytes)
        {
            string tmp = null;
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(tmp, bytes);
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
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

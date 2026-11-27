using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace UnicornPet.Core.Updater
{
    /// <summary>
    /// Extracts a release zip into an empty directory, refusing anything that could escape, collide or misbehave on Windows:
    /// traversal, absolute and drive paths, duplicate names (case-insensitive), device names, trailing dot or space, stream names,
    /// header sizes that lie, and anything over the byte or entry caps. Files are created with CreateNew, never overwritten.
    /// </summary>
    public static class ZipExtractor
    {
        const int MaxFullPathLength = 240;

        static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        static readonly char[] Invalid = Path.GetInvalidFileNameChars().Concat(new[] { ':', '*', '?', '"', '<', '>', '|' }).Distinct().ToArray();

        static bool BadComponent(string part)
        {
            if (part.Length == 0 || part == "." || part == "..") return true;
            if (part.EndsWith(".") || part.EndsWith(" ")) return true;
            if (part.IndexOfAny(Invalid) >= 0) return true;
            var stem = part.Split('.')[0].TrimEnd(' ');
            return Reserved.Contains(stem);
        }

        public static void Extract(byte[] zip, string dest, long maxUnpacked, int maxEntries)
        {
            try { ExtractCore(zip, dest, maxUnpacked, maxEntries); }
            catch (ArgumentException ex) { throw new InvalidDataException("unusable entry name", ex); } // e.g. .NET Framework rejecting illegal path characters
            catch (NotSupportedException ex) { throw new InvalidDataException("unusable entry name", ex); }
        }

        static void ExtractCore(byte[] zip, string dest, long maxUnpacked, int maxEntries)
        {
            var destFull = Path.GetFullPath(dest).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (var ms = new MemoryStream(zip))
            using (var za = new ZipArchive(ms, ZipArchiveMode.Read))
            {
                if (za.Entries.Count > maxEntries) throw new InvalidDataException("too many entries");
                long declaredTotal = 0, actualTotal = 0;
                foreach (var e in za.Entries)
                {
                    declaredTotal += e.Length;
                    if (declaredTotal > maxUnpacked) throw new InvalidDataException("too large");

                    var name = e.FullName.Replace('\\', '/');
                    bool isDir = name.EndsWith("/");
                    var trimmed = name.TrimEnd('/');
                    if (trimmed.Length == 0 || name.StartsWith("/")) throw new InvalidDataException("bad path");
                    var parts = trimmed.Split('/');
                    if (parts.Any(BadComponent)) throw new InvalidDataException("bad path component");

                    if (!seen.Add(trimmed)) throw new InvalidDataException("duplicate entry");
                    for (int i = 1; i < parts.Length; i++)
                        if (files.Contains(string.Join("/", parts.Take(i)))) throw new InvalidDataException("file used as a directory");

                    var full = Path.GetFullPath(Path.Combine(dest, trimmed.Replace('/', Path.DirectorySeparatorChar)));
                    if (!full.StartsWith(destFull, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("path escape");
                    if (full.Length > MaxFullPathLength) throw new InvalidDataException("path too long");

                    if (isDir)
                    {
                        if (e.Length != 0) throw new InvalidDataException("directory with data");
                        Directory.CreateDirectory(full);
                        continue;
                    }

                    files.Add(trimmed);
                    Directory.CreateDirectory(Path.GetDirectoryName(full));
                    long written = 0;
                    using (var src = e.Open())
                    using (var dst = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        var buf = new byte[81920]; int n;
                        while ((n = src.Read(buf, 0, buf.Length)) > 0)
                        {
                            written += n;
                            actualTotal += n;
                            if (written > e.Length || actualTotal > maxUnpacked) throw new InvalidDataException("size mismatch");
                            dst.Write(buf, 0, n);
                        }
                    }
                    if (written != e.Length) throw new InvalidDataException("length mismatch");
                }
            }
        }
    }
}

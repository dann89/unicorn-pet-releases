using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using UnicornPet.Core.Updater;

namespace UnicornPet.Tests
{
    public static class UpdateTestKit
    {
        public static RSA NewKey() => RSA.Create(2048);

        public static string PublicXml(RSA rsa)
        {
            var p = rsa.ExportParameters(false);
            return "<RSAKeyValue><Modulus>" + Convert.ToBase64String(p.Modulus) + "</Modulus><Exponent>" + Convert.ToBase64String(p.Exponent) + "</Exponent></RSAKeyValue>";
        }

        public static string Sign(RSA rsa, Version v, string url, string sha)
            => Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(v + "|" + url + "|" + sha), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        public static string Sha(byte[] b)
        {
            using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(b)).Replace("-", "").ToLowerInvariant();
        }

        public static byte[] Zip(params (string name, string content)[] entries)
        {
            var ms = new MemoryStream();
            using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
                foreach (var (name, content) in entries)
                {
                    var e = z.CreateEntry(name);
                    using (var w = new StreamWriter(e.Open())) w.Write(content);
                }
            return ms.ToArray();
        }

        public static Manifest Signed(RSA rsa, string version, string url, byte[] zip)
        {
            var v = Version.Parse(version);
            var sha = Sha(zip);
            return new Manifest(v, url, sha, Sign(rsa, v, url, sha));
        }

        public static string TempDir()
        {
            var d = Path.Combine(Path.GetTempPath(), "up-upd-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }
    }
}

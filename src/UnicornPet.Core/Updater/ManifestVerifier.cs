using System;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace UnicornPet.Core.Updater
{
    /// <summary>RSA-SHA256 (PKCS#1 v1.5) check of a release manifest. Any failure means "not valid".</summary>
    public class ManifestVerifier
    {
        readonly RSAParameters? _key;

        public ManifestVerifier(string publicKeyXml)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(publicKeyXml)) return;
                var x = XElement.Parse(publicKeyXml);
                var mod = Convert.FromBase64String((string)x.Element("Modulus"));
                var exp = Convert.FromBase64String((string)x.Element("Exponent"));
                if (mod.Length < 256) return; // refuse keys under 2048 bits
                _key = new RSAParameters { Modulus = mod, Exponent = exp };
            }
            catch { _key = null; }
        }

#if !WORKSAFE
        public bool Verify(Manifest m)
        {
            if (m == null) return false;
            return VerifyPayload(m.SignedPayload, m.Signature);
        }
#endif

        /// <summary>Checks a base64 RSA-SHA256 (PKCS#1 v1.5) signature over exactly this payload text. Any failure means "not valid".</summary>
        public bool VerifyPayload(string payload, string signatureBase64)
        {
            if (payload == null || _key == null || string.IsNullOrEmpty(signatureBase64)) return false;
            try
            {
                var sig = Convert.FromBase64String(signatureBase64);
                using (var rsa = RSA.Create())
                {
                    rsa.ImportParameters(_key.Value);
                    return rsa.VerifyData(Encoding.UTF8.GetBytes(payload), sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
            }
            catch { return false; }
        }
    }
}
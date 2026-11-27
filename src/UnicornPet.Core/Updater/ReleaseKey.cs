namespace UnicornPet.Core.Updater
{
    /// <summary>The public half of the release signing key, used to verify signed manifests. Empty disables verification.</summary>
    public static class ReleaseKey
    {
        /// <summary>RSA public key (XML, modulus and exponent only).</summary>
        public const string PublicKeyXml = "<RSAKeyValue><Modulus>tD4UiMX2Bu/FChM+RpMJHs64nv8jGZ1pIoLPHB4Qb4v4vtxja+LIrzCT0X1y6/ZGi7FZtzExQTLhPF02s/Z+v663gCICe/m/tYue5XwbLaZGzSAzp7x6XYuncNw5z/io4/9gJHyeEBLfQS0onaOguMb16QY6kXqp8mMcnDmmitXSsEOCUj8uc93meDuMHiDRbWFZ0v3Uc7gwl8prZEnv3W8xKNfDypMDmPulG2J8eGCAoX6I3IZMGI73jQhbKhE2FpRm0NsibXWwcG6fui3Krm5yhKD7CqoAQ8S53eqFg6rA9W4ZDhufdYCmdLnSEH8dih84ID/MUD5uiBeVpoQi/4VXgJxVvlUKTcd9C8pQQJ1Mbtmm2z3uI78n9eSAH79ZCFH5Y0nw8itVvsGLnEvjtEFMHImBWxqX7XinwbEvLN5/4/AOvxdLTm5zg48mHyRzLQUXZh4G/uiPSEkEibwxXCJY/3ixqLoR5qy73wFAlG9UpZgVj43WAfK544LPAF6d</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
    }
}

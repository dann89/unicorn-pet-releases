using System;
using Microsoft.Win32;

namespace UnicornPet.App
{
    public static class StartupRegistration
    {
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string ValueName = "UnicornPet";

        public static void Set(bool enabled, string exePath, string keyPath = null)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(keyPath ?? RunKey))
                {
                    if (k == null) return;
                    if (enabled) k.SetValue(ValueName, "\"" + exePath + "\"", RegistryValueKind.String);
                    else k.DeleteValue(ValueName, false);
                }
            }
            catch { }
        }

        public static string Get(string keyPath = null)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(keyPath ?? RunKey))
                    return k?.GetValue(ValueName) as string;
            }
            catch { return null; }
        }
    }
}

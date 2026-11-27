using System;
using System.IO;
using System.Windows;

namespace UnicornPet.App
{
    public partial class App : Application
    {
        public static string LocalDir => Environment.GetEnvironmentVariable("UNICORNPET_LOCALDIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UnicornPet");
        static bool LocalDirOverridden => Environment.GetEnvironmentVariable("UNICORNPET_LOCALDIR") != null;
        public static string VersionsDir => Path.Combine(LocalDir, "versions");
        public static string RoamingDir => Environment.GetEnvironmentVariable("UNICORNPET_APPDATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UnicornPet");

        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool AttachConsole(int processId);

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            try { UnicornPet.Core.Updater.UpdateConfig.DefaultManifestUrl = System.Configuration.ConfigurationManager.AppSettings["UpdateManifestUrl"] ?? ""; }
            catch { }
            DispatcherUnhandledException +=(s, a) => { Log(a.Exception); a.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (s, a) => Log(a.ExceptionObject as Exception);

            var sn = Array.IndexOf(e.Args, "--snapshot");
            if (sn >= 0 && sn + 1 < e.Args.Length)
            {
                try { Snapshot.Run(e.Args[sn + 1]); } catch (Exception ex) { Log(ex); }
                Shutdown();
                return;
            }

            var ini = Array.IndexOf(e.Args, "--init-state");
            if (ini >= 0 && ini + 1 < e.Args.Length)
            {
                bool done = false;
                try { done = new UnicornPet.Core.Updater.UpdateStager(VersionsDir).InitState(e.Args[ini + 1]); } catch (Exception ex) { Log(ex); }
                Shutdown(done ? 0 : 1);
                return;
            }

            var vm = Array.IndexOf(e.Args, "--verify-manifest");
            if (vm >= 0 && vm + 1 < e.Args.Length)
            {
                string reason;
                try { reason = UnicornPet.Core.Updater.ManifestCheck.Evaluate(File.ReadAllText(e.Args[vm + 1]), new UnicornPet.Core.Updater.ManifestVerifier(UnicornPet.Core.Updater.UpdateConfig.PublicKeyXml)); }
                catch (Exception ex) { reason = ex.GetType().Name; }
                try { AttachConsole(-1); Console.WriteLine(reason == null ? "OK" : "FAIL: " + reason); } catch { }
                Shutdown(reason == null ? 0 : 1);
                return;
            }

            var su = Array.IndexOf(e.Args, "--selftest-startup");
            if (su >= 0 && su + 1 < e.Args.Length) { RunStartupSelfTest(e.Args[su + 1]); Shutdown(); return; }

            if (!SingleInstance.TryAcquire(@"Local\UnicornPet.SingleInstance"))
            {
                Shutdown();
                return;
            }
            var ss = Array.IndexOf(e.Args, "--selftest-settings");
            if (ss >= 0 && ss + 1 < e.Args.Length) PetWindow.SettingsTestOut = e.Args[ss + 1];
            var m = Array.IndexOf(e.Args, "--selftest-menu");
            if (m >= 0 && m + 1 < e.Args.Length) PetWindow.MenuTestOut = e.Args[m + 1];
            var i = Array.IndexOf(e.Args, "--selftest-bubble");
            if (i >= 0 && i + 1 < e.Args.Length) PetWindow.SelfTestOut = e.Args[i + 1];
            var h = Array.IndexOf(e.Args, "--hold");
            if (h >= 0 && h + 1 < e.Args.Length)
            {
                var v = e.Args[h + 1];
                PetWindow.HoldState = v == "walk" ? UnicornPet.Core.PetState.WalkRight : v == "sleep" ? UnicornPet.Core.PetState.Sleep : UnicornPet.Core.PetState.Idle;
            }
            var stager = new UnicornPet.Core.Updater.UpdateStager(VersionsDir);
            if (IsInstalled && !Array.Exists(e.Args, x => x == "--from-bootstrap") && SameDir(AppDomain.CurrentDomain.BaseDirectory, LocalDir))
            {
                var plan = stager.ResolveLaunch(AppDomain.CurrentDomain.BaseDirectory);
                var dir = plan.Dir;
                var exe = Path.Combine(dir, "UnicornPet.exe");
                if (!SameDir(dir, AppDomain.CurrentDomain.BaseDirectory) && File.Exists(exe))
                {
                    SingleInstance.Release();
                    try
                    {
                        var childArgs = string.Join(" ", Array.ConvertAll(e.Args, QuoteArg)) + " --from-bootstrap";
                        if (!string.IsNullOrEmpty(plan.Token)) childArgs += " --trial-token " + plan.Token;
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, childArgs) { WorkingDirectory = dir, UseShellExecute = false });
                        Shutdown();
                        return;
                    }
                    catch (Exception ex)
                    {
                        Log(ex);
                        if (!SingleInstance.TryAcquire(@"Local\UnicornPet.SingleInstance")) { Shutdown(); return; }
                    }
                }
            }

            var win = new PetWindow();
            win.StartWithWindowsChanged += v => { if (!LocalDirOverridden) StartupRegistration.Set(v, InstalledExe); };
            win.Loaded += (s, ev) => { if (IsInstalled && !LocalDirOverridden) StartupRegistration.Set(win.Settings.StartWithWindows, InstalledExe); };
            win.Show();
            if (IsInstalled)
            {
                new UpdateService(win, stager, e.Args);
                if (!SameDir(AppDomain.CurrentDomain.BaseDirectory, LocalDir))
                {
                    var tk = Array.IndexOf(e.Args, "--trial-token");
                    var token = tk >= 0 && tk + 1 < e.Args.Length ? e.Args[tk + 1] : null;
                    var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
                    var ok = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                    ok.Tick += (s, ev) => { ok.Stop(); stager.MarkLaunchOk(version, token); };
                    ok.Start();
                }
            }
            var d = Array.IndexOf(e.Args, "--diag");
            if (d >= 0 && d + 1 < e.Args.Length)
            {
                var path = e.Args[d + 1];
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                t.Tick += (s, a) => { t.Stop(); File.WriteAllText(path, NoActivate.Dump(win)); Shutdown(); };
                t.Start();
            }
        }

        static bool SameDir(string a, string b) => string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        static string QuoteArg(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";

        public static string InstalledExe => Path.Combine(LocalDir, "UnicornPet.exe");
        public static bool IsInstalled => AppDomain.CurrentDomain.BaseDirectory.StartsWith(LocalDir, StringComparison.OrdinalIgnoreCase);

        static void RunStartupSelfTest(string outFile)
        {
            const string key = @"Software\UnicornPetSelfTest\Run";
            var lines = new System.Collections.Generic.List<string>();
            try
            {
                StartupRegistration.Set(true, @"C:\x y\UnicornPet.exe", key);
                lines.Add("enabled -> " + StartupRegistration.Get(key));
                StartupRegistration.Set(false, @"C:\x y\UnicornPet.exe", key);
                lines.Add("disabled -> " + (StartupRegistration.Get(key) ?? "<absent>"));
                StartupRegistration.Set(false, @"C:\x y\UnicornPet.exe", key);
                lines.Add("disable again (no value) -> no exception");
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\UnicornPetSelfTest", false);
                lines.Add("test key removed: " + (Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\UnicornPetSelfTest") == null));
            }
            catch (Exception ex) { lines.Add("EXCEPTION " + ex); }
            File.WriteAllLines(outFile, lines);
        }

        public static void Log(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(LocalDir);
                File.AppendAllText(Path.Combine(LocalDir, "error.log"), DateTime.Now.ToString("s") + " " + ex + Environment.NewLine);
            }
            catch { }
        }
    }
}








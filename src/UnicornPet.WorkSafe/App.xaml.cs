using System;
using System.IO;
using System.Windows;

namespace UnicornPet.App
{
    /// <summary>Work-safe entry point: starts the pet and nothing else. No command-line options; the only files are settings.json and the message files (messages.remote.json, messages.remote.json.version, messages.custom.json).</summary>
    public partial class App : Application
    {
        /// <summary>Where settings.json, messages.remote.json (+ .version) and messages.custom.json live. Debug builds only honour a scratch-folder override; Release never reads one.</summary>
        public static string RoamingDir
        {
            get
            {
#if DEBUG
                var overrideDir = Environment.GetEnvironmentVariable("UNICORNPET_APPDATA");
                if (!string.IsNullOrEmpty(overrideDir)) return overrideDir;
#endif
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UnicornPet");
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += (s, a) => { a.Handled = true; }; // nothing is written about errors
            if (!SingleInstance.TryAcquire(@"Local\UnicornPet.SingleInstance"))
            {
                Shutdown();
                return;
            }
            new PetWindow().Show();
        }
    }
}

using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Threading;
using UnicornPet.Core;
using UnicornPet.Core.Updater;

namespace UnicornPet.App
{
    /// <summary>Silent update checks. Never prompts; the only UI is a speech bubble when the user asks via the menu.</summary>
    public class UpdateService
    {
        const long MaxDownload = 100L * 1024 * 1024;
        readonly PetWindow _win;
        readonly UpdateChecker _checker;
        readonly string _publicKey;
        string _urlOverride;
        readonly DispatcherTimer _first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        readonly DispatcherTimer _periodic = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };

        public UpdateService(PetWindow win, UpdateStager stager, string[] args)
        {
            _win = win;
            _publicKey = UpdateConfig.PublicKeyXml;
            bool now = false;
#if DEBUG
            var k = Environment.GetEnvironmentVariable("UNICORNPET_UPDATE_PUBKEY");
            if (!string.IsNullOrEmpty(k)) _publicKey = k;
            int u = Array.IndexOf(args, "--update-url");
            if (u >= 0 && u + 1 < args.Length) _urlOverride = args[u + 1];
            now = Array.IndexOf(args, "--update-now") >= 0;
#endif
            var fetcher = new HttpFetcher();
            _checker = new UpdateChecker((url, max, deadline) => fetcher.FetchAsync(url, max, deadline), new ManifestVerifier(_publicKey), stager,
                Assembly.GetExecutingAssembly().GetName().Version);

            _first.Tick += async (s, e) => { _first.Stop(); await RunAsync(false, false); };
            _periodic.Tick += async (s, e) => await RunAsync(false, false);
            _win.CheckForUpdatesRequested += async () => await RunAsync(true, true);
            if (now) _first.Interval = TimeSpan.FromSeconds(2);
            _first.Start();
            _periodic.Start();
        }

        bool Enabled => !string.IsNullOrWhiteSpace(_publicKey);

        string Url => UpdateConfig.ChooseManifestUrl(_urlOverride ?? _win.Settings.UpdateUrl);

        async Task RunAsync(bool force, bool notify)
        {
            try
            {
                if (!Enabled)
                {
                    if (notify) _win.ShowSpeech(new Speech("Updates aren't set up yet.", SpeechKind.Manual));
                    return;
                }
                var url = Url;
                if (url == null)
                {
                    if (notify) _win.ShowSpeech(new Speech("Updates aren't set up yet.", SpeechKind.Manual));
                    return;
                }
                if (_checker.IsBusy)
                {
                    if (notify) _win.ShowSpeech(new Speech("Already checking.", SpeechKind.Manual));
                    return;
                }
                bool staged = await _checker.CheckAsync(url, _win.Settings, force || _urlOverride != null, () => _win.Store.Save(_win.Settings));
                _win.Store.Save(_win.Settings);
                if (notify)
                    _win.ShowSpeech(new Speech(staged ? "Found a new version. I'll switch to it next time I start." : "I'm already up to date.", SpeechKind.Manual));
            }
            catch (Exception ex) { App.Log(ex); }
        }
    }
}



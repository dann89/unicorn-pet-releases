#if WORKSAFE
using System;
using System.Windows.Threading;
using UnicornPet.Core;
using UnicornPet.Core.Updater;

namespace UnicornPet.App
{
    // The person's own messages (Settings, Messages): a file or an https address laid over the built-in chain, plus the sections.
    // Nothing user-chosen is touched on this (the UI) thread: the loader runs every read and fetch off it, raced against a time limit.
    public partial class PetWindow
    {
        HttpFetcher _fetcher;                 // one instance for the signed list and the custom web list
        CustomMessageLoader _custom;
        MessageBank _baseBank;                // messages.json beside the exe, then the signed list, then the embedded lines
        CustomMessages _customNow;            // the last good custom list for the current source, or null
        CustomLoadResult _customLast;         // the last load attempt for the current source, or null
        int _customGen;                       // bumped when the source changes, so a late result for the old source is dropped
        bool _customRetry;                    // a load was asked for while another was in flight
        DispatcherTimer _customRetryTimer;    // asks again every few seconds while a stuck read keeps the loader busy
        MessageSourceKind _appliedSource;
        string _appliedFile = "", _appliedUrl = "";
        DispatcherTimer _customTimer;

        HttpFetcher Fetcher() { return _fetcher ?? (_fetcher = new HttpFetcher()); }

        /// <summary>Called once at start with the base list. Never waits: a file, or a fresh web list, is swapped in when it arrives.</summary>
        MessageBank StartCustomMessages(MessageBank baseBank)
        {
            _baseBank = baseBank;
            try
            {
                _custom = new CustomMessageLoader((url, max, deadline) => Fetcher().FetchAsync(url, max, deadline), CustomMessageLoader.ReadCapped,
                    System.IO.Path.Combine(App.RoamingDir, "messages.custom.json"));
                TakeMessagesSnapshot();
                UseCachedWebList();
                if (Settings.MessageSource == MessageSourceKind.File && CustomSourceRules.IsAllowedFile(Settings.MessageFile))
                    Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => { var ignored = ReloadCustomAsync(false); }));
                // first web check 2 minutes after start, then hourly; Due() keeps it to one successful fetch a day
                _customTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
                _customTimer.Tick += (s, e) =>
                {
                    _customTimer.Interval = TimeSpan.FromHours(1);
                    if (Settings.MessageSource == MessageSourceKind.Web) { var ignored = ReloadCustomAsync(false); }
                };
                _customTimer.Start();
            }
            catch { }
            return ComposeMessages();
        }

        void TakeMessagesSnapshot()
        {
            _appliedSource = Settings.MessageSource;
            _appliedFile = Settings.MessageFile ?? "";
            _appliedUrl = Settings.MessageUrl ?? "";
        }

        /// <summary>Source Web: the cached copy (a small local read of our own file), but only when it belongs to the current address.</summary>
        void UseCachedWebList()
        {
            if (_custom == null || Settings.MessageSource != MessageSourceKind.Web || !CustomSourceRules.IsAllowedUrl(Settings.MessageUrl)
                || !string.Equals(Settings.MessageUrlCached, Settings.MessageUrl, StringComparison.Ordinal)) return;
            var cached = _custom.ReadCache();
            if (cached.Outcome == CustomLoadOutcome.Missing) return;
            _customLast = cached;
            if (cached.Outcome == CustomLoadOutcome.Loaded) _customNow = cached.Messages;
        }

        MessageBank ComposeMessages()
        {
            return MessageComposer.Compose(_baseBank, BuiltInSections.All, _customNow, Settings.SectionsOff);
        }

        async System.Threading.Tasks.Task ReloadCustomAsync(bool force)
        {
            try
            {
                if (_custom == null) return;
                if (_custom.IsBusy) { _customRetry = true; return; }   // tried again when the load in flight finishes
                int gen = _customGen;
                CustomLoadResult outcome;
                if (Settings.MessageSource == MessageSourceKind.File)
                {
                    outcome = await _custom.LoadFileAsync(Settings.MessageFile);   // read at start, on Save and on Reload now; never polled
                }
                else if (Settings.MessageSource == MessageSourceKind.Web)
                {
                    var url = Settings.MessageUrl;
                    var now = DateTime.UtcNow;
                    var last = Settings.LastCustomFetch;
                    if (!CustomMessageLoader.Due(now, ref last, force))
                    {
                        if (last != Settings.LastCustomFetch) { Settings.LastCustomFetch = last; _settingsStore.Save(Settings); }
                        return;
                    }
                    // the attempt time is saved BEFORE the request; if it cannot be saved, nothing is fetched. The cache loses its label
                    // first when it belongs to another address, so it can never hold one address's list under another's name.
                    Settings.LastCustomFetch = now;
                    if (!string.Equals(Settings.MessageUrlCached, url, StringComparison.Ordinal)) Settings.MessageUrlCached = "";
                    if (!_settingsStore.Save(Settings)) return;
                    outcome = await _custom.FetchWebAsync(url);
                    if (gen != _customGen)
                    {
                        // the address changed meanwhile: forget this result. The cache now holds this address's list, so any other label is wrong.
                        if (outcome.Outcome == CustomLoadOutcome.Loaded && !string.Equals(Settings.MessageUrlCached, url, StringComparison.Ordinal))
                        {
                            Settings.MessageUrlCached = "";
                            _settingsStore.Save(Settings);
                        }
                        return;
                    }
                    if (outcome.Outcome == CustomLoadOutcome.Loaded)
                    {
                        Settings.MessageUrlCached = url;
                        _settingsStore.Save(Settings);
                    }
                    else if (outcome.Outcome == CustomLoadOutcome.TimedOut || outcome.Outcome == CustomLoadOutcome.NetworkFailed)
                    {
                        Settings.LastCustomFetch = CustomMessageLoader.RetryStamp(now);   // no list came: try again in an hour
                        _settingsStore.Save(Settings);
                    }
                }
                else return;
                if (gen == _customGen) ApplyCustom(outcome);
            }
            catch { }
            finally
            {
                if (_customRetry && _custom != null)
                {
                    if (!_custom.IsBusy)
                    {
                        _customRetry = false;
                        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { var ignored = ReloadCustomAsync(true); }));
                    }
                    else ArmCustomRetry();   // a stuck file read: ask again shortly (it re-arms itself until the read is released)
                }
            }
        }

        void ArmCustomRetry()
        {
            if (_customRetryTimer == null)
            {
                _customRetryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                _customRetryTimer.Tick += (s, e) => { _customRetryTimer.Stop(); var ignored = ReloadCustomAsync(true); };
            }
            if (!_customRetryTimer.IsEnabled) _customRetryTimer.Start();
        }

        void ApplyCustom(CustomLoadResult outcome)
        {
            _customLast = outcome;
            if (outcome.Outcome == CustomLoadOutcome.Loaded) _customNow = outcome.Messages;
            Recompose();
        }

        /// <summary>Builds the list again (source and section ticks) and swaps it into the running scheduler.</summary>
        void Recompose()
        {
            if (_scheduler == null || _baseBank == null) return;
            var bank = ComposeMessages();
            _fileName = bank.Name;
            _scheduler.ReplaceBank(bank);
            RefreshName();
        }

        /// <summary>After Settings was saved: a new source, path or address starts again from the base at once and loads; otherwise the ticks apply.</summary>
        void MessagesSettingsSaved()
        {
            try
            {
                bool changed = Settings.MessageSource != _appliedSource
                    || !string.Equals(Settings.MessageFile ?? "", _appliedFile, StringComparison.Ordinal)
                    || !string.Equals(Settings.MessageUrl ?? "", _appliedUrl, StringComparison.Ordinal);
                if (!changed) { Recompose(); return; }
                _customGen++;
                _customNow = null;
                _customLast = null;
                TakeMessagesSnapshot();
                UseCachedWebList();
                Recompose();
                if (Settings.MessageSource != MessageSourceKind.BuiltIn) { var ignored = ReloadCustomAsync(true); }
            }
            catch { }
        }

        MessagesUi BuildMessagesUi()
        {
            return new MessagesUi
            {
                Sections = () => MessageComposer.MergeSections(BuiltInSections.All, _customNow),
                Status = MessagesStatus,
                Reload = () => ReloadCustomAsync(true),
            };
        }

        string MessagesStatus()
        {
            if (Settings.MessageSource == MessageSourceKind.BuiltIn) return "";
            if (_custom != null && _custom.IsBusy) return "Already loading.";
            if (_customLast == null) return "Not loaded yet.";
            var line = CustomStatus.Describe(_customLast.Outcome, _customLast.Problem, _customNow != null);
            if (_customNow != null) line += " " + _customNow.LineCount + " lines.";
            return line;
        }
    }
}
#endif

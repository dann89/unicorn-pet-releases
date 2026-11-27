using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnicornPet.Core;
using UnicornPet.Core.Updater;
using Xunit;

namespace UnicornPet.Tests
{
    public class FailureGuardTests
    {
        [Fact] public void Trips_After_Five_Consecutive_Failures_And_Never_Runs_Again()
        {
            var g = new FailureGuard(5);
            int runs = 0;
            for (int i = 0; i < 4; i++) { Assert.False(g.Run(() => { runs++; throw new InvalidOperationException(); })); Assert.False(g.Tripped); }
            Assert.False(g.Run(() => { runs++; throw new InvalidOperationException(); }));
            Assert.True(g.Tripped);
            Assert.Equal(5, runs);
            Assert.False(g.Run(() => runs++));
            Assert.Equal(5, runs);
        }

        [Fact] public void A_Success_Resets_The_Count()
        {
            var g = new FailureGuard(3);
            Assert.False(g.Run(() => throw new Exception()));
            Assert.False(g.Run(() => throw new Exception()));
            Assert.True(g.Run(() => { }));
            Assert.Equal(0, g.ConsecutiveFailures);
            Assert.False(g.Run(() => throw new Exception()));
            Assert.False(g.Run(() => throw new Exception()));
            Assert.False(g.Tripped);
            Assert.False(g.Run(() => throw new Exception()));
            Assert.True(g.Tripped);
        }

        [Fact] public void Null_Action_Is_Harmless()
        {
            var g = new FailureGuard();
            Assert.False(g.Run(null));
            Assert.False(g.Tripped);
        }

        [Fact] public void Window_Code_Uses_The_Guard_For_The_Frame_Timer()
        {
            var src = File.ReadAllText(Path.Combine(RepoRoot(), "src", "UnicornPet.App", "PetWindow.xaml.cs"));
            Assert.Contains("_frameGuard.Run(OnFrame)", src);
            Assert.Contains("StopAnimation()", src);
            Assert.Contains("CleanStaleTemp(", src);
        }

        static string RepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "UnicornPet.sln"))) dir = Path.GetDirectoryName(dir);
            return dir;
        }
    }

    public class LoadWithRemoteTests : IDisposable
    {
        readonly string _dir = UpdateTestKit.TempDir();
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
        string Local => Path.Combine(_dir, "messages.json");
        string Remote => Path.Combine(_dir, "messages.remote.json");

        static string List(string tag, string name = null) =>
            "{" + (name != null ? "\"name\":\"" + name + "\"," : "") +
            "\"scheduled\":[{\"id\":\"s\",\"time\":\"08:00\",\"pool\":[\"Morning " + tag + ".\"]}],\"random\":[\"Random " + tag + ".\"],\"cheeky\":[\"Cheeky " + tag + ".\"],\"welcome\":[\"Welcome " + tag + ".\"],\"hello\":[\"Hello " + tag + ".\"]}";

        [Fact] public void A_Name_In_The_Downloaded_List_Is_Dropped()
        {
            File.WriteAllText(Remote, List("REMOTE", "Someone"));
            var b = MessageBank.LoadWithRemote(Local, Remote);
            Assert.Equal("Random REMOTE.", b.Random[0]);
            Assert.Equal("", b.Name);
        }

        [Fact] public void A_Name_In_The_Local_Override_Is_Kept()
        {
            File.WriteAllText(Local, List("LOCAL", "Sam"));
            File.WriteAllText(Remote, List("REMOTE", "Someone"));
            var b = MessageBank.LoadWithRemote(Local, Remote);
            Assert.Equal("Random LOCAL.", b.Random[0]);
            Assert.Equal("Sam", b.Name);
        }

        [Fact] public void Local_Beats_Remote_Beats_Defaults()
        {
            Assert.Same(MessageBank.Default, MessageBank.LoadWithRemote(Local, Remote));
            File.WriteAllText(Remote, List("REMOTE"));
            Assert.Equal("Random REMOTE.", MessageBank.LoadWithRemote(Local, Remote).Random[0]);
            File.WriteAllText(Local, List("LOCAL"));
            Assert.Equal("Random LOCAL.", MessageBank.LoadWithRemote(Local, Remote).Random[0]);
        }

        [Fact] public void A_Broken_Local_File_Falls_Through_To_Remote_Then_Defaults()
        {
            File.WriteAllText(Local, "this is not json");
            File.WriteAllText(Remote, List("REMOTE"));
            Assert.Equal("Random REMOTE.", MessageBank.LoadWithRemote(Local, Remote).Random[0]);
            File.WriteAllText(Remote, "{{{");
            Assert.Same(MessageBank.Default, MessageBank.LoadWithRemote(Local, Remote));
        }

        [Fact] public void Null_Paths_Give_Defaults() => Assert.Same(MessageBank.Default, MessageBank.LoadWithRemote(null, null));

        [Fact] public void Window_Code_Uses_This_Loader()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "UnicornPet.sln"))) dir = Path.GetDirectoryName(dir);
            Assert.Contains("MessageBank.LoadWithRemote(", File.ReadAllText(Path.Combine(dir, "src", "UnicornPet.App", "PetWindow.xaml.cs")));
        }
    }

    public class ManifestQuirkTests : IDisposable
    {
        readonly string _dir = UpdateTestKit.TempDir();
        readonly RSA _rsa = UpdateTestKit.NewKey();
        readonly DateTime _now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        const string ManifestUrl = "https://github.com/x/y/releases/latest/download/messages.manifest.json";
        const string ListUrl = "https://github.com/x/y/releases/download/v2/messages.json";
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        static readonly byte[] List = Encoding.UTF8.GetBytes("{\"scheduled\":[{\"id\":\"s\",\"time\":\"08:00\",\"pool\":[\"Morning.\"]}],\"random\":[\"Random.\"],\"cheeky\":[\"Cheeky.\"],\"welcome\":[\"Welcome.\"],\"hello\":[\"Hello.\"]}");

        string Sig(string version) =>
            Convert.ToBase64String(_rsa.SignData(Encoding.UTF8.GetBytes(MessageUpdater.SigningDomain + "|" + version + "|" + ListUrl + "|" + UpdateTestKit.Sha(List)), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        int _listRequests;

        // every call gets its own folder, so one case can never be rejected (or accepted) because of what an earlier case left behind
        MessageUpdater Updater(string manifestText)
        {
            var dir = Path.Combine(_dir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            _listRequests = 0;
            FetchFunc fetch = (u, m, d) =>
            {
                if (u == ManifestUrl) return Task.FromResult(Encoding.UTF8.GetBytes(manifestText));
                _listRequests++;
                return Task.FromResult(List);
            };
            _lastDir = dir;
            return new MessageUpdater(fetch, new ManifestVerifier(UpdateTestKit.PublicXml(_rsa)), Path.Combine(dir, "messages.remote.json"), () => _now);
        }

        string _lastDir;

        string Json(string version, string signatureFor) =>
            "{\"version\":\"" + version + "\",\"url\":\"" + ListUrl + "\",\"sha256\":\"" + UpdateTestKit.Sha(List) + "\",\"signature\":\"" + Sig(signatureFor) + "\"}";

        [Fact] public async Task Fetching_Turned_Off_Makes_No_Request_At_All()
        {
            int calls = 0;
            FetchFunc fetch = (u, m, d) => { calls++; throw new Exception("must not be called"); };
            var u2 = new MessageUpdater(fetch, new ManifestVerifier(UpdateTestKit.PublicXml(_rsa)), Path.Combine(_dir, "r.json"), () => _now);
            Assert.False(await u2.CheckAsync(ManifestUrl, new Settings { FetchMessages = false }, () => true));
            Assert.Equal(0, calls);
        }

        [Fact] public async Task First_Update_With_No_Stored_Version_Anywhere_Is_Applied()
        {
            var s = new Settings { FetchMessages = true };
            Assert.Equal("", s.RemoteMessagesVersion);
            Assert.True(await Updater(Json("1", "1")).CheckAsync(ManifestUrl, s, () => true));
            Assert.Equal("1", s.RemoteMessagesVersion);
            Assert.True(File.Exists(Path.Combine(_lastDir, "messages.remote.json")));
        }

        [Fact] public async Task A_Manifest_With_A_Byte_Order_Mark_Is_Handled()
        {
            var s = new Settings { FetchMessages = true };
            Assert.True(await Updater("﻿" + Json("2", "2")).CheckAsync(ManifestUrl, s, () => true));
        }

        [Fact] public async Task Duplicate_Keys_Use_The_Last_Value_And_The_Signature_Must_Match_It()
        {
            // the last "version" wins, so the signature has to be for "3"
            var dup = "{\"version\":\"9\",\"version\":\"3\",\"url\":\"" + ListUrl + "\",\"sha256\":\"" + UpdateTestKit.Sha(List) + "\",\"signature\":\"" + Sig("3") + "\"}";
            Assert.True(await Updater(dup).CheckAsync(ManifestUrl, new Settings { FetchMessages = true }, () => true));
            // a signature for the FIRST (ignored) value does not help
            var bad = "{\"version\":\"9\",\"version\":\"3\",\"url\":\"" + ListUrl + "\",\"sha256\":\"" + UpdateTestKit.Sha(List) + "\",\"signature\":\"" + Sig("9") + "\"}";
            Assert.False(await Updater(bad).CheckAsync(ManifestUrl, new Settings { FetchMessages = true }, () => true));
            Assert.Equal(0, _listRequests);                                    // a bad signature means the list is never even requested
            Assert.False(File.Exists(Path.Combine(_lastDir, "messages.remote.json")));
        }

        [Fact] public async Task A_Manifest_With_Extra_Keys_Is_Still_Judged_By_Its_Signed_Fields()
        {
            var extra = Json("2", "2").TrimEnd('}') + ",\"note\":\"hello\"}";
            Assert.True(await Updater(extra).CheckAsync(ManifestUrl, new Settings { FetchMessages = true }, () => true));
        }
    }
}

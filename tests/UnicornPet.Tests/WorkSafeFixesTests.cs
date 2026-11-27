using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnicornPet.Core;
using UnicornPet.Core.Updater;
using Xunit;

namespace UnicornPet.Tests
{
    // ---------- A2: a read that ignores cancellation must still be abandoned at the deadline ----------
    public class StalledReadTests : IDisposable
    {
        sealed class Handler : HttpMessageHandler
        {
            public Func<HttpRequestMessage, Task<HttpResponseMessage>> Fn;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Fn(r);
        }

        /// <summary>Sends a few bytes and then blocks in ReadAsync WITHOUT looking at the cancellation token (as .NET Framework's stream may).</summary>
        sealed class TokenIgnoringStallStream : Stream
        {
            int _sent;
            public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] b, int o, int c) { Thread.Sleep(Timeout.Infinite); return 0; }
            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            {
                if (_sent < 10) { _sent += 10; buffer[offset] = 1; return 10; }
                await Task.Delay(Timeout.Infinite);   // deliberately ignores ct
                return 0;
            }
            public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
            public override void SetLength(long v) => throw new NotSupportedException();
            public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        }

        sealed class StreamContent2 : HttpContent
        {
            readonly Stream _s;
            public StreamContent2(Stream s) { _s = s; }
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) => Task.CompletedTask;
            protected override bool TryComputeLength(out long length) { length = 0; return false; }
            protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult(_s);
        }

        readonly string _dir = UpdateTestKit.TempDir();
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        [Fact] public async Task Fetch_BodyReadThatIgnoresTheToken_IsAbandonedAtTheDeadline()
        {
            var h = new Handler { Fn = r => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent2(new TokenIgnoringStallStream()) }) };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await Assert.ThrowsAsync<TimeoutException>(() => new HttpFetcher(h).FetchAsync("https://a.example/x", 100000, TimeSpan.FromMilliseconds(400)));
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(6), "took " + sw.Elapsed);
        }

        [Fact] public async Task Fetch_HeadersThatNeverArrive_AndIgnoreTheToken_AreAbandonedAtTheDeadline()
        {
            var h = new Handler { Fn = async r => { await Task.Delay(Timeout.Infinite); return null; } };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await Assert.ThrowsAsync<TimeoutException>(() => new HttpFetcher(h).FetchAsync("https://a.example/x", 1000, TimeSpan.FromMilliseconds(400)));
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(6), "took " + sw.Elapsed);
        }

        [Fact] public async Task MessageUpdater_WithAStalledBody_ReturnsInTimeAndTheNextCheckCanRun()
        {
            var rsa = UpdateTestKit.NewKey();
            var h = new Handler { Fn = r => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent2(new TokenIgnoringStallStream()) }) };
            var fetcher = new HttpFetcher(h);
            FetchFunc fetch = (url, max, d) => fetcher.FetchAsync(url, max, TimeSpan.FromMilliseconds(400));
            var remote = Path.Combine(_dir, "messages.remote.json");
            var updater = new MessageUpdater(fetch, new ManifestVerifier(UpdateTestKit.PublicXml(rsa)), remote, () => DateTime.UtcNow);
            var s = new Settings { FetchMessages = true, RemoteMessagesVersion = "1" };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Assert.False(await updater.CheckAsync("https://example.com/messages.manifest.json", s, () => true));
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(6), "took " + sw.Elapsed);
            Assert.False(updater.IsBusy);
            sw.Restart();
            Assert.False(await updater.CheckAsync("https://example.com/messages.manifest.json", s, () => true, force: true));
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(6), "second check took " + sw.Elapsed);
            Assert.False(File.Exists(remote));
        }
    }

    // ---------- A6: every redirect hop goes through the same URL rules ----------
    public class RedirectRuleTests
    {
        sealed class Handler : HttpMessageHandler
        {
            public Func<HttpRequestMessage, HttpResponseMessage> Fn; public int Calls;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) { Calls++; return Task.FromResult(Fn(r)); }
        }

        static HttpResponseMessage Redirect(string to)
        {
            var r = new HttpResponseMessage(HttpStatusCode.Found);
            r.Headers.Location = new Uri(to, UriKind.RelativeOrAbsolute);
            return r;
        }

        static HttpResponseMessage Ok(string body) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)) };

        [Theory]
        [InlineData("https://user:secret@evil.example/x")]
        [InlineData("ftp://evil.example/x")]
        [InlineData("file:///c:/windows/win.ini")]
        [InlineData("http://evil.example/x")]
        public async Task Redirect_To_A_Disallowed_Target_Is_Refused(string target)
        {
            var h = new Handler { Fn = r => r.RequestUri.Host == "a.example" ? Redirect(target) : Ok("x") };
            await Assert.ThrowsAnyAsync<Exception>(() => new HttpFetcher(h).FetchAsync("https://a.example/x", 1000, TimeSpan.FromSeconds(5)));
            Assert.Equal(1, h.Calls);
        }

        [Fact] public async Task Redirect_To_An_Overlong_Url_Is_Refused()
        {
            var h = new Handler { Fn = r => r.RequestUri.Host == "a.example" ? Redirect("https://b.example/" + new string('a', 3000)) : Ok("x") };
            await Assert.ThrowsAnyAsync<Exception>(() => new HttpFetcher(h).FetchAsync("https://a.example/x", 1000, TimeSpan.FromSeconds(5)));
            Assert.Equal(1, h.Calls);
        }

        [Fact] public async Task Redirect_Chain_Where_A_Later_Hop_Is_Bad_Stops_There()
        {
            var h = new Handler { Fn = r => r.RequestUri.AbsolutePath == "/1" ? Redirect("https://u:p@c.example/3") : r.RequestUri.AbsolutePath == "/0" ? Redirect("/1") : Ok("x") };
            await Assert.ThrowsAnyAsync<Exception>(() => new HttpFetcher(h).FetchAsync("https://a.example/0", 1000, TimeSpan.FromSeconds(5)));
            Assert.Equal(2, h.Calls);
        }

        [Fact] public async Task Ordinary_Https_Redirect_Still_Works()
        {
            var h = new Handler { Fn = r => r.RequestUri.Host == "github.com" ? Redirect("https://objects.githubusercontent.com/abc?x=1") : Ok("ok") };
            Assert.Equal("ok", Encoding.UTF8.GetString(await new HttpFetcher(h).FetchAsync("https://github.com/a/b", 1000, TimeSpan.FromSeconds(5))));
        }

        [Fact] public void User_Agent_Does_Not_Mention_Updating()
        {
            var src = File.ReadAllText(Path.Combine(RepoRoot(), "src", "UnicornPet.Core", "Updater", "HttpFetcher.cs"));
            Assert.Contains("UnicornPet/1.0", src);
            Assert.DoesNotContain("updater/1", src);
        }

        static string RepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "UnicornPet.sln"))) dir = Path.GetDirectoryName(dir);
            return dir;
        }
    }

    // ---------- A3: settings saves from several threads ----------
    public class SettingsConcurrencyTests : IDisposable
    {
        readonly string _dir = UpdateTestKit.TempDir();
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        [Fact] public void Hammering_Save_From_Many_Threads_Always_Leaves_A_Valid_File_And_The_Last_Write_Wins()
        {
            var path = Path.Combine(_dir, "settings.json");
            var store = new SettingsStore(path);
            var failures = 0;
            var tasks = Enumerable.Range(0, 8).Select(t => Task.Run(() =>
            {
                for (int i = 0; i < 40; i++)
                {
                    if (!store.Save(new Settings { CustomName = "Name" + t + "x" + i, CheekyMode = i % 2 == 0 })) Interlocked.Increment(ref failures);
                    var loaded = store.Load();
                    if (!loaded.CustomName.StartsWith("Name")) Interlocked.Increment(ref failures);
                }
            })).ToArray();
            Task.WaitAll(tasks);
            Assert.Equal(0, failures);
            Assert.True(store.Save(new Settings { CustomName = "Last", CheekyMode = false }));
            var final = store.Load();
            Assert.Equal("Last", final.CustomName);
            Assert.False(final.CheekyMode);
            Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        }

        [Fact] public void Keys_That_Differ_Only_In_Case_Give_Defaults_Not_An_Exception()
        {
            var path = Path.Combine(_dir, "settings.json");
            File.WriteAllText(path, "{\"CheekyMode\":false,\"cheekymode\":false,\"CustomName\":\"X\",\"customname\":\"Y\"}");
            var s = new SettingsStore(path).Load();
            Assert.True(s.CheekyMode);
            Assert.Equal("", s.CustomName);
        }

        [Fact] public void Settings_Default_To_Fetching_Messages_And_Keep_A_Saved_Off()
        {
            Assert.True(new Settings().FetchMessages);
            Assert.True(new SettingsStore(Path.Combine(_dir, "missing.json")).Load().FetchMessages);
            var path = Path.Combine(_dir, "settings.json");
            File.WriteAllText(path, "{\"CheekyMode\":true}");
            Assert.True(new SettingsStore(path).Load().FetchMessages);
            new SettingsStore(path).Save(new Settings { FetchMessages = false });
            Assert.False(new SettingsStore(path).Load().FetchMessages);
        }
    }

    // ---------- A4 and A5: domain separation and a replay floor that survives settings loss ----------
    public class MessageSigningTests : IDisposable
    {
        readonly string _dir = UpdateTestKit.TempDir();
        readonly RSA _rsa = UpdateTestKit.NewKey();
        readonly DateTime _now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        string Remote => Path.Combine(_dir, "messages.remote.json");
        const string ManifestUrl = "https://github.com/x/y/releases/latest/download/messages.manifest.json";
        const string ListUrl = "https://github.com/x/y/releases/download/v2/messages.json";
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        static string GoodList() => "{\"scheduled\":[{\"id\":\"start\",\"time\":\"08:00\",\"pool\":[\"Morning.\"]}],\"random\":[\"Random.\"],\"cheeky\":[\"Cheeky.\"],\"welcome\":[\"Welcome.\"],\"hello\":[\"Hello.\"]}";
        string Sign(string payload) => Convert.ToBase64String(_rsa.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        static string Domain => MessageUpdater.SigningDomain + "|";

        string Manifest(string version, byte[] list, string payloadPrefix)
        {
            var sha = UpdateTestKit.Sha(list);
            return "{\"version\":\"" + version + "\",\"url\":\"" + ListUrl + "\",\"sha256\":\"" + sha + "\",\"signature\":\"" + Sign(payloadPrefix + version + "|" + ListUrl + "|" + sha) + "\"}";
        }

        MessageUpdater Updater(string manifest, byte[] list)
        {
            FetchFunc fetch = (u, m, d) => Task.FromResult(u == ManifestUrl ? Encoding.UTF8.GetBytes(manifest) : list);
            return new MessageUpdater(fetch, new ManifestVerifier(UpdateTestKit.PublicXml(_rsa)), Remote, () => _now);
        }

        [Fact] public void The_Signing_Domain_Is_Fixed() => Assert.Equal("unicornpet-messages-v1", MessageUpdater.SigningDomain);

        [Fact] public async Task A_Message_Manifest_Signed_With_The_Domain_Prefix_Is_Accepted()
        {
            var list = Encoding.UTF8.GetBytes(GoodList());
            var s = new Settings { FetchMessages = true };
            Assert.True(await Updater(Manifest("2", list, Domain), list).CheckAsync(ManifestUrl, s, () => true));
        }

        [Fact] public async Task A_Code_Release_Style_Signature_Does_Not_Verify_As_A_Message_Manifest()
        {
            var list = Encoding.UTF8.GetBytes(GoodList());
            var s = new Settings { FetchMessages = true };
            Assert.False(await Updater(Manifest("2", list, ""), list).CheckAsync(ManifestUrl, s, () => true));
            Assert.False(File.Exists(Remote));
        }

        [Fact] public void A_Message_Signature_Does_Not_Verify_As_A_Code_Release_Manifest()
        {
            var sha = new string('a', 64);
            var url = "https://example.com/UnicornPet-1.2.3.zip";
            var messageSig = Sign(Domain + "1.2.3|" + url + "|" + sha);
            var asRelease = new Manifest(new Version(1, 2, 3), url, sha, messageSig);
            Assert.False(new ManifestVerifier(UpdateTestKit.PublicXml(_rsa)).Verify(asRelease));
            var releaseSig = UpdateTestKit.Sign(_rsa, new Version(1, 2, 3), url, sha);
            Assert.True(new ManifestVerifier(UpdateTestKit.PublicXml(_rsa)).Verify(new Manifest(new Version(1, 2, 3), url, sha, releaseSig)));
        }

        [Fact] public async Task Replay_Floor_Survives_Losing_Settings()
        {
            var list = Encoding.UTF8.GetBytes(GoodList());
            Assert.True(await Updater(Manifest("5", list, Domain), list).CheckAsync(ManifestUrl, new Settings { FetchMessages = true }, () => true));
            Assert.Equal("5", File.ReadAllText(Remote + ".version").Trim());

            // settings.json deleted or corrupt: the version and the interval are forgotten
            Assert.False(await Updater(Manifest("4", list, Domain), list).CheckAsync(ManifestUrl, new Settings { FetchMessages = true }, () => true));
            Assert.False(await Updater(Manifest("5", list, Domain), list).CheckAsync(ManifestUrl, new Settings { FetchMessages = true }, () => true));
            Assert.True(await Updater(Manifest("6", list, Domain), list).CheckAsync(ManifestUrl, new Settings { FetchMessages = true }, () => true));
            Assert.Equal("6", File.ReadAllText(Remote + ".version").Trim());
        }

        [Fact] public async Task The_Higher_Of_Settings_And_Sidecar_Is_The_Floor()
        {
            var list = Encoding.UTF8.GetBytes(GoodList());
            File.WriteAllText(Remote + ".version", "3");
            Assert.False(await Updater(Manifest("5", list, Domain), list).CheckAsync(ManifestUrl, new Settings { FetchMessages = true, RemoteMessagesVersion = "7" }, () => true));
            Assert.False(await Updater(Manifest("3", list, Domain), list).CheckAsync(ManifestUrl, new Settings { FetchMessages = true, RemoteMessagesVersion = "2" }, () => true));
            Assert.True(await Updater(Manifest("4", list, Domain), list).CheckAsync(ManifestUrl, new Settings { FetchMessages = true, RemoteMessagesVersion = "2" }, () => true));
        }

        [Fact] public async Task A_Corrupt_Sidecar_Is_Ignored_Not_Trusted()
        {
            var list = Encoding.UTF8.GetBytes(GoodList());
            File.WriteAllText(Remote + ".version", "not a version 99999999999");
            Assert.True(await Updater(Manifest("2", list, Domain), list).CheckAsync(ManifestUrl, new Settings { FetchMessages = true, RemoteMessagesVersion = "1" }, () => true));
        }
    }

    // ---------- C2: stale temporary files ----------
    public class StaleTempTests : IDisposable
    {
        readonly string _dir = UpdateTestKit.TempDir();
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        [Fact] public void Only_Exact_Temp_Names_We_Create_Are_Removed()
        {
            var g = Guid.NewGuid().ToString("N");
            var mine = new[] { "settings.json." + g + ".tmp", "messages.remote.json." + g + ".tmp", "messages.remote.json.version." + g + ".tmp" };
            var others = new[] { "settings.json", "messages.remote.json", "messages.remote.json.version", "notes.tmp", "settings.json.tmp", "settings.json." + g + ".bak", "settings.json.XYZ.tmp", "other.json." + g + ".tmp", "settings.json." + "ABCDEF0123456789ABCDEF0123456789" + ".tmp" };
            foreach (var f in mine.Concat(others)) File.WriteAllText(Path.Combine(_dir, f), "x");
            AtomicFile.CleanStaleTemp(_dir, "settings.json", "messages.remote.json", "messages.remote.json.version");
            foreach (var f in mine) Assert.False(File.Exists(Path.Combine(_dir, f)), f);
            foreach (var f in others) Assert.True(File.Exists(Path.Combine(_dir, f)), f);
        }

        [Fact] public void Missing_Folder_Is_Fine() => AtomicFile.CleanStaleTemp(Path.Combine(_dir, "nope"), "settings.json");

        [Fact] public void A_Locked_Stale_File_Does_Not_Throw()
        {
            var f = Path.Combine(_dir, "settings.json." + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(f, "x");
            using (new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.None)) AtomicFile.CleanStaleTemp(_dir, "settings.json");
            Assert.True(File.Exists(f));
        }
    }
}

namespace UnicornPet.Tests
{
    public class CommitOrderTests : IDisposable
    {
        readonly string _dir = UpdateTestKit.TempDir();
        readonly RSA _rsa = UpdateTestKit.NewKey();
        readonly DateTime _now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        string Remote => Path.Combine(_dir, "messages.remote.json");
        const string ManifestUrl = "https://github.com/x/y/releases/latest/download/messages.manifest.json";
        const string ListUrl = "https://github.com/x/y/releases/download/v2/messages.json";
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        static string GoodList(string tag) => "{\"scheduled\":[{\"id\":\"s\",\"time\":\"08:00\",\"pool\":[\"Morning " + tag + ".\"]}],\"random\":[\"Random " + tag + ".\"],\"cheeky\":[\"Cheeky.\"],\"welcome\":[\"Welcome.\"],\"hello\":[\"Hello.\"]}";

        MessageUpdater Updater(string version, byte[] list)
        {
            var sha = UpdateTestKit.Sha(list);
            var sig = Convert.ToBase64String(_rsa.SignData(Encoding.UTF8.GetBytes(MessageUpdater.SigningDomain + "|" + version + "|" + ListUrl + "|" + sha), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            var manifest = "{\"version\":\"" + version + "\",\"url\":\"" + ListUrl + "\",\"sha256\":\"" + sha + "\",\"signature\":\"" + sig + "\"}";
            FetchFunc fetch = (u, m, d) => Task.FromResult(u == ManifestUrl ? Encoding.UTF8.GetBytes(manifest) : list);
            return new MessageUpdater(fetch, new ManifestVerifier(UpdateTestKit.PublicXml(_rsa)), Remote, () => _now);
        }

        [Fact] public async Task A_Failed_Floor_Save_Keeps_The_Old_List_And_Returns_False()
        {
            File.WriteAllText(Remote, GoodList("OLD"));
            var s = new Settings { FetchMessages = true, RemoteMessagesVersion = "1" };
            int saves = 0;
            var list = Encoding.UTF8.GetBytes(GoodList("NEW"));
            // the first save (attempt time) works, the second (the new floor) fails
            Assert.False(await Updater("2", list).CheckAsync(ManifestUrl, s, () => ++saves == 1));
            Assert.Equal(GoodList("OLD"), File.ReadAllText(Remote));
            Assert.True(MessageVersion.Compare(File.ReadAllText(Remote + ".version").Trim(), "2") >= 0, "the floor must already be at least the new version");
            Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        }

        [Fact] public async Task A_Sidecar_That_Cannot_Be_Written_Keeps_The_Old_List_And_The_Old_Floor()
        {
            File.WriteAllText(Remote, GoodList("OLD"));
            Directory.CreateDirectory(Remote + ".version"); // a folder where the file should go: the write must fail
            var s = new Settings { FetchMessages = true, RemoteMessagesVersion = "1" };
            Assert.False(await Updater("2", Encoding.UTF8.GetBytes(GoodList("NEW"))).CheckAsync(ManifestUrl, s, () => true));
            Assert.Equal(GoodList("OLD"), File.ReadAllText(Remote));
            Assert.Equal("1", s.RemoteMessagesVersion);
        }

        [Fact] public async Task True_Is_Returned_Only_When_Floor_And_List_Are_Both_Durable()
        {
            var s = new Settings { FetchMessages = true, RemoteMessagesVersion = "1" };
            Assert.True(await Updater("2", Encoding.UTF8.GetBytes(GoodList("NEW"))).CheckAsync(ManifestUrl, s, () => true));
            Assert.Equal("2", s.RemoteMessagesVersion);
            Assert.Equal("2", File.ReadAllText(Remote + ".version").Trim());
            Assert.Equal(GoodList("NEW"), File.ReadAllText(Remote));
        }

        [Fact] public async Task A_Crash_Between_The_Steps_Is_Monotonic()
        {
            // simulate: floor written (sidecar), process ends before the list replace; the next run must not accept an older list
            File.WriteAllText(Remote, GoodList("OLD"));
            File.WriteAllText(Remote + ".version", "5");
            var s = new Settings { FetchMessages = true };
            Assert.False(await Updater("4", Encoding.UTF8.GetBytes(GoodList("OLDER"))).CheckAsync(ManifestUrl, s, () => true));
            Assert.Equal(GoodList("OLD"), File.ReadAllText(Remote));
        }
    }
}

namespace UnicornPet.Tests
{
    public class FetcherOwnershipTests
    {
        sealed class Handler : HttpMessageHandler
        {
            public Func<HttpRequestMessage, Task<HttpResponseMessage>> Fn; public int Calls;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) { Interlocked.Increment(ref Calls); return Fn(r); }
        }

        sealed class TrackedContent : HttpContent
        {
            public volatile bool Disposed;
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) => Task.CompletedTask;
            protected override bool TryComputeLength(out long length) { length = 3; return true; }
            protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(new byte[] { 1, 2, 3 }));
            protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        }

        sealed class CountingEndlessStream : Stream
        {
            public long Requested;
            public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] b, int o, int c) { Requested += c; return c; }
            public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
            public override void SetLength(long v) => throw new NotSupportedException();
            public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        }

        sealed class NoLength : HttpContent
        {
            readonly Stream _s; public NoLength(Stream s) { _s = s; }
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) => Task.CompletedTask;
            protected override bool TryComputeLength(out long length) { length = 0; return false; }
            protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult(_s);
        }

        [Fact] public async Task A_Response_That_Arrives_After_The_Deadline_Is_Disposed_Anyway()
        {
            var content = new TrackedContent();
            var h = new Handler { Fn = async r => { await Task.Delay(1000); return new HttpResponseMessage(HttpStatusCode.OK) { Content = content }; } };
            await Assert.ThrowsAsync<TimeoutException>(() => new HttpFetcher(h).FetchAsync("https://a.example/x", 1000, TimeSpan.FromMilliseconds(250)));
            var t0 = DateTime.UtcNow;
            while (!content.Disposed && (DateTime.UtcNow - t0).TotalSeconds < 5) await Task.Delay(50);
            Assert.True(content.Disposed, "the late response was never disposed");
        }

        [Fact] public async Task A_Late_Failure_Is_Observed_Not_Left_To_Crash_The_Process()
        {
            var h = new Handler { Fn = async r => { await Task.Delay(600); throw new HttpRequestException("late failure"); } };
            await Assert.ThrowsAsync<TimeoutException>(() => new HttpFetcher(h).FetchAsync("https://a.example/x", 1000, TimeSpan.FromMilliseconds(200)));
            await Task.Delay(1000);
            GC.Collect(); GC.WaitForPendingFinalizers();
        }

        [Fact] public async Task The_Cap_Is_Enforced_With_One_Sentinel_Byte_So_At_Most_Cap_Plus_One_Bytes_Are_Read()
        {
            var endless = new CountingEndlessStream();
            var h = new Handler { Fn = r => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new NoLength(endless) }) };
            await Assert.ThrowsAsync<InvalidDataException>(() => new HttpFetcher(h).FetchAsync("https://a.example/x", 1000, TimeSpan.FromSeconds(10)));
            Assert.True(endless.Requested <= 1001, "asked the stream for " + endless.Requested + " bytes");
        }

        [Fact] public async Task A_Body_Exactly_At_The_Cap_Is_Accepted()
        {
            var h = new Handler { Fn = r => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new NoLength(new MemoryStream(new byte[1000])) }) };
            Assert.Equal(1000, (await new HttpFetcher(h).FetchAsync("https://a.example/x", 1000, TimeSpan.FromSeconds(5))).Length);
        }

        [Fact] public async Task A_Used_Up_Deadline_Starts_No_Request()
        {
            var h = new Handler { Fn = r => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1 }) }) };
            await Assert.ThrowsAsync<TimeoutException>(() => new HttpFetcher(h).FetchAsync("https://a.example/x", 1000, TimeSpan.Zero));
            Assert.Equal(0, h.Calls);
        }

        [Fact] public async Task The_Stalled_Tests_Expect_A_Timeout_Not_Just_Any_Exception()
        {
            var h = new Handler { Fn = async r => { await Task.Delay(Timeout.Infinite); return null; } };
            var ex = await Assert.ThrowsAsync<TimeoutException>(() => new HttpFetcher(h).FetchAsync("https://a.example/x", 1000, TimeSpan.FromMilliseconds(300)));
            Assert.Contains("deadline", ex.Message);
        }
    }
}

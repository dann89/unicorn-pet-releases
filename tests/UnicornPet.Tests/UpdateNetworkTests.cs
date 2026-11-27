using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnicornPet.Core;
using UnicornPet.Core.Updater;
using Xunit;

namespace UnicornPet.Tests
{
    public class UpdateNetworkTests : IDisposable
    {
        class Stub : HttpMessageHandler
        {
            public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Fn;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Fn(r, ct);
        }

        /// <summary>Delivers a few bytes, then stalls until cancelled.</summary>
        class StallStream : Stream
        {
            int _sent;
            public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] b, int o, int c) => ReadAsync(b, o, c, CancellationToken.None).GetAwaiter().GetResult();
            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            {
                if (_sent < 10) { _sent += 10; buffer[offset] = 1; return 10 > count ? count : 10; }
                await Task.Delay(Timeout.Infinite, ct);
                return 0;
            }
            public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
            public override void SetLength(long v) => throw new NotSupportedException();
            public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        }

        /// <summary>Endless data with no Content-Length; counts what was actually pulled.</summary>
        class EndlessStream : Stream
        {
            public long Served;
            public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] b, int o, int c) { Served += c; return c; }
            public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
            public override void SetLength(long v) => throw new NotSupportedException();
            public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        }

        class NoLengthContent : HttpContent
        {
            readonly Stream _s;
            public NoLengthContent(Stream s) { _s = s; }
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) => Task.CompletedTask;
            protected override bool TryComputeLength(out long length) { length = 0; return false; }
            protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult(_s);
        }

        readonly string _root = UpdateTestKit.TempDir();
        public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

        // ---------- fetcher ----------

        [Fact] public async Task Fetch_StalledBody_AbortsWithinDeadline()
        {
            var stub = new Stub { Fn = (r, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new NoLengthContent(new StallStream()) }) };
            var sw = Stopwatch.StartNew();
            await Assert.ThrowsAsync<TimeoutException>(() => new HttpFetcher(stub).FetchAsync("https://a.example/x", 1000000, TimeSpan.FromMilliseconds(400)));
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), "took " + sw.Elapsed);
        }

        [Fact] public async Task Fetch_StalledHeaders_AbortsWithinDeadline()
        {
            var stub = new Stub { Fn = async (r, ct) => { await Task.Delay(Timeout.Infinite, ct); return null; } };
            var sw = Stopwatch.StartNew();
            await Assert.ThrowsAsync<TimeoutException>(() => new HttpFetcher(stub).FetchAsync("https://a.example/x", 1000, TimeSpan.FromMilliseconds(400)));
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), "took " + sw.Elapsed);
        }

        [Fact] public async Task Fetch_DeadlineCoversTheWholeRedirectChain()
        {
            int hops = 0;
            var stub = new Stub
            {
                Fn = async (r, ct) =>
                {
                    hops++;
                    await Task.Delay(250, ct);
                    var resp = new HttpResponseMessage(HttpStatusCode.Found);
                    resp.Headers.Location = new Uri("https://a.example/" + hops);
                    return resp;
                }
            };
            var sw = Stopwatch.StartNew();
            await Assert.ThrowsAsync<TimeoutException>(() => new HttpFetcher(stub).FetchAsync("https://a.example/0", 1000, TimeSpan.FromMilliseconds(600)));
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), "took " + sw.Elapsed);
            Assert.True(hops <= 4, "hops " + hops);
        }

        [Fact] public async Task Fetch_EndlessBodyWithoutContentLength_StopsAtTheCap_WithoutBufferingMore()
        {
            var endless = new EndlessStream();
            var stub = new Stub { Fn = (r, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new NoLengthContent(endless) }) };
            await Assert.ThrowsAsync<InvalidDataException>(() => new HttpFetcher(stub).FetchAsync("https://a.example/x", 64 * 1024, TimeSpan.FromSeconds(10)));
            Assert.True(endless.Served <= 64 * 1024 + 1, "pulled " + endless.Served);
        }

        [Fact] public async Task Fetch_NormalResponse_StillWorksWithDeadline()
        {
            var stub = new Stub { Fn = (r, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("ok")) }) };
            Assert.Equal("ok", Encoding.UTF8.GetString(await new HttpFetcher(stub).FetchAsync("https://a.example/x", 100, TimeSpan.FromSeconds(5))));
        }

        // ---------- checker ----------

        static readonly DateTime Now = new DateTime(2026, 11, 24, 12, 0, 0);

        (UpdateChecker c, List<(string url, long max, TimeSpan dl)> calls) MakeChecker(Func<string, Task<byte[]>> http)
        {
            var calls = new List<(string, long, TimeSpan)>();
            var stager = new UpdateStager(Path.Combine(_root, "versions"));
            Directory.CreateDirectory(Path.Combine(_root, "versions", "1.0.0.0"));
            File.WriteAllText(Path.Combine(_root, "versions", "1.0.0.0", "UnicornPet.exe"), "x");
            stager.InitState("1.0.0.0");
            var key = UpdateTestKit.NewKey();
            var c = new UpdateChecker((u, m, d) => { calls.Add((u, m, d)); return http(u); }, new ManifestVerifier(UpdateTestKit.PublicXml(key)), stager, new Version(1, 0, 0, 0), () => Now);
            return (c, calls);
        }

        const string ManifestUrl = "https://example.com/manifest.json";

        [Fact] public async Task Check_ManifestAndZipFetches_UseSeparateCapsAndDeadlines()
        {
            var rsa = UpdateTestKit.NewKey();
            var zip = UpdateTestKit.Zip(("UnicornPet.exe", "v2"));
            var m = UpdateTestKit.Signed(rsa, "1.1.0", "https://example.com/app.zip", zip);
            var json = "{\"version\":\"" + m.Version + "\",\"url\":\"" + m.Url + "\",\"sha256\":\"" + m.Sha256 + "\",\"signature\":\"" + m.Signature + "\"}";
            var calls = new List<(string, long, TimeSpan)>();
            var stager = new UpdateStager(Path.Combine(_root, "versions"));
            Directory.CreateDirectory(Path.Combine(_root, "versions", "1.0.0.0"));
            File.WriteAllText(Path.Combine(_root, "versions", "1.0.0.0", "UnicornPet.exe"), "x");
            stager.InitState("1.0.0.0");
            var c = new UpdateChecker((u, max, dl) => { calls.Add((u, max, dl)); return Task.FromResult(u == ManifestUrl ? Encoding.UTF8.GetBytes(json) : zip); },
                new ManifestVerifier(UpdateTestKit.PublicXml(rsa)), stager, new Version(1, 0, 0, 0), () => Now);
            Assert.True(await c.CheckAsync(ManifestUrl));
            Assert.Equal(2, calls.Count);
            Assert.Equal(64 * 1024, calls[0].Item2);
            Assert.Equal(TimeSpan.FromSeconds(30), calls[0].Item3);
            Assert.True(calls[1].Item2 > calls[0].Item2);
            Assert.True(calls[1].Item3 > calls[0].Item3);
        }

        [Fact] public async Task Check_OversizeManifestBody_IsRejected()
        {
            var (c, calls) = MakeChecker(u => Task.FromResult(new byte[200 * 1024]));
            Assert.False(await c.CheckAsync(ManifestUrl));
            Assert.Single(calls);
        }

        [Fact] public async Task Check_PersistsAttemptBeforeTheNetworkCall()
        {
            var store = new SettingsStore(Path.Combine(_root, "settings.json"));
            var seenAtRequest = (DateTime?)null;
            var settings = new Settings();
            var (c, calls) = MakeChecker(u => { seenAtRequest = store.Load().LastUpdateCheck; throw new HttpRequestException("down"); });
            Assert.False(await c.CheckAsync(ManifestUrl, settings, false, () => store.Save(settings)));
            Assert.Equal(Now, seenAtRequest);
        }

        [Fact] public async Task Check_RestartAfterCrash_DoesNotRecheckWithinSixHours()
        {
            var store = new SettingsStore(Path.Combine(_root, "settings.json"));
            var s1 = new Settings();
            var (c1, calls1) = MakeChecker(u => throw new Exception("crash"));
            await c1.CheckAsync(ManifestUrl, s1, false, () => store.Save(s1));
            Assert.Single(calls1);
            var s2 = store.Load();
            var (c2, calls2) = MakeChecker(u => throw new Exception("should not be called"));
            Assert.False(await c2.CheckAsync(ManifestUrl, s2, false, () => store.Save(s2)));
            Assert.Empty(calls2);
        }

        [Fact] public async Task Check_ManualForce_BypassesInterval_ButStillPersists()
        {
            var store = new SettingsStore(Path.Combine(_root, "settings.json"));
            var s = new Settings { LastUpdateCheck = Now.AddMinutes(-1) };
            var (c, calls) = MakeChecker(u => throw new Exception("down"));
            await c.CheckAsync(ManifestUrl, s, force: true, persist: () => store.Save(s));
            Assert.Single(calls);
            Assert.Equal(Now, store.Load().LastUpdateCheck);
        }

        [Fact] public async Task Check_OverlappingCalls_OnlyOneRuns()
        {
            var release = new TaskCompletionSource<byte[]>();
            var (c, calls) = MakeChecker(u => release.Task);
            var first = c.CheckAsync(ManifestUrl);
            Assert.True(c.IsBusy);
            Assert.False(await c.CheckAsync(ManifestUrl, force: true));
            Assert.Single(calls);
            release.SetResult(new byte[0]);
            await first;
            Assert.False(c.IsBusy);
        }

        [Fact] public async Task Check_AfterOneFinishes_AnotherCanRun()
        {
            var (c, calls) = MakeChecker(u => Task.FromResult(new byte[0]));
            await c.CheckAsync(ManifestUrl);
            await c.CheckAsync(ManifestUrl);
            Assert.Equal(2, calls.Count);
        }

        [Fact] public async Task Check_WhenFetchThrows_GuardIsReleased()
        {
            var (c, calls) = MakeChecker(u => throw new Exception("boom"));
            await c.CheckAsync(ManifestUrl);
            Assert.False(c.IsBusy);
        }

        [Fact] public async Task Check_BusyCallDoesNotTouchTheIntervalRecord()
        {
            var release = new TaskCompletionSource<byte[]>();
            var s = new Settings();
            var (c, _) = MakeChecker(u => release.Task);
            var first = c.CheckAsync(ManifestUrl, s);
            s.LastUpdateCheck = null;
            Assert.False(await c.CheckAsync(ManifestUrl, s, force: true));
            Assert.Null(s.LastUpdateCheck);
            release.SetResult(new byte[0]);
            await first;
        }
    }
}

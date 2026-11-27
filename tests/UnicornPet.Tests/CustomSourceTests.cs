using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnicornPet.Core;
using UnicornPet.Core.Updater;
using Xunit;

namespace UnicornPet.Tests
{
    /// <summary>Loading the person's own message file or web list: guarded reads, the daily rule, the cache, fixed status text, and the live bank swap.</summary>
    public class CustomSourceTests : IDisposable
    {
        readonly string _dir = Path.Combine(Path.GetTempPath(), "up-src-" + Guid.NewGuid().ToString("N"));
        string CachePath => Path.Combine(_dir, "messages.custom.json");
        public CustomSourceTests() { Directory.CreateDirectory(_dir); }
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        static byte[] Bytes(string s) => new UTF8Encoding(false).GetBytes(s);
        const string GoodJson = "{\"random\":[\"Hello {name}.\"],\"name\":\"Alex\"}";
        const string Url = "https://example.invalid/pets/list.json";

        static readonly FetchFunc NoFetch = (u, m, d) => throw new InvalidOperationException("no network in this test");
        static readonly ReadFileFunc NoRead = (p, m) => throw new InvalidOperationException("no file in this test");

        CustomMessageLoader FileLoader(ReadFileFunc read, TimeSpan? limit = null) => new CustomMessageLoader(NoFetch, read, CachePath, limit);
        CustomMessageLoader WebLoader(FetchFunc fetch) => new CustomMessageLoader(fetch, NoRead, CachePath);

        // ---------- decoding and form rules ----------

        [Fact] public void Decode_StripsOneBom() => Assert.Equal("{}", CustomSourceRules.Decode(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}' }));

        [Fact]
        public void Decode_Utf16_IsNull()
        {
            var b = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("{}")).ToArray();
            Assert.Null(CustomSourceRules.Decode(b));
        }

        [Fact]
        public void Decode_Empty_OrOverCap_IsNull()
        {
            Assert.Null(CustomSourceRules.Decode(new byte[0]));
            Assert.Null(CustomSourceRules.Decode(new byte[65537]));
            Assert.Null(CustomSourceRules.Decode(null!));
        }

        [Fact]
        public void Decode_ExactlyAtCap_IsText()
        {
            var b = Enumerable.Repeat((byte)' ', 65536).ToArray();
            Assert.Equal(65536, CustomSourceRules.Decode(b)!.Length);
        }

        [Fact] public void Decode_BrokenUtf8_IsNull() => Assert.Null(CustomSourceRules.Decode(new byte[] { (byte)'{', 0xC3, (byte)'}' }));

        [Theory]
        [InlineData(MessageSourceKind.BuiltIn, "", "", "")]
        [InlineData(MessageSourceKind.File, @"C:\a.json", "", "")]
        [InlineData(MessageSourceKind.File, "a.json", "", "Type the full path of a .json file.")]
        [InlineData(MessageSourceKind.Web, "", "http://example.invalid/a", "Type an https address.")]
        [InlineData(MessageSourceKind.Web, "", "https://example.invalid/a", "")]
        [InlineData(MessageSourceKind.BuiltIn, "a.json", "http://example.invalid/a", "")]
        public void FormError_Cases(MessageSourceKind k, string file, string url, string expected) => Assert.Equal(expected, CustomSourceRules.FormError(k, file, url));

        // ---------- file ----------

        [Fact]
        public async Task File_Good_Loads_AndMaySetTheName()
        {
            var r = await FileLoader((p, m) => Bytes(GoodJson)).LoadFileAsync(@"C:\lists\mine.json");
            Assert.Equal(CustomLoadOutcome.Loaded, r.Outcome);
            Assert.Equal("Alex", r.Messages.Name);
        }

        [Fact]
        public async Task File_WithBom_Loads()
        {
            var r = await FileLoader((p, m) => new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Bytes(GoodJson)).ToArray()).LoadFileAsync(@"C:\a.json");
            Assert.Equal(CustomLoadOutcome.Loaded, r.Outcome);
        }

        [Fact]
        public async Task File_Utf16_IsInvalid_WithAPlainReason()
        {
            var r = await FileLoader((p, m) => Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(GoodJson)).ToArray()).LoadFileAsync(@"C:\a.json");
            Assert.Equal(CustomLoadOutcome.Invalid, r.Outcome);
            Assert.Equal("not UTF-8 text", r.Problem);
        }

        [Fact]
        public async Task File_Missing_IsMissing() => Assert.Equal(CustomLoadOutcome.Missing, (await FileLoader((p, m) => null!).LoadFileAsync(@"C:\a.json")).Outcome);

        [Fact]
        public async Task File_OverCap_IsTooLarge() => Assert.Equal(CustomLoadOutcome.TooLarge, (await FileLoader((p, m) => new byte[65537]).LoadFileAsync(@"C:\a.json")).Outcome);

        [Fact]
        public async Task File_ReadThrows_IsUnreadable()
        {
            Assert.Equal(CustomLoadOutcome.Unreadable, (await FileLoader((p, m) => throw new IOException("x")).LoadFileAsync(@"C:\a.json")).Outcome);
            Assert.Equal(CustomLoadOutcome.Unreadable, (await FileLoader((p, m) => throw new UnauthorizedAccessException("x")).LoadFileAsync(@"C:\a.json")).Outcome);
        }

        [Fact]
        public async Task File_BadPath_IsRefused_WithoutReading()
        {
            int calls = 0;
            var r = await FileLoader((p, m) => { calls++; return Bytes(GoodJson); }).LoadFileAsync("a.json");
            Assert.Equal(CustomLoadOutcome.Refused, r.Outcome);
            Assert.Equal(0, calls);
        }

        [Fact]
        public async Task File_Invalid_IsInvalid_WithTheReason()
        {
            var r = await FileLoader((p, m) => Bytes("{\"random\":[]}")).LoadFileAsync(@"C:\a.json");
            Assert.Equal(CustomLoadOutcome.Invalid, r.Outcome);
            Assert.Equal("random: missing or empty", r.Problem);
            Assert.Null(r.Messages);
        }

        [Fact]
        public async Task File_PassesTheCap_ToTheReader()
        {
            long seen = 0;
            await FileLoader((p, m) => { seen = m; return Bytes(GoodJson); }).LoadFileAsync(@"C:\a.json");
            Assert.Equal(65536, seen);
        }

        [Fact]
        public async Task File_HangingRead_TimesOut_AndTheCallerIsNotBlocked()
        {
            using (var gate = new ManualResetEventSlim(false))
            {
                var loader = FileLoader((p, m) => { gate.Wait(TimeSpan.FromSeconds(30)); return Bytes(GoodJson); }, TimeSpan.FromMilliseconds(100));
                var sw = Stopwatch.StartNew();
                var r = await loader.LoadFileAsync(@"\\server\share\a.json");
                Assert.Equal(CustomLoadOutcome.TimedOut, r.Outcome);
                Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3));
                Assert.True(loader.IsBusy);
                Assert.Equal(CustomLoadOutcome.Busy, (await loader.LoadFileAsync(@"C:\a.json")).Outcome);   // the stuck read still counts as in flight
                gate.Set();
                var until = DateTime.UtcNow.AddSeconds(5);
                while (loader.IsBusy && DateTime.UtcNow < until) await Task.Delay(10);
                Assert.False(loader.IsBusy);
            }
        }

        [Fact]
        public void LoadFileAsync_ReturnsBeforeTheReadFinishes()
        {
            using (var gate = new ManualResetEventSlim(false))
            {
                var loader = FileLoader((p, m) => { gate.Wait(TimeSpan.FromSeconds(30)); return Bytes(GoodJson); });
                var t = loader.LoadFileAsync(@"C:\a.json");
                Assert.False(t.IsCompleted);
                gate.Set();
                Assert.True(t.Wait(TimeSpan.FromSeconds(10)));
                Assert.Equal(CustomLoadOutcome.Loaded, t.Result.Outcome);
            }
        }

        [Fact]
        public void ReadCapped_ReadsAtMostCapPlusOne()
        {
            var big = Path.Combine(_dir, "big.json");
            File.WriteAllBytes(big, new byte[200000]);
            Assert.Equal(65537, CustomMessageLoader.ReadCapped(big, 65536).Length);
            var small = Path.Combine(_dir, "small.json");
            File.WriteAllBytes(small, Bytes(GoodJson));
            Assert.Equal(Bytes(GoodJson), CustomMessageLoader.ReadCapped(small, 65536));
            Assert.Null(CustomMessageLoader.ReadCapped(Path.Combine(_dir, "missing.json"), 65536));
            Assert.Null(CustomMessageLoader.ReadCapped(Path.Combine(_dir, "no-such-dir", "missing.json"), 65536));
        }

        // ---------- web ----------

        [Fact]
        public async Task Web_Good_Loads_WritesTheCache_AndNeverSetsTheName()
        {
            var raw = Bytes(GoodJson);
            var r = await WebLoader((u, m, d) => Task.FromResult(raw)).FetchWebAsync(Url);
            Assert.Equal(CustomLoadOutcome.Loaded, r.Outcome);
            Assert.Equal("", r.Messages.Name);
            Assert.Equal(raw, File.ReadAllBytes(CachePath));
        }

        [Fact]
        public async Task Web_PassesCapAndDeadline_ToTheFetcher()
        {
            var seen = new List<(string, long, TimeSpan)>();
            await WebLoader((u, m, d) => { seen.Add((u, m, d)); return Task.FromResult(Bytes(GoodJson)); }).FetchWebAsync(Url);
            Assert.Equal(new[] { (Url, 65536L, TimeSpan.FromSeconds(30)) }, seen);
        }

        [Fact]
        public async Task Web_NotHttps_IsRefused_WithoutFetching()
        {
            int calls = 0;
            var r = await WebLoader((u, m, d) => { calls++; return Task.FromResult(Bytes(GoodJson)); }).FetchWebAsync("http://example.invalid/a.json");
            Assert.Equal(CustomLoadOutcome.Refused, r.Outcome);
            Assert.Equal(0, calls);
        }

        [Theory]
        [InlineData(typeof(TimeoutException), CustomLoadOutcome.TimedOut)]
        [InlineData(typeof(InvalidDataException), CustomLoadOutcome.TooLarge)]
        [InlineData(typeof(HttpRequestException), CustomLoadOutcome.NetworkFailed)]
        [InlineData(typeof(InvalidOperationException), CustomLoadOutcome.NetworkFailed)]
        public async Task Web_Failures_KeepTheOldCache(Type thrown, CustomLoadOutcome expected)
        {
            File.WriteAllText(CachePath, "old");
            var ex = (Exception)Activator.CreateInstance(thrown, "x")!;
            var r = await WebLoader((u, m, d) => throw ex).FetchWebAsync(Url);
            Assert.Equal(expected, r.Outcome);
            Assert.Equal("old", File.ReadAllText(CachePath));
        }

        [Fact]
        public async Task Web_FaultedTask_IsMapped_Too()
        {
            var r = await WebLoader((u, m, d) => Task.FromException<byte[]>(new TimeoutException("x"))).FetchWebAsync(Url);
            Assert.Equal(CustomLoadOutcome.TimedOut, r.Outcome);
        }

        [Fact]
        public async Task Web_Invalid_KeepsTheOldCache()
        {
            File.WriteAllText(CachePath, "old");
            var r = await WebLoader((u, m, d) => Task.FromResult(Bytes("{\"random\":[\"{pet}\"]}"))).FetchWebAsync(Url);
            Assert.Equal(CustomLoadOutcome.Invalid, r.Outcome);
            Assert.Equal("old", File.ReadAllText(CachePath));
        }

        [Fact]
        public async Task Web_CacheNotWritable_IsCacheFailed()
        {
            var blocker = Path.Combine(_dir, "a-file");
            File.WriteAllText(blocker, "x");
            var loader = new CustomMessageLoader((u, m, d) => Task.FromResult(Bytes(GoodJson)), NoRead, Path.Combine(blocker, "messages.custom.json"));
            Assert.Equal(CustomLoadOutcome.CacheFailed, (await loader.FetchWebAsync(Url)).Outcome);
        }

        [Fact]
        public async Task Web_SynchronousFetch_DoesNotRunOnTheCaller()
        {
            int fetchThread = -1;
            int caller = Environment.CurrentManagedThreadId;
            await WebLoader((u, m, d) => { fetchThread = Environment.CurrentManagedThreadId; Thread.Sleep(50); return Task.FromResult(Bytes(GoodJson)); }).FetchWebAsync(Url);
            Assert.NotEqual(-1, fetchThread);
            Assert.NotEqual(caller, fetchThread);
        }

        [Fact]
        public async Task Web_SecondCallWhileBusy_IsBusy()
        {
            var release = new TaskCompletionSource<byte[]>();
            var loader = WebLoader((u, m, d) => release.Task);
            var first = loader.FetchWebAsync(Url);
            var until = DateTime.UtcNow.AddSeconds(5);
            while (!loader.IsBusy && DateTime.UtcNow < until) await Task.Delay(5);
            Assert.Equal(CustomLoadOutcome.Busy, (await loader.FetchWebAsync(Url)).Outcome);
            release.SetResult(Bytes(GoodJson));
            Assert.Equal(CustomLoadOutcome.Loaded, (await first).Outcome);
            Assert.False(loader.IsBusy);
        }

        [Fact]
        public void ReadCache_Missing_IsMissing_Good_IsLoaded_Broken_IsInvalid()
        {
            var loader = WebLoader(NoFetch);
            Assert.Equal(CustomLoadOutcome.Missing, loader.ReadCache().Outcome);
            File.WriteAllBytes(CachePath, Bytes(GoodJson));
            var good = loader.ReadCache();
            Assert.Equal(CustomLoadOutcome.Loaded, good.Outcome);
            Assert.Equal("", good.Messages.Name);   // a web list never sets the name, cached or not
            File.WriteAllText(CachePath, "{ broken");
            Assert.Equal(CustomLoadOutcome.Invalid, loader.ReadCache().Outcome);
        }

        // ---------- timing ----------

        static readonly DateTime T0 = new DateTime(2026, 11, 25, 9, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void Due_NeverFetched_IsTrue()
        {
            DateTime? last = null;
            Assert.True(CustomMessageLoader.Due(T0, ref last, false));
        }

        [Fact]
        public void Due_After23h59_IsFalse_After24h_IsTrue()
        {
            DateTime? last = T0;
            Assert.False(CustomMessageLoader.Due(T0.AddHours(23).AddMinutes(59), ref last, false));
            Assert.True(CustomMessageLoader.Due(T0.AddHours(24), ref last, false));
        }

        [Fact]
        public void Due_ClockWentBack_ClampsAndWaits()
        {
            DateTime? last = T0.AddHours(1);
            Assert.False(CustomMessageLoader.Due(T0, ref last, false));
            Assert.Equal(T0, last);
        }

        [Fact]
        public void Due_ClockWentBack_ReloadNowStillWorks()
        {
            DateTime? last = T0.AddHours(1);
            Assert.True(CustomMessageLoader.Due(T0, ref last, true));
            Assert.Equal(T0, last);
        }

        [Fact]
        public void Due_Forced_RespectsTheTenSecondGap()
        {
            DateTime? last = T0;
            Assert.False(CustomMessageLoader.Due(T0.AddSeconds(9), ref last, true));
            Assert.True(CustomMessageLoader.Due(T0.AddSeconds(10), ref last, true));
        }

        [Fact]
        public void RetryStamp_AllowsTheNextTryOneHourLater()
        {
            DateTime? stamp = CustomMessageLoader.RetryStamp(T0);
            Assert.False(CustomMessageLoader.Due(T0.AddMinutes(59), ref stamp, false));
            Assert.True(CustomMessageLoader.Due(T0.AddMinutes(60), ref stamp, false));
        }

        // ---------- status text ----------

        [Fact]
        public void Describe_IsFixedText()
        {
            Assert.Equal("Loaded.", CustomStatus.Describe(CustomLoadOutcome.Loaded, "", true));
            Assert.Equal("Not usable: random: missing or empty. Still using the last good copy.", CustomStatus.Describe(CustomLoadOutcome.Invalid, "random: missing or empty", true));
            Assert.Equal("File not found. Using the built-in lines.", CustomStatus.Describe(CustomLoadOutcome.Missing, "", false));
        }

        [Theory]
        [InlineData(CustomLoadOutcome.TooLarge, "Too big (the limit is 64 KB).")]
        [InlineData(CustomLoadOutcome.Unreadable, "Couldn't read the file.")]
        [InlineData(CustomLoadOutcome.TimedOut, "Took too long.")]
        [InlineData(CustomLoadOutcome.Refused, "That address or path can't be used.")]
        [InlineData(CustomLoadOutcome.NetworkFailed, "Couldn't reach the address.")]
        [InlineData(CustomLoadOutcome.CacheFailed, "Couldn't keep a copy.")]
        [InlineData(CustomLoadOutcome.Busy, "Already loading.")]
        public void Describe_OtherReasons(CustomLoadOutcome outcome, string reason)
        {
            Assert.Equal(reason + " Still using the last good copy.", CustomStatus.Describe(outcome, "", true));
            Assert.Equal(reason + " Using the built-in lines.", CustomStatus.Describe(outcome, "", false));
        }

        // ---------- Scheduler.ReplaceBank ----------

        static readonly DateTime Mon = new DateTime(2026, 11, 23);
        class Clock { public DateTime Now; public Func<DateTime> F => () => Now; }

        static MessageBank Bank(string tag, params TimeSpan[] times) =>
            new MessageBank("", times.Select(t => new ScheduledSlot(tag + t.Hours, t, new[] { tag + " at " + t.ToString("hhmm") })).ToList(), new[] { "Hi {name}." }, new string[0]);

        static readonly MessageBank A = Bank("A", new TimeSpan(8, 0, 0), new TimeSpan(12, 45, 0));
        static readonly MessageBank B = Bank("B", new TimeSpan(9, 0, 0), new TimeSpan(12, 45, 0));

        [Fact]
        public void ReplaceBank_PastSlots_DoNotFire()
        {
            // 09:05: B's 09:00 slot would still be within its 10 minutes if the swap did not mark it as passed
            var c = new Clock { Now = Mon.AddHours(9).AddMinutes(5) };
            var s = new Scheduler(A, c.F, new Random(1));
            s.ReplaceBank(B);
            var sp = s.Tick(new Settings());
            Assert.True(sp == null || sp.Kind != SpeechKind.Scheduled);
        }

        [Fact]
        public void ReplaceBank_FutureSlot_StillFires()
        {
            var c = new Clock { Now = Mon.AddHours(10) };
            var s = new Scheduler(A, c.F, new Random(1));
            s.ReplaceBank(B);
            c.Now = Mon.AddHours(12).AddMinutes(45);
            var sp = s.Tick(new Settings());
            Assert.NotNull(sp);
            Assert.Equal(SpeechKind.Scheduled, sp!.Kind);
            Assert.Equal("B at 1245", sp.Text);
        }

        [Fact]
        public void ReplaceBank_KeepsTheCurrentName()
        {
            var c = new Clock { Now = Mon.AddHours(10) };
            var s = new Scheduler(A, c.F, new Random(1));
            s.UpdateName("Alex");
            s.ReplaceBank(B);
            Assert.Equal("Hi Alex.", s.SpeakNow(new Settings { CheekyMode = false }).Text);
        }

        [Fact]
        public void ReplaceBank_Null_IsIgnored()
        {
            var c = new Clock { Now = Mon.AddHours(10) };
            var s = new Scheduler(A, c.F, new Random(1));
            s.ReplaceBank(null!);
            c.Now = Mon.AddHours(12).AddMinutes(45);
            Assert.Equal("A at 1245", s.Tick(new Settings())!.Text);
        }

        // ---------- the window wiring (read as text: the WPF code is not built by the test project) ----------

        static string Read(string appFile)
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "UnicornPet.sln"))) dir = Path.GetDirectoryName(dir);
            return File.ReadAllText(Path.Combine(dir!, "src", "UnicornPet.App", appFile));
        }

        [Fact]
        public void Window_StillBuildsTheBaseChain_ThenLaysTheCustomSourceOver()
        {
            var src = Read("PetWindow.xaml.cs");
            Assert.Contains("MessageBank.LoadWithRemote(", src);
            Assert.Contains("bank = StartCustomMessages(bank);", src);
            Assert.Contains("\"messages.custom.json\"", src);
        }

        [Fact]
        public void MessagesWiring_NeverLoadsOnTheCallingThread()
        {
            var src = Read("PetWindow.Messages.cs");
            Assert.DoesNotContain("File.Read", src);
            Assert.DoesNotContain(".Result", src);
            Assert.DoesNotContain(".Wait(", src);
            Assert.DoesNotContain("OpenFileDialog", Read("SettingsWindow.xaml.cs"));
        }
    }
}

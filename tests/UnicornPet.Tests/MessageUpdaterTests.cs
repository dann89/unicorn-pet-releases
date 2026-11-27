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
    public class MessageVersionTests
    {
        [Theory]
        [InlineData("1")] [InlineData("0")] [InlineData("12")] [InlineData("1.2")] [InlineData("1.2.3")] [InlineData("999999.999999.999999")] [InlineData("0.0.1")]
        public void Valid(string v) => Assert.NotNull(MessageVersion.Parse(v));

        [Theory]
        [InlineData(null)] [InlineData("")] [InlineData(" ")] [InlineData(" 1")] [InlineData("1 ")] [InlineData("-1")] [InlineData("1.")] [InlineData(".1")] [InlineData("1..2")]
        [InlineData("1.2.3.4")] [InlineData("1000000")] [InlineData("1.2.1000000")] [InlineData("01")] [InlineData("1.02")] [InlineData("1,2")] [InlineData("1.2-beta")]
        [InlineData("١")] [InlineData("1\n")] [InlineData("v1")] [InlineData("1e3")]
        public void Invalid(string v) => Assert.Null(MessageVersion.Parse(v));

        [Fact] public void Ordering_IsNumeric_NotTextual()
        {
            Assert.True(MessageVersion.Compare("10", "9") > 0);
            Assert.True(MessageVersion.Compare("1.10", "1.9") > 0);
            Assert.True(MessageVersion.Compare("2", "1.9.9") > 0);
            Assert.Equal(0, MessageVersion.Compare("1", "1.0.0"));
            Assert.True(MessageVersion.Compare("1.0.1", "1.0") > 0);
            Assert.True(MessageVersion.Compare("0.9", "1") < 0);
        }

        [Fact] public void Publisher_Script_Uses_The_Same_Pattern()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "UnicornPet.sln"))) dir = Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            var code = File.ReadAllLines(Path.Combine(dir, "tools", "publish-messages.ps1")).Where(l => !l.TrimStart().StartsWith("#")).ToList();
            Assert.Contains(code, l => l.Trim() == "$versionPattern = '" + MessageVersion.Pattern + "'");
            Assert.Contains(code, l => l.Trim() == "$signingDomain = '" + MessageUpdater.SigningDomain + "'");
            Assert.Contains(code, l => l.Contains("$signingDomain|$Version|$url|$sha"));
        }
    }

    public class MessageValidatorTests
    {
        static string Good(string extra = "") =>
            "{\"scheduled\":[{\"id\":\"start\",\"time\":\"08:00\",\"pool\":[\"Good morning, {name}!\",\"Morning.\"]}]," +
            "\"random\":[\"Drink water.\",\"Hi {name}.\"],\"cheeky\":[\"Reply-all again.\"],\"welcome\":[\"Hi {name}! Right-click me.\"],\"hello\":[\"Back again.\"]" + extra + "}";

        static string Err(string json) { MessageBank b; return MessageValidator.Validate(json, out b); }

        [Fact] public void Valid_List_Passes_And_Produces_A_Bank()
        {
            MessageBank b;
            Assert.Null(MessageValidator.Validate(Good(), out b));
            Assert.Equal("", b.Name);
            Assert.Single(b.Scheduled);
            Assert.Equal(new TimeSpan(8, 0, 0), b.Scheduled[0].Time);
            Assert.Equal(2, b.Random.Count);
        }

        [Fact] public void The_Built_In_Defaults_Pass_The_Same_Validator()
        {
            var text = new StreamReader(typeof(MessageBank).Assembly.GetManifestResourceStream("messages.default.json")).ReadToEnd();
            Assert.Null(Err(text));
        }

        [Theory]
        [InlineData("")] [InlineData("not json")] [InlineData("[]")] [InlineData("{}")] [InlineData("null")]
        public void Garbage_Is_Rejected(string json) => Assert.NotNull(Err(json));

        [Fact] public void Unknown_TopLevel_Key_Is_Rejected() => Assert.NotNull(Err(Good(",\"script\":\"x\"")));

        [Fact] public void Missing_Pools_Are_Rejected()
        {
            foreach (var pool in new[] { "random", "cheeky", "welcome", "hello" })
            {
                // remove the property entirely (renaming it would be rejected as an unknown key, which proves nothing)
                var json = System.Text.RegularExpressions.Regex.Replace(Good(), ",\"" + pool + "\":\\[[^\\]]*\\]", "");
                Assert.DoesNotContain("\"" + pool + "\"", json);
                Assert.Equal("missing or empty", Err(json).Substring(Err(json).IndexOf(":", StringComparison.Ordinal) + 2));
            }
            Assert.NotNull(Err("{\"random\":[\"a\"],\"cheeky\":[\"a\"],\"welcome\":[\"a\"],\"hello\":[\"a\"]}"));
        }

        [Fact] public void Empty_Pools_Are_Rejected()
        {
            Assert.NotNull(Err(Good().Replace("\"hello\":[\"Back again.\"]", "\"hello\":[]")));
            Assert.NotNull(Err(Good().Replace("[\"Good morning, {name}!\",\"Morning.\"]", "[]")));
        }

        [Fact] public void Overlong_Line_Is_Rejected()
        {
            Assert.Null(Err(Good().Replace("Drink water.", new string('a', 140))));
            Assert.NotNull(Err(Good().Replace("Drink water.", new string('a', 141))));
        }

        [Theory]
        [InlineData("bell\\u0007")] [InlineData("nul\\u0000")] [InlineData("tab\\there")] [InlineData("new\\nline")] [InlineData("del\\u007f")]
        [InlineData("bidi\\u202eevil")] [InlineData("zero\\u200bwidth")] [InlineData("sep\\u2028x")] [InlineData("para\\u2029x")] [InlineData("private\\ue000")]
        [InlineData("lone\\ud83d")] [InlineData("bom\\ufeff")]
        public void Control_Format_And_Odd_Characters_Are_Rejected(string line)
        {
            Assert.NotNull(Err(Good().Replace("Drink water.", line)));
        }

        [Fact] public void Normal_Text_Emoji_And_Accents_Are_Allowed()
        {
            Assert.Null(Err(Good().Replace("Drink water.", "Zoë says hi \\ud83e\\udd84 \\u2014 nice")));
        }

        [Theory]
        [InlineData("{name")] [InlineData("name}")] [InlineData("{other}")] [InlineData("{{name}}")] [InlineData("{ name }")] [InlineData("{0}")] [InlineData("{Name}")] [InlineData("a } b")]
        public void Only_The_Exact_Name_Placeholder_Is_Allowed(string line)
        {
            Assert.NotNull(Err(Good().Replace("Drink water.", line)));
        }

        [Fact] public void Exact_Name_Placeholder_Is_Fine() => Assert.Null(Err(Good().Replace("Drink water.", "{name}, {name}!")));

        [Fact] public void Blank_Lines_Are_Rejected() => Assert.NotNull(Err(Good().Replace("Drink water.", "   ")));

        [Fact] public void Too_Many_Lines_Are_Rejected()
        {
            var many = string.Join(",", Enumerable.Range(0, 301).Select(i => "\"line " + i + "\""));
            Assert.NotNull(Err(Good().Replace("\"random\":[\"Drink water.\",\"Hi {name}.\"]", "\"random\":[" + many + "]")));
            var ok = string.Join(",", Enumerable.Range(0, 300).Select(i => "\"line " + i + "\""));
            Assert.Null(Err(Good().Replace("\"random\":[\"Drink water.\",\"Hi {name}.\"]", "\"random\":[" + ok + "]")));
        }

        [Fact] public void Too_Many_Slots_Are_Rejected()
        {
            var slots = string.Join(",", Enumerable.Range(0, 13).Select(i => "{\"id\":\"s" + i + "\",\"time\":\"" + (8 + i % 10).ToString("00") + ":" + (i * 4).ToString("00") + "\",\"pool\":[\"x\"]}"));
            Assert.NotNull(Err(Good().Replace("[{\"id\":\"start\",\"time\":\"08:00\",\"pool\":[\"Good morning, {name}!\",\"Morning.\"]}]", "[" + slots + "]")));
        }

        [Theory]
        [InlineData("8:00")] [InlineData("24:00")] [InlineData("08:60")] [InlineData("08:00:00")] [InlineData("noon")] [InlineData("")] [InlineData("-1:00")]
        public void Bad_Slot_Times_Are_Rejected(string time)
        {
            Assert.NotNull(Err(Good().Replace("\"time\":\"08:00\"", "\"time\":\"" + time + "\"")));
        }

        [Fact] public void Slot_With_Unknown_Key_Or_Bad_Id_Is_Rejected()
        {
            Assert.NotNull(Err(Good().Replace("\"id\":\"start\"", "\"id\":\"start\",\"run\":\"x\"")));
            Assert.NotNull(Err(Good().Replace("\"id\":\"start\"", "\"id\":\"bad id!\"")));
        }

        [Fact] public void Non_String_Pool_Entries_Are_Rejected()
        {
            Assert.NotNull(Err(Good().Replace("\"Drink water.\"", "5")));
            Assert.NotNull(Err(Good().Replace("\"Drink water.\"", "null")));
            Assert.NotNull(Err(Good().Replace("\"Drink water.\"", "[\"nested\"]")));
        }

        [Fact] public void A_Name_In_The_File_Is_Ignored_Not_Applied()
        {
            MessageBank b;
            Assert.Null(MessageValidator.Validate(Good().Replace("{\"scheduled\"", "{\"name\":\"Someone\",\"scheduled\""), out b));
            Assert.Equal("", b.Name);
        }

        [Fact] public void A_Non_String_Name_Is_Rejected() => Assert.NotNull(Err(Good().Replace("{\"scheduled\"", "{\"name\":5,\"scheduled\"")));
    }

    public class MessageUpdaterTests : IDisposable
    {
        readonly string _dir = UpdateTestKit.TempDir();
        readonly RSA _rsa = UpdateTestKit.NewKey();
        readonly DateTime _now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        readonly List<string> _calls = new List<string>();
        string Remote => Path.Combine(_dir, "messages.remote.json");
        const string ManifestUrl = "https://github.com/x/y/releases/latest/download/messages.manifest.json";
        const string ListUrl = "https://github.com/x/y/releases/download/v2/messages.json";

        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        static string GoodList(string tag = "A") =>
            "{\"scheduled\":[{\"id\":\"start\",\"time\":\"08:00\",\"pool\":[\"Morning " + tag + ".\"]}],\"random\":[\"Random " + tag + ".\"],\"cheeky\":[\"Cheeky " + tag + ".\"],\"welcome\":[\"Welcome " + tag + ".\"],\"hello\":[\"Hello " + tag + ".\"]}";

        string SignedManifest(string version, string url, byte[] list, Func<string, string> tamper = null)
        {
            var sha = UpdateTestKit.Sha(list);
            var sig = Convert.ToBase64String(_rsa.SignData(Encoding.UTF8.GetBytes(MessageUpdater.SigningDomain + "|" + version + "|" + url + "|" + sha), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            var json = "{\"version\":\"" + version + "\",\"url\":\"" + url + "\",\"sha256\":\"" + sha + "\",\"signature\":\"" + sig + "\"}";
            return tamper == null ? json : tamper(json);
        }

        (MessageUpdater u, Settings s) Make(string manifestJson, byte[] list, string storedVersion = "", Func<string, Task<byte[]>> custom = null)
        {
            FetchFunc fetch = (url, max, deadline) =>
            {
                _calls.Add(url);
                if (custom != null) return custom(url);
                if (url == ManifestUrl) return Task.FromResult(Encoding.UTF8.GetBytes(manifestJson));
                if (url == ListUrl) return Task.FromResult(list);
                throw new InvalidOperationException("unexpected " + url);
            };
            var updater = new MessageUpdater(fetch, new ManifestVerifier(UpdateTestKit.PublicXml(_rsa)), Remote, () => _now);
            return (updater, new Settings { FetchMessages = true, RemoteMessagesVersion = storedVersion });
        }

        static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

        void WriteExisting(string text) => File.WriteAllText(Remote, text);

        async Task AssertUntouched(MessageUpdater u, Settings s, string storedVersion, string existingText, Func<Task<bool>> run)
        {
            Assert.False(await run());
            Assert.Equal(storedVersion, s.RemoteMessagesVersion);
            if (existingText == null) Assert.False(File.Exists(Remote)); else Assert.Equal(existingText, File.ReadAllText(Remote));
            Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        }

        [Fact] public async Task Valid_Update_Is_Applied_Verified_And_Recorded()
        {
            var list = B(GoodList("NEW"));
            var (u, s) = Make(SignedManifest("2", ListUrl, list), list, "1");
            WriteExisting(GoodList("OLD"));
            Assert.True(await u.CheckAsync(ManifestUrl, s, () => true));
            Assert.Equal("2", s.RemoteMessagesVersion);
            Assert.Equal(_now, s.LastMessageCheck);
            var bank = MessageBank.TryReadFile(Remote);
            Assert.NotNull(bank);
            Assert.Equal("Random NEW.", bank.Random[0]);
            Assert.Equal(new[] { ManifestUrl, ListUrl }, _calls.ToArray());
            Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        }

        [Fact] public async Task First_Update_With_No_Stored_Version_Is_Applied()
        {
            var list = B(GoodList("NEW"));
            var (u, s) = Make(SignedManifest("1.0.0", ListUrl, list), list);
            Assert.Equal("", s.RemoteMessagesVersion);
            Assert.False(File.Exists(Remote));
            Assert.True(await u.CheckAsync(ManifestUrl, s, () => true));
            Assert.Equal("1.0.0", s.RemoteMessagesVersion);
            Assert.True(File.Exists(Remote));
        }

        [Fact] public async Task Bad_Signature_Leaves_Everything_Untouched()
        {
            var list = B(GoodList());
            var m = SignedManifest("2", ListUrl, list, j => j.Replace("\"signature\":\"", "\"signature\":\"AAAA"));
            var (u, s) = Make(m, list, "1");
            WriteExisting(GoodList("OLD"));
            await AssertUntouched(u, s, "1", GoodList("OLD"), () => u.CheckAsync(ManifestUrl, s, () => true));
            Assert.Single(_calls);
        }

        [Fact] public async Task Signed_By_A_Different_Key_Is_Rejected()
        {
            var list = B(GoodList());
            using (var other = UpdateTestKit.NewKey())
            {
                var sha = UpdateTestKit.Sha(list);
                var sig = Convert.ToBase64String(other.SignData(B(MessageUpdater.SigningDomain + "|2|" + ListUrl + "|" + sha), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
                var m = "{\"version\":\"2\",\"url\":\"" + ListUrl + "\",\"sha256\":\"" + sha + "\",\"signature\":\"" + sig + "\"}";
                var (u, s) = Make(m, list, "1");
                await AssertUntouched(u, s, "1", null, () => u.CheckAsync(ManifestUrl, s, () => true));
            }
        }

        [Fact] public async Task Tampered_Version_Url_Or_Hash_Is_Rejected()
        {
            var list = B(GoodList());
            foreach (Func<string, string> tamper in new Func<string, string>[]
            {
                j => j.Replace("\"version\":\"2\"", "\"version\":\"3\""),
                j => j.Replace("v2/messages.json", "v2/other.json"),
                j => j.Replace("\"sha256\":\"", "\"sha256\":\"0"),
            })
            {
                _calls.Clear();
                var (u, s) = Make(SignedManifest("2", ListUrl, list, tamper), list, "1");
                await AssertUntouched(u, s, "1", null, () => u.CheckAsync(ManifestUrl, s, () => true));
            }
        }

        [Fact] public async Task Hash_Mismatch_Of_The_Downloaded_List_Is_Rejected()
        {
            var good = B(GoodList("GOOD"));
            var evil = B(GoodList("EVIL"));
            var (u, s) = Make(SignedManifest("2", ListUrl, good), evil, "1");
            WriteExisting(GoodList("OLD"));
            await AssertUntouched(u, s, "1", GoodList("OLD"), () => u.CheckAsync(ManifestUrl, s, () => true));
            Assert.Equal(2, _calls.Count);
        }

        [Fact] public async Task Oversize_List_Is_Rejected()
        {
            var big = B(GoodList() + new string(' ', 70 * 1024));
            var (u, s) = Make(SignedManifest("2", ListUrl, big), big, "1");
            await AssertUntouched(u, s, "1", null, () => u.CheckAsync(ManifestUrl, s, () => true));
        }

        [Fact] public async Task Oversize_Manifest_Is_Rejected()
        {
            var list = B(GoodList());
            var (u, s) = Make(SignedManifest("2", ListUrl, list) + new string(' ', 70 * 1024), list, "1");
            await AssertUntouched(u, s, "1", null, () => u.CheckAsync(ManifestUrl, s, () => true));
            Assert.Single(_calls);
        }

        [Fact] public async Task Fetches_Use_The_64KiB_Cap_And_A_Deadline()
        {
            var caps = new List<(long, TimeSpan)>();
            var list = B(GoodList());
            var manifest = SignedManifest("2", ListUrl, list);
            FetchFunc fetch = (url, max, deadline) => { caps.Add((max, deadline)); return Task.FromResult(url == ManifestUrl ? B(manifest) : list); };
            var u = new MessageUpdater(fetch, new ManifestVerifier(UpdateTestKit.PublicXml(_rsa)), Remote, () => _now);
            await u.CheckAsync(ManifestUrl, new Settings { FetchMessages = true }, () => true);
            Assert.Equal(2, caps.Count);
            Assert.All(caps, c => { Assert.Equal(64 * 1024, c.Item1); Assert.True(c.Item2 > TimeSpan.Zero && c.Item2 <= TimeSpan.FromMinutes(1)); });
        }

        [Theory]
        [InlineData("{\"scheduled\":[],\"random\":[\"a\"],\"cheeky\":[\"a\"],\"welcome\":[\"a\"],\"hello\":[\"a\"]}")]
        [InlineData("{\"random\":[\"a\"],\"cheeky\":[\"a\"],\"welcome\":[\"a\"],\"hello\":[\"a\"]}")]
        [InlineData("[\"just\",\"an\",\"array\"]")]
        [InlineData("not json at all")]
        [InlineData("{\"scheduled\":[{\"time\":\"08:00\",\"pool\":[\"a\"]}],\"random\":[\"a\"],\"cheeky\":[\"a\"],\"welcome\":[\"a\"],\"hello\":[\"a\"],\"run\":\"calc.exe\"}")]
        public async Task Wrong_Schema_Is_Rejected_Even_When_Correctly_Signed(string list)
        {
            var bytes = B(list);
            var (u, s) = Make(SignedManifest("2", ListUrl, bytes), bytes, "1");
            WriteExisting(GoodList("OLD"));
            await AssertUntouched(u, s, "1", GoodList("OLD"), () => u.CheckAsync(ManifestUrl, s, () => true));
        }

        [Fact] public async Task Overlong_Line_Is_Rejected_Even_When_Correctly_Signed()
        {
            var bytes = B(GoodList().Replace("Random A.", new string('x', 200)));
            var (u, s) = Make(SignedManifest("2", ListUrl, bytes), bytes, "1");
            await AssertUntouched(u, s, "1", null, () => u.CheckAsync(ManifestUrl, s, () => true));
        }

        [Fact] public async Task Control_Characters_Are_Rejected_Even_When_Correctly_Signed()
        {
            var bytes = B(GoodList().Replace("Random A.", "bad\\u0007bell"));
            var (u, s) = Make(SignedManifest("2", ListUrl, bytes), bytes, "1");
            await AssertUntouched(u, s, "1", null, () => u.CheckAsync(ManifestUrl, s, () => true));
        }

        [Fact] public async Task Invalid_Utf8_Is_Rejected()
        {
            var bytes = new byte[] { 0x7b, 0xff, 0xfe, 0x7d };
            var (u, s) = Make(SignedManifest("2", ListUrl, bytes), bytes, "1");
            await AssertUntouched(u, s, "1", null, () => u.CheckAsync(ManifestUrl, s, () => true));
        }

        [Fact] public async Task Http_Manifest_Url_Makes_No_Request()
        {
            var list = B(GoodList());
            var (u, s) = Make(SignedManifest("2", ListUrl, list), list, "1");
            Assert.False(await u.CheckAsync("http://example.com/messages.manifest.json", s, () => true));
            Assert.False(await u.CheckAsync("", s, () => true));
            Assert.False(await u.CheckAsync("garbage", s, () => true));
            Assert.Empty(_calls);
        }

        [Fact] public async Task Signed_Manifest_Pointing_At_An_Http_List_Is_Rejected()
        {
            var list = B(GoodList());
            var httpUrl = "http://example.com/messages.json";
            var (u, s) = Make(SignedManifest("2", httpUrl, list), list, "1");
            await AssertUntouched(u, s, "1", null, () => u.CheckAsync(ManifestUrl, s, () => true));
            Assert.Single(_calls);
        }

        [Fact] public async Task Server_Down_Is_Silent_And_Changes_Nothing()
        {
            var (u, s) = Make(null, null, "1", url => throw new HttpRequestException("down"));
            WriteExisting(GoodList("OLD"));
            await AssertUntouched(u, s, "1", GoodList("OLD"), () => u.CheckAsync(ManifestUrl, s, () => true));
        }

        [Fact] public async Task A_Server_That_Stalls_Is_Abandoned_At_The_Deadline()
        {
            var handler = new StallHandler();
            var fetcher = new HttpFetcher(handler);
            FetchFunc fetch = (url, max, deadline) => fetcher.FetchAsync(url, max, TimeSpan.FromMilliseconds(400));
            var u = new MessageUpdater(fetch, new ManifestVerifier(UpdateTestKit.PublicXml(_rsa)), Remote, () => _now);
            var s = new Settings { FetchMessages = true, RemoteMessagesVersion = "1" };
            WriteExisting(GoodList("OLD"));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Assert.False(await u.CheckAsync(ManifestUrl, s, () => true));
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(8), "took " + sw.Elapsed);
            Assert.Equal(GoodList("OLD"), File.ReadAllText(Remote));
            Assert.Equal("1", s.RemoteMessagesVersion);
        }

        class StallHandler : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            {
                await Task.Delay(Timeout.Infinite, ct);
                return null;
            }
        }

        [Theory]
        [InlineData("1", "1")] [InlineData("1.0", "1.0.0")] [InlineData("1.2.3", "1.2.4")] [InlineData("9", "10")] [InlineData("1.10", "1.10")]
        public async Task Replay_Of_The_Same_Or_An_Older_Version_Is_Rejected(string offered, string stored)
        {
            Assert.True(MessageVersion.Compare(offered, stored) <= 0, "this theory is only for offers that are not newer");
            var list = B(GoodList("REPLAY"));
            var (u, s) = Make(SignedManifest(offered, ListUrl, list), list, stored);
            WriteExisting(GoodList("CURRENT"));
            await AssertUntouched(u, s, stored, GoodList("CURRENT"), () => u.CheckAsync(ManifestUrl, s, () => true));
            Assert.Single(_calls);
        }

        [Theory]
        [InlineData("2", "1")] [InlineData("1.0.1", "1.0.0")] [InlineData("10", "9")] [InlineData("1.10", "1.9")] [InlineData("1", "")]
        public async Task A_Higher_Version_Is_Applied(string offered, string stored)
        {
            var list = B(GoodList("UP"));
            var (u, s) = Make(SignedManifest(offered, ListUrl, list), list, stored);
            Assert.True(await u.CheckAsync(ManifestUrl, s, () => true));
            Assert.Equal(offered, s.RemoteMessagesVersion);
        }

        [Theory]
        [InlineData("1000000")] [InlineData("1.2.3.4")] [InlineData("v2")] [InlineData("2-beta")] [InlineData("01")] [InlineData("")] [InlineData("1.2.1000000")]
        public async Task Malformed_Version_In_The_Manifest_Is_Rejected(string version)
        {
            var list = B(GoodList());
            var (u, s) = Make(SignedManifest(version, ListUrl, list), list, "1");
            await AssertUntouched(u, s, "1", null, () => u.CheckAsync(ManifestUrl, s, () => true));
        }

        [Fact] public async Task Interval_Is_24_Hours_And_Persisted_Before_The_Request()
        {
            var list = B(GoodList());
            DateTime? seenAtRequest = null;
            var persisted = new List<DateTime?>();
            var settings = new Settings { FetchMessages = true, RemoteMessagesVersion = "1" };
            FetchFunc fetch = (url, max, deadline) => { seenAtRequest = persisted.LastOrDefault(); throw new HttpRequestException("down"); };
            var u = new MessageUpdater(fetch, new ManifestVerifier(UpdateTestKit.PublicXml(_rsa)), Remote, () => _now);
            Assert.False(await u.CheckAsync(ManifestUrl, settings, () => { persisted.Add(settings.LastMessageCheck); return true; }));
            Assert.Equal(_now, seenAtRequest);

            // within 24 h: no request at all
            var calls = 0;
            FetchFunc again = (url, max, deadline) => { calls++; throw new HttpRequestException("down"); };
            var u2 = new MessageUpdater(again, new ManifestVerifier(UpdateTestKit.PublicXml(_rsa)), Remote, () => _now.AddHours(23));
            Assert.False(await u2.CheckAsync(ManifestUrl, settings, () => true));
            Assert.Equal(0, calls);
            var u3 = new MessageUpdater(again, new ManifestVerifier(UpdateTestKit.PublicXml(_rsa)), Remote, () => _now.AddHours(25));
            await u3.CheckAsync(ManifestUrl, settings, () => true);
            Assert.Equal(1, calls);
        }

        [Fact] public async Task When_The_Attempt_Cannot_Be_Saved_No_Request_Is_Made()
        {
            var list = B(GoodList());
            var (u, s) = Make(SignedManifest("2", ListUrl, list), list, "1");
            Assert.False(await u.CheckAsync(ManifestUrl, s, () => false));
            Assert.Empty(_calls);
        }

        [Fact] public async Task A_Clock_That_Went_Backwards_Is_Clamped_And_Defers_The_Check_A_Full_Day()
        {
            var list = B(GoodList());
            var (u, s) = Make(SignedManifest("2", ListUrl, list), list, "1");
            s.LastMessageCheck = _now.AddDays(30);
            var saves = 0;
            Assert.False(await u.CheckAsync(ManifestUrl, s, () => { saves++; return true; }));
            Assert.Empty(_calls);
            Assert.Equal(_now, s.LastMessageCheck);
            Assert.Equal(1, saves);

            // 23 hours later it is still too soon; after 24 hours it proceeds
            FetchFunc fetch = (url, max, d) => { _calls.Add(url); return Task.FromResult(url == ManifestUrl ? B(SignedManifest("2", ListUrl, list)) : list); };
            var later = new MessageUpdater(fetch, new ManifestVerifier(UpdateTestKit.PublicXml(_rsa)), Remote, () => _now.AddHours(23));
            Assert.False(await later.CheckAsync(ManifestUrl, s, () => true));
            Assert.Empty(_calls);
            var nextDay = new MessageUpdater(fetch, new ManifestVerifier(UpdateTestKit.PublicXml(_rsa)), Remote, () => _now.AddHours(25));
            Assert.True(await nextDay.CheckAsync(ManifestUrl, s, () => true));
        }
        [Fact] public async Task Disabled_Setting_Makes_No_Request()
        {
            var list = B(GoodList());
            var (u, s) = Make(SignedManifest("2", ListUrl, list), list, "1");
            s.FetchMessages = false;
            Assert.False(await u.CheckAsync(ManifestUrl, s, () => true));
            Assert.Empty(_calls);
        }

        [Fact] public async Task Only_One_Check_Runs_At_A_Time()
        {
            var release = new TaskCompletionSource<byte[]>();
            var (u, s) = Make(null, null, "1", url => release.Task);
            var first = u.CheckAsync(ManifestUrl, s, () => true);
            Assert.True(u.IsBusy);
            Assert.False(await u.CheckAsync(ManifestUrl, new Settings { FetchMessages = true }, () => true));
            Assert.Single(_calls);
            release.SetResult(new byte[0]);
            await first;
            Assert.False(u.IsBusy);
        }

        [Fact] public async Task The_Guard_Is_Released_After_A_Failure()
        {
            var (u, s) = Make(null, null, "1", url => throw new Exception("boom"));
            await u.CheckAsync(ManifestUrl, s, () => true);
            Assert.False(u.IsBusy);
        }

        [Fact] public async Task Never_Throws_Even_For_Null_Arguments()
        {
            var (u, _) = Make(null, null);
            Assert.False(await u.CheckAsync(null, null, null));
            Assert.False(await u.CheckAsync(ManifestUrl, null, null));
            Assert.False(await u.CheckAsync(ManifestUrl, new Settings { FetchMessages = true }, null));
        }

        [Fact] public async Task A_Previous_Remote_File_That_Is_Locked_Is_Left_As_It_Was()
        {
            var list = B(GoodList("NEW"));
            var (u, s) = Make(SignedManifest("2", ListUrl, list), list, "1");
            WriteExisting(GoodList("OLD"));
            using (new FileStream(Remote, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.False(await u.CheckAsync(ManifestUrl, s, () => true));
            }
            Assert.Equal(GoodList("OLD"), File.ReadAllText(Remote));
            // the replay floor is committed before the list, so it stays at the new version (monotonic) and the old list stays in place
            Assert.Equal("2", s.RemoteMessagesVersion);
            Assert.Equal("2", File.ReadAllText(Remote + ".version").Trim());
            Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        }

        [Fact] public async Task The_Folder_Is_Created_When_Missing()
        {
            var nested = Path.Combine(_dir, "sub", "deeper", "messages.remote.json");
            var list = B(GoodList());
            FetchFunc fetch = (url, max, d) => Task.FromResult(url == ManifestUrl ? B(SignedManifest("2", ListUrl, list)) : list);
            var u = new MessageUpdater(fetch, new ManifestVerifier(UpdateTestKit.PublicXml(_rsa)), nested, () => _now);
            Assert.True(await u.CheckAsync(ManifestUrl, new Settings { FetchMessages = true }, () => true));
            Assert.True(File.Exists(nested));
        }
    }
}

namespace UnicornPet.Tests
{
    public class MessageValidatorEmojiTests
    {
        static string Line(string text) => MessageValidator.CheckLine(text);

        [Theory]
        [InlineData("\U0001F970")] [InlineData("\U0001F973")] [InlineData("\U0001F98B")] [InlineData("\U0001F984")] [InlineData("\U0001F9A5")]
        [InlineData("\U0001FAE0")] [InlineData("\U0001F600")] [InlineData("\U0001F44D\U0001F3FD")] [InlineData("\U0001F1F5\U0001F1F1")]
        public void Emoji_Of_Any_Unicode_Version_Are_Accepted(string emoji)
        {
            Assert.Null(Line("Hi " + emoji + " there"));
        }

        [Fact] public void Zwj_And_Variation_Selector_Sequences_Are_Accepted()
        {
            Assert.Null(Line("Coding \U0001F469‍\U0001F4BB today"));
            Assert.Null(Line("Pride \U0001F3F3️‍\U0001F308"));
            Assert.Null(Line("Family \U0001F468‍\U0001F469‍\U0001F467‍\U0001F466"));
            Assert.Null(Line("Love ❤️ and sparkles ✨"));
        }

        [Fact] public void Stray_Zwj_Or_Variation_Selectors_Are_Rejected()
        {
            Assert.NotNull(Line("a‍b"));
            Assert.NotNull(Line("‍start"));
            Assert.NotNull(Line("end‍"));
            Assert.NotNull(Line("letter️"));
            Assert.NotNull(Line("a️b"));
            Assert.NotNull(Line("\U0001F984‍‍\U0001F984x‍"));
        }

        [Fact] public void Tag_Characters_And_Private_Use_Are_Rejected()
        {
            Assert.NotNull(Line("flag \U0001F3F4\U000E0067\U000E0062\U000E007F"));
            Assert.NotNull(Line("private \U000F0000"));
            Assert.NotNull(Line("private "));
        }

        [Fact] public void Unassigned_Bmp_Code_Points_Are_Rejected() => Assert.NotNull(Line("odd ͸ char"));

        [Fact] public void Bidi_And_Other_Format_Controls_Are_Rejected()
        {
            foreach (var c in new[] { "‪", "‫", "‬", "‭", "‮", "⁦", "⁧", "⁨", "⁩", "‎", "‏", "​", "‌", "⁠", "﻿", "­" })
                Assert.NotNull(Line("x" + c + "y"));
        }

        [Fact] public void Lone_Surrogates_Are_Rejected()
        {
            Assert.NotNull(Line("a\uD83Db"));
            Assert.NotNull(Line("a\uDC00b"));
            Assert.NotNull(Line("end\uD83D"));
        }

        [Fact] public void Combining_Marks_Are_Capped_At_Three()
        {
            Assert.Null(Line("é̂̃ ok"));
            Assert.NotNull(Line("é̂̃̄ zalgo"));
            Assert.Null(Line("é á b́ ć d́ spread out is fine"));
        }

        [Fact] public void Accents_And_Ordinary_Punctuation_Are_Fine()
            => Assert.Null(Line("Zoë, naïve café — “quoted” … £ €"));
    }
}

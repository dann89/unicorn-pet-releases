using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using UnicornPet.Core.Updater;
using Xunit;

namespace UnicornPet.Tests
{
    public class VersionIdTests
    {
        [Theory]
        [InlineData("1.2.3", "1.2.3.0")] [InlineData("1.2.3.4", "1.2.3.4")] [InlineData("01.2.3", "1.2.3.0")] [InlineData("10.0.0.0", "10.0.0.0")]
        public void Canonical_Valid(string input, string expected) => Assert.Equal(expected, VersionId.Canonical(input));

        [Theory]
        [InlineData(null)] [InlineData("")] [InlineData(".")] [InlineData("..")] [InlineData("1")] [InlineData("1.0")] [InlineData("1.0.0.0.0")]
        [InlineData("1.0.0-beta")] [InlineData("1.0.x")] [InlineData(" 1.0.0")] [InlineData("1.0.0 ")] [InlineData("..\\1.0.0")] [InlineData("1.0.0/..")]
        [InlineData("\u0661.\u0660.\u0660")] [InlineData("1,0,0")] [InlineData("9999999.0.0")] [InlineData("-1.0.0")]
        public void Canonical_Invalid(string input) => Assert.Null(VersionId.Canonical(input));

        [Fact] public void Canonical_FromVersion_PadsMissingParts()
        {
            Assert.Equal("1.2.0.0", VersionId.Canonical(new Version(1, 2)));
            Assert.Equal("1.2.3.0", VersionId.Canonical(new Version(1, 2, 3)));
        }
    }

    public class UpdateStagerTests : IDisposable
    {
        readonly string _root = UpdateTestKit.TempDir();
        readonly RSA _rsa = UpdateTestKit.NewKey();
        string Versions => Path.Combine(_root, "versions");
        string Fallback => Path.Combine(_root, "fallback");

        public UpdateStagerTests()
        {
            MakeDir("1.0.0.0");
            Stager().InitState("1.0.0.0");
        }

        public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

        UpdateStager Stager() => new UpdateStager(Versions);

        void MakeDir(string id, bool verified = false)
        {
            Directory.CreateDirectory(Path.Combine(Versions, id));
            File.WriteAllText(Path.Combine(Versions, id, "UnicornPet.exe"), "exe " + id);
            if (verified) File.WriteAllText(Path.Combine(Versions, id, UpdateStager.VerifiedMarker), id);
        }

        string Dir(string id) => Path.Combine(Versions, id);

        (Manifest m, byte[] zip) Release(string ver, string content = "v2")
        {
            var zip = UpdateTestKit.Zip(("UnicornPet.exe", content), ("Lib.dll", "lib"));
            return (UpdateTestKit.Signed(_rsa, ver, "https://example.com/" + ver + ".zip", zip), zip);
        }

        [Fact] public void ResolveLaunchDir_NoStateAndNoDirs_ReturnsFallback()
        {
            var empty = UpdateTestKit.TempDir();
            try { Assert.Equal(Fallback, new UpdateStager(Path.Combine(empty, "versions")).ResolveLaunchDir(Fallback)); }
            finally { Directory.Delete(empty, true); }
        }

        [Fact] public void ResolveLaunchDir_Current_ReturnsCurrentDir() => Assert.Equal(Dir("1.0.0.0"), Stager().ResolveLaunchDir(Fallback));

        [Fact] public void Stage_Valid_AppliesOnNextResolve()
        {
            var (m, zip) = Release("2.0.0");
            Assert.Equal(StageResult.Staged, Stager().Stage(m, zip));
            Assert.True(File.Exists(Path.Combine(Dir("2.0.0.0"), "UnicornPet.exe")));
            var st = Stager().ReadState();
            Assert.Equal("1.0.0.0", st.Current);
            Assert.Equal("2.0.0.0", st.Pending);
            Assert.Equal(Dir("2.0.0.0"), Stager().ResolveLaunchDir(Fallback));
            st = Stager().ReadState();
            Assert.Equal("2.0.0.0", st.Current);
            Assert.Equal("1.0.0.0", st.Previous);
            Assert.Null(st.Pending);
            Assert.NotNull(st.Trial);
        }

        [Fact] public void Stage_HashMismatch_NotApplied()
        {
            var (m, zip) = Release("2.0.0");
            Assert.Equal(StageResult.HashMismatch, Stager().Stage(m with { Sha256 = new string('0', 64) }, zip));
            Assert.False(Directory.Exists(Dir("2.0.0.0")));
            Assert.Null(Stager().ReadState().Pending);
            Assert.Equal(Dir("1.0.0.0"), Stager().ResolveLaunchDir(Fallback));
        }

        [Fact] public void Stage_TruncatedZip_NotApplied()
        {
            var (_, zip) = Release("2.0.0");
            var cut = new byte[zip.Length / 2];
            Array.Copy(zip, cut, cut.Length);
            var m = UpdateTestKit.Signed(_rsa, "2.0.0", "https://example.com/2.zip", cut);
            Assert.Equal(StageResult.BadArchive, Stager().Stage(m, cut));
            Assert.False(Directory.Exists(Dir("2.0.0.0")));
            Assert.Empty(Directory.GetDirectories(Versions, ".staging*"));
        }

        [Fact] public void Stage_ZipSlipEntry_Rejected()
        {
            var zip = UpdateTestKit.Zip(("UnicornPet.exe", "x"), ("../evil.txt", "boom"));
            var m = UpdateTestKit.Signed(_rsa, "2.0.0", "https://example.com/2.zip", zip);
            Assert.Equal(StageResult.BadArchive, Stager().Stage(m, zip));
            Assert.False(File.Exists(Path.Combine(Versions, "evil.txt")));
            Assert.False(File.Exists(Path.Combine(_root, "evil.txt")));
            Assert.False(Directory.Exists(Dir("2.0.0.0")));
        }

        [Fact] public void Stage_AbsolutePathEntry_Rejected()
        {
            var zip = UpdateTestKit.Zip(("UnicornPet.exe", "x"), ("C:/Windows/Temp/evil.txt", "boom"));
            var m = UpdateTestKit.Signed(_rsa, "2.0.0", "https://example.com/2.zip", zip);
            Assert.Equal(StageResult.BadArchive, Stager().Stage(m, zip));
            Assert.False(File.Exists(@"C:\Windows\Temp\evil.txt"));
        }

        [Fact] public void Stage_ZipWithoutExe_Rejected()
        {
            var zip = UpdateTestKit.Zip(("readme.txt", "x"));
            var m = UpdateTestKit.Signed(_rsa, "2.0.0", "https://example.com/2.zip", zip);
            Assert.Equal(StageResult.BadArchive, Stager().Stage(m, zip));
        }

        [Fact] public void Stage_SameVersionTwice_ReturnsAlreadyPresent()
        {
            var (m, zip) = Release("2.0.0");
            Assert.Equal(StageResult.Staged, Stager().Stage(m, zip));
            Assert.Equal(StageResult.Rejected, Stager().Stage(m, zip));
        }

        [Fact] public void Stage_OlderOrEqualToCurrent_Rejected()
        {
            var (m, zip) = Release("1.0.0");
            Assert.Equal(StageResult.Rejected, Stager().Stage(m, zip));
        }

        [Fact] public void Stage_OlderThanRunningVersion_Rejected()
        {
            var (m, zip) = Release("1.5.0");
            Assert.Equal(StageResult.Rejected, Stager().Stage(m, zip, new Version(2, 0, 0, 0)));
        }

        [Fact] public void ReplayedOlderManifest_WhileNewerPending_IsRejected()
        {
            var (m3, z3) = Release("3.0.0");
            var (m2, z2) = Release("2.0.0");
            Assert.Equal(StageResult.Staged, Stager().Stage(m3, z3));
            Assert.Equal(StageResult.Rejected, Stager().Stage(m2, z2));
            Assert.Equal("3.0.0.0", Stager().ReadState().Pending);
            Assert.False(Directory.Exists(Dir("2.0.0.0")));
        }

        [Fact] public void Stage_NewerPending_ReplacesOlderPending()
        {
            var (m2, z2) = Release("2.0.0");
            var (m3, z3) = Release("3.0.0");
            Stager().Stage(m2, z2);
            Assert.Equal(StageResult.Staged, Stager().Stage(m3, z3));
            Assert.Equal("3.0.0.0", Stager().ReadState().Pending);
            Assert.Equal(Dir("3.0.0.0"), Stager().ResolveLaunchDir(Fallback));
        }

        [Fact] public void PartialDirWithoutExe_AsPending_IsIgnoredOnResolve()
        {
            Directory.CreateDirectory(Dir("2.0.0.0"));
            File.WriteAllText(Path.Combine(Dir("2.0.0.0"), "half.dll"), "partial");
            File.WriteAllText(Path.Combine(_root, "state.json"), "{\"Current\":\"1.0.0.0\",\"Pending\":\"2.0.0.0\",\"Rejected\":[]}");
            Directory.CreateDirectory(Path.Combine(Versions, ".staging-3.0.0.0-abc"));
            Assert.Equal(Dir("1.0.0.0"), Stager().ResolveLaunchDir(Fallback));
            var st = Stager().ReadState();
            Assert.Equal("1.0.0.0", st.Current);
            Assert.Null(st.Pending);
        }

        [Fact] public void VerifiedExtractedDir_WithoutPending_IsReconciledAndPromoted()
        {
            MakeDir("2.0.0.0", verified: true);
            Assert.Equal(Dir("2.0.0.0"), Stager().ResolveLaunchDir(Fallback));
            Assert.Equal("2.0.0.0", Stager().ReadState().Current);
        }

        [Fact] public void UnverifiedExtractedDir_WithoutPending_IsNotPromoted()
        {
            MakeDir("2.0.0.0", verified: false);
            Assert.Equal(Dir("1.0.0.0"), Stager().ResolveLaunchDir(Fallback));
        }

        [Fact] public void ResolveLaunchDir_NewVersionCrashedTwice_RollsBackToPrevious()
        {
            var (m, zip) = Release("2.0.0");
            Stager().Stage(m, zip);
            Assert.Equal(Dir("2.0.0.0"), Stager().ResolveLaunchDir(Fallback));
            Assert.Equal(Dir("2.0.0.0"), Stager().ResolveLaunchDir(Fallback));
            Assert.Equal(Dir("1.0.0.0"), Stager().ResolveLaunchDir(Fallback));
            var st = Stager().ReadState();
            Assert.Equal("1.0.0.0", st.Current);
            Assert.Contains("2.0.0.0", st.Rejected);
            Assert.Null(st.Trial);
            Assert.False(Directory.Exists(Dir("2.0.0.0")));
            Assert.Equal(Dir("1.0.0.0"), Stager().ResolveLaunchDir(Fallback));
        }

        [Fact] public void RolledBackVersion_IsNotRetaken()
        {
            var (m, zip) = Release("2.0.0");
            Stager().Stage(m, zip);
            for (int i = 0; i < 3; i++) Stager().ResolveLaunchDir(Fallback);
            Assert.Equal(StageResult.Rejected, Stager().Stage(m, zip));
            Assert.Equal(Dir("1.0.0.0"), Stager().ResolveLaunchDir(Fallback));
        }

        [Fact] public void Rollback_StillEndsOnPrevious_WhenDeletingTheBadDirFails()
        {
            var (m, zip) = Release("2.0.0");
            Stager().Stage(m, zip);
            Stager().ResolveLaunchDir(Fallback);
            Stager().ResolveLaunchDir(Fallback);
            using (new FileStream(Path.Combine(Dir("2.0.0.0"), "UnicornPet.exe"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.Equal(Dir("1.0.0.0"), Stager().ResolveLaunchDir(Fallback));
            }
            var st = Stager().ReadState();
            Assert.Equal("1.0.0.0", st.Current);
            Assert.Contains("2.0.0.0", st.Rejected);
            Assert.Equal(Dir("1.0.0.0"), Stager().ResolveLaunchDir(Fallback));
        }

        [Fact] public void MarkLaunchOk_RightVersionAndToken_ClearsTrial_NoRollback()
        {
            var (m, zip) = Release("2.0.0");
            Stager().Stage(m, zip);
            var plan = Stager().ResolveLaunch(Fallback);
            Assert.Equal(Dir("2.0.0.0"), plan.Dir);
            Assert.False(string.IsNullOrEmpty(plan.Token));
            Assert.True(Stager().MarkLaunchOk("2.0.0.0", plan.Token));
            for (int i = 0; i < 6; i++) Assert.Equal(Dir("2.0.0.0"), Stager().ResolveLaunchDir(Fallback));
            Assert.Null(Stager().ReadState().Trial);
        }

        [Fact] public void MarkLaunchOk_WrongToken_DoesNotClearTrial()
        {
            var (m, zip) = Release("2.0.0");
            Stager().Stage(m, zip);
            var plan = Stager().ResolveLaunch(Fallback);
            Assert.False(Stager().MarkLaunchOk("2.0.0.0", "not-the-token"));
            Assert.False(Stager().MarkLaunchOk("2.0.0.0", ""));
            Assert.False(Stager().MarkLaunchOk("2.0.0.0", null));
            Assert.NotNull(Stager().ReadState().Trial);
            Assert.NotEqual(plan.Token, "not-the-token");
        }

        [Fact] public void MarkLaunchOk_WrongVersion_DoesNotClearTrial()
        {
            var (m, zip) = Release("2.0.0");
            Stager().Stage(m, zip);
            var plan = Stager().ResolveLaunch(Fallback);
            Assert.False(Stager().MarkLaunchOk("1.0.0.0", plan.Token));
            Assert.False(Stager().MarkLaunchOk("garbage", plan.Token));
            Assert.NotNull(Stager().ReadState().Trial);
        }

        [Fact] public void StaleToken_FromEarlierLaunch_CannotClearTrial()
        {
            var (m, zip) = Release("2.0.0");
            Stager().Stage(m, zip);
            var first = Stager().ResolveLaunch(Fallback);
            var second = Stager().ResolveLaunch(Fallback);
            Assert.NotEqual(first.Token, second.Token);
            Assert.False(Stager().MarkLaunchOk("2.0.0.0", first.Token));
            Assert.True(Stager().MarkLaunchOk("2.0.0.0", second.Token));
        }

        [Fact] public void MarkLaunchOk_PrunesOldVersionDirs_ButKeepsCurrentAndPrevious()
        {
            MakeDir("0.9.0.0");
            var (m, zip) = Release("2.0.0");
            Stager().Stage(m, zip);
            var plan = Stager().ResolveLaunch(Fallback);
            Stager().MarkLaunchOk("2.0.0.0", plan.Token);
            Assert.True(Directory.Exists(Dir("2.0.0.0")));
            Assert.True(Directory.Exists(Dir("1.0.0.0")));
            Assert.False(Directory.Exists(Dir("0.9.0.0")));
        }

        [Fact] public void CurrentDirMissingExe_FallsBackToPrevious()
        {
            var (m, zip) = Release("2.0.0");
            Stager().Stage(m, zip);
            var plan = Stager().ResolveLaunch(Fallback);
            Stager().MarkLaunchOk("2.0.0.0", plan.Token);
            File.Delete(Path.Combine(Dir("2.0.0.0"), "UnicornPet.exe"));
            Assert.Equal(Dir("1.0.0.0"), Stager().ResolveLaunchDir(Fallback));
        }

        [Fact] public void CorruptState_FallsBackToHighestValidDir()
        {
            MakeDir("1.5.0.0");
            Directory.CreateDirectory(Dir("9.9.9.9"));
            File.WriteAllText(Path.Combine(_root, "state.json"), "{{{ not json");
            Assert.Equal(Dir("1.5.0.0"), Stager().ResolveLaunchDir(Fallback));
            Assert.Equal("1.5.0.0", Stager().ReadState().Current);
        }

        [Fact] public void EmptyOrTruncatedState_IsTolerated()
        {
            foreach (var junk in new[] { "", "null", "[]", "{\"Current\":", "{\"Current\":5}" })
            {
                File.WriteAllText(Path.Combine(_root, "state.json"), junk);
                Assert.Equal(Dir("1.0.0.0"), Stager().ResolveLaunchDir(Fallback));
            }
        }

        [Fact] public void RejectedVersion_IsNeverSelected()
        {
            MakeDir("2.0.0.0");
            File.WriteAllText(Path.Combine(_root, "state.json"), "{\"Current\":\"2.0.0.0\",\"Previous\":\"1.0.0.0\",\"Rejected\":[\"2.0.0.0\"]}");
            Assert.Equal(Dir("1.0.0.0"), Stager().ResolveLaunchDir(Fallback));
            Assert.Equal("1.0.0.0", Stager().ReadState().Current);
        }

        [Fact] public void RejectedVersion_IsNeverPromotedFromPending()
        {
            MakeDir("2.0.0.0", verified: true);
            File.WriteAllText(Path.Combine(_root, "state.json"), "{\"Current\":\"1.0.0.0\",\"Pending\":\"2.0.0.0\",\"Rejected\":[\"2.0.0.0\"]}");
            Assert.Equal(Dir("1.0.0.0"), Stager().ResolveLaunchDir(Fallback));
        }

        [Fact] public void PendingOlderThanCurrent_IsNeverPromoted()
        {
            MakeDir("0.5.0.0", verified: true);
            File.WriteAllText(Path.Combine(_root, "state.json"), "{\"Current\":\"1.0.0.0\",\"Pending\":\"0.5.0.0\",\"Rejected\":[]}");
            Assert.Equal(Dir("1.0.0.0"), Stager().ResolveLaunchDir(Fallback));
            Assert.Null(Stager().ReadState().Pending);
        }

        [Fact] public void InvalidVersionNamesInState_AreDropped()
        {
            File.WriteAllText(Path.Combine(_root, "state.json"), "{\"Current\":\"..\",\"Previous\":\"..\\\\x\",\"Pending\":\"1.0\",\"Rejected\":[\"..\",\"junk\"]}");
            var plan = Stager().ResolveLaunch(Fallback);
            Assert.Equal(Dir("1.0.0.0"), plan.Dir);
            var st = Stager().ReadState();
            Assert.Equal("1.0.0.0", st.Current);
            Assert.Empty(st.Rejected);
        }

        [Fact] public void NonCanonicalFolderNames_AreNeverLaunched()
        {
            Directory.CreateDirectory(Path.Combine(Versions, "9.9.9"));
            File.WriteAllText(Path.Combine(Versions, "9.9.9", "UnicornPet.exe"), "x");
            File.WriteAllText(Path.Combine(_root, "state.json"), "garbage");
            Assert.Equal(Dir("1.0.0.0"), Stager().ResolveLaunchDir(Fallback));
        }

        [Fact] public void InitState_AfterReinstall_ClearsStalePendingAndTrial_KeepsRejected()
        {
            var (m, zip) = Release("2.0.0");
            Stager().Stage(m, zip);
            Stager().ResolveLaunch(Fallback);
            var (m3, z3) = Release("3.0.0");
            Stager().Stage(m3, z3);
            File.WriteAllText(Path.Combine(_root, "state.json"),
                "{\"Current\":\"2.0.0.0\",\"Previous\":\"1.0.0.0\",\"Pending\":\"3.0.0.0\",\"Trial\":{\"Version\":\"2.0.0.0\",\"Attempts\":1,\"Token\":\"t\"},\"Rejected\":[\"0.9.0.0\"]}");
            Assert.True(Stager().InitState("1.0.0.0"));
            var st = Stager().ReadState();
            Assert.Equal("1.0.0.0", st.Current);
            Assert.Null(st.Pending);
            Assert.Null(st.Trial);
            Assert.Null(st.Previous);
            Assert.Equal(new[] { "0.9.0.0" }, st.Rejected.ToArray());
            Assert.False(Directory.Exists(Dir("3.0.0.0")));
            Assert.False(Directory.Exists(Dir("2.0.0.0")));
            Assert.Equal(Dir("1.0.0.0"), Stager().ResolveLaunchDir(Fallback));
        }

        [Fact] public void InitState_RejectsInvalidVersion()
        {
            Assert.False(Stager().InitState("..\\evil"));
            Assert.False(Stager().InitState("1.0"));
            Assert.Equal("1.0.0.0", Stager().ReadState().Current);
        }

        [Fact] public void InitState_ForARejectedVersion_UnrejectsIt()
        {
            File.WriteAllText(Path.Combine(_root, "state.json"), "{\"Current\":\"1.0.0.0\",\"Rejected\":[\"2.0.0.0\"]}");
            Assert.True(Stager().InitState("2.0.0.0"));
            var st = Stager().ReadState();
            Assert.Equal("2.0.0.0", st.Current);
            Assert.DoesNotContain("2.0.0.0", st.Rejected);
        }

        [Fact] public void ConcurrentResolve_IsSerialised_NoLostUpdatesOrCorruption()
        {
            var (m, zip) = Release("2.0.0");
            Stager().Stage(m, zip);
            using (var gate = new ManualResetEventSlim(false))
            {
                var tasks = Enumerable.Range(0, 2).Select(_ => Task.Run(() => { gate.Wait(); return Stager().ResolveLaunchDir(Fallback); })).ToArray();
                gate.Set();
                Task.WaitAll(tasks);
                Assert.All(tasks, t => Assert.Equal(Dir("2.0.0.0"), t.Result));
            }
            var st = Stager().ReadState();
            Assert.Equal("2.0.0.0", st.Current);
            Assert.Equal(2, st.Trial.Attempts);
            Assert.Empty(Directory.GetFiles(_root, "state.json.*.tmp"));
        }

        [Fact] public void LockHeldElsewhere_ResolveGivesUp_ReturnsFallback_WithoutChangingState()
        {
            var (m, zip) = Release("2.0.0");
            Stager().Stage(m, zip);
            using (new FileStream(Path.Combine(_root, "state.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.Equal(Fallback, Stager().ResolveLaunchDir(Fallback));
            }
            Assert.Equal("2.0.0.0", Stager().ReadState().Pending);
        }
    }
}


using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnicornPet.Core.Updater;
using Xunit;

namespace UnicornPet.Tests
{
    public class ZipExtractorTests : IDisposable
    {
        readonly string _dest = UpdateTestKit.TempDir();
        public void Dispose() { try { Directory.Delete(_dest, true); } catch { } }

        void Extract(byte[] zip, long max = 200L * 1024 * 1024, int entries = 2000) => ZipExtractor.Extract(zip, _dest, max, entries);

        static byte[] Zip(params (string name, string content)[] e) => UpdateTestKit.Zip(e);

        [Fact] public void Valid_Zip_ExtractsEverything()
        {
            Extract(Zip(("UnicornPet.exe", "exe"), ("sub/Lib.dll", "lib"), ("Other.dll", "o")));
            Assert.Equal("exe", File.ReadAllText(Path.Combine(_dest, "UnicornPet.exe")));
            Assert.Equal("lib", File.ReadAllText(Path.Combine(_dest, "sub", "Lib.dll")));
        }

        [Fact] public void DuplicateNames_DifferingOnlyInCase_AreRejected()
        {
            Assert.Throws<InvalidDataException>(() => Extract(Zip(("UnicornPet.exe", "x"), ("Lib.dll", "a"), ("lib.dll", "b"))));
        }

        [Fact] public void ExactDuplicateNames_AreRejected()
        {
            Assert.Throws<InvalidDataException>(() => Extract(Zip(("UnicornPet.exe", "x"), ("a.dll", "1"), ("a.dll", "2"))));
        }

        [Fact] public void DuplicateAcrossSlashStyles_IsRejected()
        {
            Assert.Throws<InvalidDataException>(() => Extract(Zip(("UnicornPet.exe", "x"), ("sub/a.dll", "1"), ("SUB\\A.dll", "2"))));
        }

        [Theory]
        [InlineData("NUL.txt")] [InlineData("nul")] [InlineData("CON")] [InlineData("con.dll")] [InlineData("PRN.x.y")] [InlineData("AUX")]
        [InlineData("COM1")] [InlineData("com9.txt")] [InlineData("LPT1")] [InlineData("lpt9.dll")] [InlineData("sub/aux.txt")]
        [InlineData("CONIN$")] [InlineData("CONOUT$")] [InlineData("NUL .txt")]
        public void ReservedDeviceNames_AreRejected(string name)
        {
            Assert.Throws<InvalidDataException>(() => Extract(Zip(("UnicornPet.exe", "x"), (name, "boom"))));
        }

        [Theory]
        [InlineData("a.")] [InlineData("a ")] [InlineData("dir./file.dll")] [InlineData("dir /file.dll")] [InlineData("a.b.")]
        [InlineData("a:b")] [InlineData("a.txt:stream")] [InlineData("C:evil.txt")] [InlineData("x*y")] [InlineData("x?y")] [InlineData("x|y")]
        [InlineData("x\"y")] [InlineData("x<y")] [InlineData("a//b.dll")] [InlineData("./a.dll")] [InlineData("a/./b.dll")]
        public void BadComponents_AreRejected(string name)
        {
            byte[] zip;
            try { zip = Zip(("UnicornPet.exe", "x"), (name, "boom")); }
            catch (ArgumentException) { return; } // this runtime refuses to even write such an entry name; nothing to extract
            Assert.Throws<InvalidDataException>(() => Extract(zip));
        }

        [Theory]
        [InlineData("../evil.txt")] [InlineData("a/../../evil.txt")] [InlineData("/abs.txt")] [InlineData("\\abs.txt")] [InlineData("..\\evil.txt")]
        public void Traversal_IsRejected(string name)
        {
            Assert.Throws<InvalidDataException>(() => Extract(Zip(("UnicornPet.exe", "x"), (name, "boom"))));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(_dest), "evil.txt"));
        }

        [Fact] public void OverlongPath_IsRejected()
        {
            var name = string.Join("/", Enumerable.Repeat(new string('a', 60), 6)) + "/f.dll";
            Assert.Throws<InvalidDataException>(() => Extract(Zip(("UnicornPet.exe", "x"), (name, "x"))));
        }

        [Fact] public void FileThenDirectoryOfTheSameName_IsRejected()
        {
            Assert.Throws<InvalidDataException>(() => Extract(Zip(("UnicornPet.exe", "x"), ("thing", "file"), ("thing/inner.dll", "x"))));
        }

        [Fact] public void ExistingFileInDestination_IsNeverOverwritten()
        {
            File.WriteAllText(Path.Combine(_dest, "UnicornPet.exe"), "original");
            Assert.ThrowsAny<IOException>(() => Extract(Zip(("UnicornPet.exe", "replacement"))));
            Assert.Equal("original", File.ReadAllText(Path.Combine(_dest, "UnicornPet.exe")));
        }

        [Fact] public void AggregateByteCap_IsEnforced_AcrossEntries()
        {
            var big = new string('a', 1000);
            var zip = Zip(("UnicornPet.exe", big), ("a.dll", big), ("b.dll", big));
            Assert.Throws<InvalidDataException>(() => Extract(zip, max: 2500));
            var other = UpdateTestKit.TempDir();
            try { ZipExtractor.Extract(Zip(("UnicornPet.exe", big), ("a.dll", big)), other, 2500, 2000); }
            finally { try { Directory.Delete(other, true); } catch { } }
        }

        [Fact] public void EntryCountCap_IsEnforced()
        {
            var zip = Zip(("UnicornPet.exe", "x"), ("a.dll", "1"), ("b.dll", "2"));
            Assert.Throws<InvalidDataException>(() => Extract(zip, entries: 2));
        }

        static byte[] PatchUncompressedSize(byte[] zip, uint newSize)
        {
            var copy = (byte[])zip.Clone();
            // central directory headers: signature 0x02014b50, uncompressed size at offset 24
            for (int i = 0; i + 28 < copy.Length; i++)
            {
                if (copy[i] == 0x50 && copy[i + 1] == 0x4b && copy[i + 2] == 0x01 && copy[i + 3] == 0x02)
                    BitConverter.GetBytes(newSize).CopyTo(copy, i + 24);
            }
            return copy;
        }

        // .NET Core clamps a lying entry to its declared size while .NET Framework may not, so these assert the safety property
        // (never more than declared, never past the caps) rather than one particular failure mode.
        static long BytesIn(string dir) => Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);

        [Fact] public void HeaderUnderstatesTheRealSize_NeverWritesMoreThanDeclared()
        {
            var zip = Zip(("UnicornPet.exe", new string('x', 5000)));
            var lying = PatchUncompressedSize(zip, 10);
            try { Extract(lying); } catch (InvalidDataException) { }
            Assert.True(BytesIn(_dest) <= 10, "wrote " + BytesIn(_dest));
        }

        [Fact] public void HeaderOverstatesTheRealSize_IsRejected()
        {
            var zip = Zip(("UnicornPet.exe", "tiny"));
            var lying = PatchUncompressedSize(zip, 5000);
            Assert.ThrowsAny<Exception>(() => Extract(lying));
        }

        [Fact] public void LyingHeaders_CannotSneakPastTheAggregateCap()
        {
            var big = new string('a', 4000);
            var zip = Zip(("UnicornPet.exe", big), ("a.dll", big), ("b.dll", big));
            var lying = PatchUncompressedSize(zip, 10);
            try { Extract(lying, max: 6000); } catch (InvalidDataException) { }
            Assert.True(BytesIn(_dest) <= 6000, "wrote " + BytesIn(_dest));
        }

        [Fact] public void HonestHeaders_WithTotalOverTheCap_AreRejected()
        {
            var big = new string('a', 4000);
            var zip = Zip(("UnicornPet.exe", big), ("a.dll", big), ("b.dll", big));
            Assert.Throws<InvalidDataException>(() => Extract(zip, max: 6000));
        }

        [Fact] public void DirectoryEntry_WithData_IsRejected()
        {
            var ms = new MemoryStream();
            using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                z.CreateEntry("UnicornPet.exe");
                var d = z.CreateEntry("folder/");
                using (var w = new StreamWriter(d.Open())) w.Write("data in a directory entry");
            }
            Assert.ThrowsAny<Exception>(() => Extract(ms.ToArray()));
        }

        [Fact] public void StagerTreatsExtractorFailures_AsBadArchive()
        {
            var root = UpdateTestKit.TempDir();
            try
            {
                var versions = Path.Combine(root, "versions");
                Directory.CreateDirectory(Path.Combine(versions, "1.0.0.0"));
                File.WriteAllText(Path.Combine(versions, "1.0.0.0", "UnicornPet.exe"), "x");
                var st = new UpdateStager(versions);
                st.InitState("1.0.0.0");
                var rsa = UpdateTestKit.NewKey();
                foreach (var bad in new[] { "NUL.txt", "a.", "Lib.dll|lib.dll" })
                {
                    var entries = bad.Contains("|")
                        ? new[] { ("UnicornPet.exe", "x"), ("Lib.dll", "1"), ("lib.dll", "2") }
                        : new[] { ("UnicornPet.exe", "x"), (bad, "y") };
                    var zip = UpdateTestKit.Zip(entries);
                    var m = UpdateTestKit.Signed(rsa, "2.0.0", "https://example.com/2.zip", zip);
                    Assert.Equal(StageResult.BadArchive, st.Stage(m, zip));
                    Assert.False(Directory.Exists(Path.Combine(versions, "2.0.0.0")));
                    Assert.Empty(Directory.GetDirectories(versions, ".staging*"));
                }
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }
    }
}


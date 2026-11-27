using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnicornPet.Core;
using Xunit;

namespace UnicornPet.Tests
{
    /// <summary>The custom message format (pools may be left out, named sections), the five built-in sections, and how they are laid over the base list.</summary>
    public class CustomMessagesTests
    {
        static string RepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "UnicornPet.sln"))) dir = Path.GetDirectoryName(dir);
            return dir!;
        }

        static string Sample() => File.ReadAllText(Path.Combine(RepoRoot(), "docs", "examples", "messages.sample.json"));

        static string Resource(string name)
        {
            using (var s = typeof(MessageBank).Assembly.GetManifestResourceStream(name)!)
            using (var r = new StreamReader(s))
                return r.ReadToEnd();
        }

        static string? Err(string json, bool allowName = false) { CustomMessages m; return CustomMessageValidator.Validate(json, allowName, out m); }
        static CustomMessages Ok(string json, bool allowName = false) { CustomMessages m; Assert.Null(CustomMessageValidator.Validate(json, allowName, out m)); return m; }

        static string Section(string id, int lines, string title = "A") =>
            "{\"id\":\"" + id + "\",\"title\":\"" + title + "\",\"lines\":[" + string.Join(",", Enumerable.Range(0, lines).Select(i => "\"line " + i + "\"")) + "]}";

        // ---------- format ----------

        [Fact]
        public void SampleFile_Passes_WithTwoSections()
        {
            var m = Ok(Sample());
            Assert.Equal(4, m.Random.Count); Assert.Equal(2, m.Cheeky.Count); Assert.Null(m.Scheduled); Assert.Null(m.Welcome); Assert.Null(m.Hello);
            Assert.Equal(new[] { "banter", "pep" }, m.Sections.Select(s => s.Id));
            Assert.Equal("Office banter", m.Sections[0].Title);
            Assert.Equal(11, m.LineCount);
        }

        [Fact]
        public void SampleFile_IsRefused_ByTheSignedValidator()
        {
            MessageBank b;
            Assert.NotNull(MessageValidator.Validate(Sample(), out b));
        }

        [Fact]
        public void SignedList_WithSections_IsStillRefused()
        {
            var full = Resource("messages.default.json").Trim();
            MessageBank b;
            Assert.Null(MessageValidator.Validate(full, out b));   // the full list itself is fine...
            var withSections = full.Substring(0, full.LastIndexOf('}')) + ",\"sections\":[]}";
            Assert.NotNull(MessageValidator.Validate(withSections, out b));   // ...but 2.0.0 clients refuse a new key, so signed lists never carry one
        }

        [Fact]
        public void SectionsOnly_File_Passes()
        {
            var m = Ok("{\"sections\":[{\"id\":\"a\",\"title\":\"A\",\"lines\":[\"x\"]}]}");
            Assert.Null(m.Random); Assert.Null(m.Cheeky); Assert.Null(m.Scheduled);
            Assert.Single(m.Sections);
            Assert.Equal(1, m.LineCount);
        }

        [Fact]
        public void Empty_Object_IsRefused() => Assert.Equal("nothing to use", Err("{}"));

        [Fact]
        public void ScheduledSlots_AreRead_InTimeOrder()
        {
            var m = Ok("{\"scheduled\":[{\"id\":\"late\",\"time\":\"12:45\",\"pool\":[\"Lunch!\"]},{\"time\":\"08:00\",\"pool\":[\"Morning.\",\"Hi.\"]}]}");
            Assert.Equal(2, m.Scheduled.Count);
            Assert.Equal(new TimeSpan(8, 0, 0), m.Scheduled[0].Time);
            Assert.Equal("0800", m.Scheduled[0].Id);
            Assert.Equal("late", m.Scheduled[1].Id);
            Assert.Equal(3, m.LineCount);
        }

        [Theory]   // each is refused
        [InlineData("")]
        [InlineData("[]")]
        [InlineData("not json")]
        [InlineData("{\"random\":[\"a\"],\"section\":[]}")]                                              // unknown key
        [InlineData("{\"random\":[]}")]                                                                   // present but empty
        [InlineData("{\"random\":[\"{pet}\"]}")]                                                          // line rules
        [InlineData("{\"random\":[\"a\"],\"name\":7}")]
        [InlineData("{\"sections\":[{\"id\":\"a b\",\"title\":\"A\",\"lines\":[\"x\"]}]}")]             // id
        [InlineData("{\"sections\":[{\"id\":\"a\",\"title\":\"\",\"lines\":[\"x\"]}]}")]                // title
        [InlineData("{\"sections\":[{\"id\":\"a\",\"title\":\"Hi {name}\",\"lines\":[\"x\"]}]}")]       // no braces in a title
        [InlineData("{\"sections\":[{\"id\":\"a\",\"title\":\"A\",\"lines\":[]}]}")]
        [InlineData("{\"sections\":[{\"id\":\"a\",\"title\":\"A\",\"lines\":[\"x\"],\"extra\":1}]}")]
        [InlineData("{\"sections\":[{\"id\":\"a\",\"title\":\"A\",\"lines\":[\"x\"]},{\"id\":\"a\",\"title\":\"B\",\"lines\":[\"y\"]}]}")]   // duplicate id
        [InlineData("{\"sections\":{}}")]
        [InlineData("{\"sections\":[]}")]                                                                 // nothing to use
        [InlineData("{\"sections\":[{\"title\":\"A\",\"lines\":[\"x\"]}]}")]                             // id missing
        [InlineData("{\"scheduled\":[{\"time\":\"25:00\",\"pool\":[\"x\"]}]}")]
        [InlineData("{\"scheduled\":[]}")]
        public void Refused(string json) => Assert.NotNull(Err(json));

        [Fact]
        public void NineSections_AreRefused_EightPass()
        {
            string Doc(int n) => "{\"sections\":[" + string.Join(",", Enumerable.Range(0, n).Select(i => Section("s" + i, 1))) + "]}";
            Assert.Equal(8, Ok(Doc(8)).Sections.Count);
            Assert.NotNull(Err(Doc(9)));
        }

        [Fact]
        public void HundredAndOneLines_InASection_AreRefused_HundredPass()
        {
            Assert.Equal(100, Ok("{\"sections\":[" + Section("a", 100) + "]}").Sections[0].Lines.Count);
            Assert.NotNull(Err("{\"sections\":[" + Section("a", 101) + "]}"));
        }

        [Fact]
        public void Title_Of41Characters_IsRefused()
        {
            Assert.Equal(40, Ok("{\"sections\":[" + Section("a", 1, new string('t', 40)) + "]}").Sections[0].Title.Length);
            Assert.NotNull(Err("{\"sections\":[" + Section("a", 1, new string('t', 41)) + "]}"));
        }

        [Fact]
        public void Reasons_NeverQuoteTheFile()
        {
            Assert.DoesNotContain("s3cr3t", Err("{\"s3cr3t-key\":1}"));
            Assert.DoesNotContain("s3cr3t", Err("{\"scheduled\":[{\"time\":\"08:00\",\"pool\":[\"x\"],\"s3cr3t\":1}]}"));
            Assert.DoesNotContain("s3cr3t", Err("{\"sections\":[{\"id\":\"a\",\"title\":\"A\",\"lines\":[\"x\"],\"s3cr3t\":1}]}"));
        }

        [Fact]
        public void Name_IsKept_OnlyWhenAllowed_AndValid()
        {
            Assert.Equal("Alex", Ok("{\"name\":\"Alex\",\"random\":[\"a\"]}", true).Name);
            Assert.Equal("", Ok("{\"name\":\"Alex\",\"random\":[\"a\"]}", false).Name);
            Assert.Equal("", Ok("{\"name\":\"{name}\",\"random\":[\"a\"]}", true).Name);   // breaks the name rules: ignored, file still loads
        }

        // ---------- composing ----------

        static readonly IReadOnlyList<MessageSection> None = new MessageSection[0];

        [Fact]
        public void Compose_NothingToAdd_GivesTheBaseItself() => Assert.Same(MessageBank.Default, MessageComposer.Compose(MessageBank.Default, None, null, null));

        [Fact]
        public void Compose_ReplacesOnlyThePoolsInTheFile()
        {
            var m = Ok(Sample());
            var b = MessageComposer.Compose(MessageBank.Default, None, m, new[] { "banter", "pep" });
            Assert.Equal(m.Random, b.Random);
            Assert.Equal(m.Cheeky, b.Cheeky);
            Assert.Same(MessageBank.Default.Scheduled, b.Scheduled);
            Assert.Same(MessageBank.Default.Welcome, b.Welcome);
            Assert.Same(MessageBank.Default.Hello, b.Hello);
        }

        [Fact]
        public void Compose_SectionsAreOn_UnlessUnticked()
        {
            var m = Ok(Sample());
            var all = MessageComposer.Compose(MessageBank.Default, None, m, null);
            Assert.Equal(4 + 3 + 2, all.Random.Count);
            Assert.Equal(m.Random.Concat(m.Sections[0].Lines).Concat(m.Sections[1].Lines), all.Random);   // file order
            Assert.Equal(4 + 3, MessageComposer.Compose(MessageBank.Default, None, m, new[] { "pep" }).Random.Count);
        }

        [Fact]
        public void Compose_UnknownOffIds_AreIgnored_AndIdsAreCaseSensitive() =>
            Assert.Equal(4 + 3 + 2, MessageComposer.Compose(MessageBank.Default, None, Ok(Sample()), new[] { "gone", "BANTER" }).Random.Count);

        [Fact]
        public void Compose_SectionsOnlyFile_AddsToTheBaseRandom()
        {
            var m = Ok("{\"sections\":[{\"id\":\"a\",\"title\":\"A\",\"lines\":[\"x\",\"y\"]}]}");
            var b = MessageComposer.Compose(MessageBank.Default, None, m, null);
            Assert.Equal(MessageBank.Default.Random.Count + 2, b.Random.Count);
            Assert.Equal(MessageBank.Default.Random, b.Random.Take(MessageBank.Default.Random.Count));
            Assert.Same(MessageBank.Default.Cheeky, b.Cheeky);
        }

        [Fact]
        public void Compose_Name_FileNameWins_ElseBase()
        {
            var baseBank = MessageBank.Default.WithName("Sparkle");
            Assert.Equal("Alex", MessageComposer.Compose(baseBank, None, Ok("{\"name\":\"Alex\",\"random\":[\"a\"]}", true), null).Name);
            Assert.Equal("Sparkle", MessageComposer.Compose(baseBank, None, Ok("{\"random\":[\"a\"]}", true), null).Name);
            Assert.Equal("Sparkle", MessageComposer.Compose(baseBank, None, Ok("{\"name\":\"Alex\",\"random\":[\"a\"]}", false), null).Name);
        }

        // ---------- the five built-in sections ----------

        [Fact]
        public void BuiltIn_HasTheFiveSections_InOrder_WithTheirCounts()
        {
            Assert.Equal(new[] { "banter", "pep", "facts", "randomfacts", "roasts" }, BuiltInSections.All.Select(s => s.Id));
            Assert.Equal(new[] { "Office banter", "Pep talks", "Unicorn facts", "Random facts", "Office roasts" }, BuiltInSections.All.Select(s => s.Title));
            Assert.Equal(new[] { 10, 8, 5, 15, 21 }, BuiltInSections.All.Select(s => s.Lines.Count));
        }

        [Fact]
        public void BuiltIn_Resource_PassesTheCustomValidator_AndEveryLineRule()
        {
            CustomMessages m;
            Assert.Null(CustomMessageValidator.Validate(Resource("sections.default.json"), false, out m));
            foreach (var s in m.Sections) foreach (var l in s.Lines) Assert.Null(MessageValidator.CheckLine(l));
        }

        [Fact]
        public void BuiltIn_Text_IsVerbatim()
        {
            var all = BuiltInSections.All;
            Assert.Equal("Another meeting. Magical.", all[0].Lines[0]);
            Assert.Equal("I'm afraid you came in at 8:01. I'm not paying you now.", all[4].Lines[20]);
            Assert.Contains("{name}", all[1].Lines[0]);
        }

        [Fact]
        public void EmbeddedDefaults_StillHaveNoSections()
        {
            var text = Resource("messages.default.json");
            Assert.DoesNotContain("\"sections\"", text);
            MessageBank b;
            Assert.Null(MessageValidator.Validate(text, out b));
        }

        [Fact]
        public void BuiltIn_AllTicked_ByDefault() =>
            Assert.Equal(MessageBank.Default.Random.Count + 59, MessageComposer.Compose(MessageBank.Default, BuiltInSections.All, null, null).Random.Count);

        [Fact]
        public void BuiltIn_Unticked_StayOut() =>
            Assert.Equal(MessageBank.Default.Random.Count + 23, MessageComposer.Compose(MessageBank.Default, BuiltInSections.All, null, new[] { "roasts", "randomfacts" }).Random.Count);

        [Fact]
        public void Merge_SameId_ReplacesInPlace_NewIdsAppend()
        {
            var m = MessageComposer.MergeSections(BuiltInSections.All, Ok(Sample()));   // the sample has banter (3 lines) and pep (2 lines)
            Assert.Equal(new[] { "banter", "pep", "facts", "randomfacts", "roasts" }, m.Select(s => s.Id));
            Assert.Equal(3, m[0].Lines.Count); Assert.Equal(2, m[1].Lines.Count); Assert.Equal(5, m[2].Lines.Count);
        }

        [Fact]
        public void Merge_NewId_GoesLast()
        {
            var m = MessageComposer.MergeSections(BuiltInSections.All, Ok("{\"sections\":[{\"id\":\"team\",\"title\":\"Team\",\"lines\":[\"x\"]}]}"));
            Assert.Equal(6, m.Count);
            Assert.Equal("team", m[5].Id);
        }

        [Fact]
        public void Merge_NullCustom_IsTheBuiltInList() =>
            Assert.Equal(BuiltInSections.All.Select(s => s.Id), MessageComposer.MergeSections(BuiltInSections.All, null).Select(s => s.Id));

        [Fact]
        public void Merge_UntickedId_StaysOff_WhenTheFileReplacesThatSection()
        {
            var sample = Ok(Sample());
            var b = MessageComposer.Compose(MessageBank.Default, BuiltInSections.All, sample, new[] { "banter" });
            foreach (var line in sample.Sections[0].Lines) Assert.DoesNotContain(line, b.Random);
            foreach (var line in sample.Sections[1].Lines) Assert.Contains(line, b.Random);
        }
    }
}

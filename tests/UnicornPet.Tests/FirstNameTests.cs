using System;
using UnicornPet.Core;
using Xunit;

namespace UnicornPet.Tests
{
    public class FirstNameResolverTests
    {
        class Fake : IUserNameSource
        {
            public string DisplayName { get; set; } = "";
            public string AccountName { get; set; } = "";
            public bool Throw;
            string IUserNameSource.DisplayName => Throw ? throw new InvalidOperationException("boom") : DisplayName;
            string IUserNameSource.AccountName => Throw ? throw new InvalidOperationException("boom") : AccountName;
        }

        static string R(string display, string account = "") => FirstNameResolver.Resolve(new Fake { DisplayName = display, AccountName = account });

        [Theory]
        [InlineData("Alex Moreaux", "", "Alex")]
        [InlineData("Moreaux, Alex", "", "Alex")]
        [InlineData("ALEX MOREAUX", "", "Alex")]
        [InlineData("alex moreaux", "", "Alex")]
        [InlineData("dr Alex K", "", "Alex")]
        [InlineData("Dr. Alex Moreaux", "", "Alex")]
        [InlineData("Prof Alex Moreaux", "", "Alex")]
        [InlineData("Ms Alex Moreaux", "", "Alex")]
        [InlineData("Anne-Marie Jones", "", "Anne-Marie")]
        [InlineData("Zoë Ng", "", "Zoë")]
        [InlineData("Małgorzata Nowak", "", "Małgorzata")]
        [InlineData("Moreaux, Dr Alex", "", "Alex")]
        [InlineData("  Alex   Moreaux  ", "", "Alex")]
        [InlineData("", "alex.moreaux", "Alex")]
        [InlineData("", "CORP\\alex_moreaux", "Alex")]
        [InlineData("", "alex-moreaux", "Alex")]
        [InlineData("", "alex123", "Alex")]
        [InlineData("", "alex.moreaux@corp.example", "Alex")]
        [InlineData("", "amoreaux", "")]
        [InlineData("", "i.smith", "")]
        [InlineData("", "Administrator", "")]
        [InlineData("", "Owner", "")]
        [InlineData("", "", "")]
        [InlineData("", "   ", "")]
        public void Resolve_Table(string display, string account, string expected) => Assert.Equal(expected, R(display, account));

        [Theory]
        [InlineData("Admin"), InlineData("Administrator"), InlineData("User"), InlineData("Owner"), InlineData("Guest"), InlineData("Default"),
         InlineData("Test"), InlineData("Temp"), InlineData("Support"), InlineData("IT"), InlineData("Service"), InlineData("PC"),
         InlineData("Laptop"), InlineData("Desktop"), InlineData("root"), InlineData("Student")]
        public void GenericAccounts_AreRejected_AsDisplayOrAccount(string generic)
        {
            Assert.Equal("", R(generic, ""));
            Assert.Equal("", R("", generic));
            Assert.Equal("", R(generic + " Account", ""));
        }

        [Fact] public void SingleLetterAndDigits_Rejected()
        {
            Assert.Equal("", R("I Smith"));
            Assert.Equal("", R("J. Smith"));
            Assert.Equal("", R("Room 101"));
            Assert.Equal("", R("1234 5678"));
            Assert.Equal("", R("{name} X"));
            Assert.Equal("", R("<b>Alex</b> K"));
        }

        [Fact] public void DisplayName_WinsOver_Account() => Assert.Equal("Alex", R("Alex Moreaux", "sam.lee"));

        [Fact] public void BadDisplayName_FallsBackToAccount() => Assert.Equal("Alex", R("Administrator", "alex.moreaux"));

        [Fact] public void SourceThrows_ReturnsEmpty_NoCrash() => Assert.Equal("", FirstNameResolver.Resolve(new Fake { Throw = true }));

        [Fact] public void NullSource_ReturnsEmpty() => Assert.Equal("", FirstNameResolver.Resolve(null));
    }

    public class NameRulesTests
    {
        [Theory]
        [InlineData("Alex", "Alex")] [InlineData("  Alex  ", "Alex")] [InlineData("Zoë", "Zoë")] [InlineData("Anne-Marie", "Anne-Marie")]
        [InlineData("O'Brien", "O'Brien")] [InlineData("Mary Jane", "Mary Jane")] [InlineData("Mary    Jane", "Mary Jane")] [InlineData("", "")]
        [InlineData("   ", "")] [InlineData("Małgorzata", "Małgorzata")] [InlineData("O’Neil", "O’Neil")]
        public void Valid(string input, string expected)
        {
            Assert.True(NameRules.TryNormalize(input, out var n));
            Assert.Equal(expected, n);
        }

        [Theory]
        [InlineData("Alex1")] [InlineData("{name}")] [InlineData("Alex{name}")] [InlineData("<b>x</b>")] [InlineData("a<b")] [InlineData("Alex\nK")]
        [InlineData("Alex\t")] [InlineData("Alex_K")] [InlineData("Alex!")] [InlineData("--")] [InlineData("'")] [InlineData("Alex@work")] [InlineData("Alex/K")]
        [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        public void Invalid(string input)
        {
            Assert.False(NameRules.TryNormalize(input, out var n));
            Assert.Equal("", n);
        }

        [Fact] public void ThirtyChars_Allowed_ThirtyOne_Rejected()
        {
            Assert.True(NameRules.TryNormalize(new string('a', 30), out _));
            Assert.False(NameRules.TryNormalize(new string('a', 31), out _));
        }

        [Fact] public void NullInput_IsEmptyValid()
        {
            Assert.True(NameRules.TryNormalize(null, out var n));
            Assert.Equal("", n);
        }
    }

    public class NameProviderTests
    {
        static string R(string custom, string file, string detected, bool useWindows = true)
            => NameProvider.Resolve(new Settings { CustomName = custom, UseWindowsName = useWindows }, file, detected);

        [Fact] public void CustomName_BeatsEverything() => Assert.Equal("Casey", R("Casey", "Bob", "Alex"));
        [Fact] public void FileName_BeatsDetection() => Assert.Equal("Casey", R("", "Casey", "Alex"));
        [Fact] public void Detection_UsedWhenNothingElse() => Assert.Equal("Alex", R("", "", "Alex"));
        [Fact] public void UseWindowsNameFalse_SkipsDetection() => Assert.Equal("", R("", "", "Alex", false));
        [Fact] public void UseWindowsNameFalse_CustomStillWins() => Assert.Equal("Casey", R("Casey", "", "Alex", false));
        [Fact] public void UseWindowsNameFalse_FileStillWins() => Assert.Equal("Casey", R("", "Casey", "Alex", false));
        [Fact] public void AllEmpty_IsEmpty() => Assert.Equal("", R("", "", ""));
        [Fact] public void InvalidCustom_FallsThrough() => Assert.Equal("Alex", R("{name}", "", "Alex"));
        [Fact] public void InvalidFileName_FallsThrough() => Assert.Equal("Alex", R("", "<x>", "Alex"));
        [Fact] public void InvalidDetected_IsIgnored() => Assert.Equal("", R("", "", "Alex1"));
        [Fact] public void NullSettings_Safe() => Assert.Equal("Alex", NameProvider.Resolve(null, "", "Alex"));

        [Fact] public void Resolved_FlowsIntoMessagesCleanly()
        {
            var name = R("", "", "Alex");
            Assert.Equal("Good morning, Alex!", MessagePicker.Substitute("Good morning, {name}!", name));
            Assert.Equal("Good morning!", MessagePicker.Substitute("Good morning, {name}!", R("", "", "")));
        }

        [Fact] public void Scheduler_UsesUpdatedName()
        {
            var now = new DateTime(2026, 11, 23, 8, 0, 0);
            var bank = new MessageBank("", new[] { new ScheduledSlot("a", new TimeSpan(8, 0, 0), new[] { "Hi {name}!" }) }, new[] { "r" }, new[] { "c" });
            var s = new Scheduler(bank, () => now, new Random(1));
            s.UpdateName("Alex");
            Assert.Equal("Hi Alex!", s.Tick(new Settings()).Text);
        }

        [Fact] public void Settings_NewFields_RoundTrip()
        {
            var p = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "up-n-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var st = new SettingsStore(p);
                st.Save(new Settings { CustomName = "Zoë", UseWindowsName = false });
                var l = st.Load();
                Assert.Equal("Zoë", l.CustomName);
                Assert.False(l.UseWindowsName);
                System.IO.File.WriteAllText(p, "{\"CheekyMode\":false}");
                var old = st.Load();
                Assert.True(old.UseWindowsName);
                Assert.Equal("", old.CustomName);
            }
            finally { try { System.IO.File.Delete(p); } catch { } }
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnicornPet.Core;
using Xunit;

namespace UnicornPet.Tests
{
    public class NameSubstitutionTests
    {
        static IEnumerable<string> AllLines()
        {
            var b = MessageBank.Default;
            return b.Scheduled.SelectMany(s => s.Pool).Concat(b.Random).Concat(b.Cheeky).Concat(b.Welcome).Concat(b.Hello);
        }

        [Fact]
        public void EmptyName_RendersCleanly_ForEveryDefaultLine()
        {
            var withName = AllLines().Where(l => l.Contains("{name}")).ToList();
            Assert.NotEmpty(withName);
            foreach (var line in withName)
            {
                var r = MessagePicker.Substitute(line, "");
                Assert.DoesNotContain("{name}", r);
                Assert.DoesNotContain("  ", r);
                Assert.DoesNotMatch(new Regex(@"\s[,!.?;:]"), r);
                Assert.DoesNotMatch(new Regex(@",\s*[!.?]"), r);
                Assert.DoesNotMatch(new Regex(@"^[\s,]"), r);
                Assert.Equal(r.Trim(), r);
            }
        }

        [Fact]
        public void EmptyName_GoodMorning_BecomesGoodMorning()
        {
            Assert.Equal("Good morning! Today is going to be a good one.",
                MessagePicker.Substitute("Good morning, {name}! Today is going to be a good one.", ""));
            Assert.Equal("Hello there.", MessagePicker.Substitute("Hello {name} there.".Replace(" {name} there", " there"), ""));
        }

        [Theory]
        [InlineData("Good morning, {name}!", "Good morning!")]
        [InlineData("{name}, you did great.", "you did great.")]
        [InlineData("Well {name} that was fun.", "Well that was fun.")]
        [InlineData("Well, {name}, that was fun.", "Well, that was fun.")]
        [InlineData("Go home, {name}.", "Go home.")]
        public void EmptyName_MidSentencePatterns(string input, string expected)
        {
            var r = MessagePicker.Substitute(input, "");
            Assert.Equal(expected, r);
        }

        [Fact]
        public void WithName_IsSubstituted()
        {
            Assert.Equal("Good morning, Alex!", MessagePicker.Substitute("Good morning, {name}!", "Alex"));
        }
    }
}


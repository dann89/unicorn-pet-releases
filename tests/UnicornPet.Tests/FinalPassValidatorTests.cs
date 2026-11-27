using System;
using System.Linq;
using UnicornPet.Core;
using Xunit;

namespace UnicornPet.Tests
{
    public class FinalPassValidatorTests
    {
        [Theory]
        [InlineData(0x034F)] [InlineData(0x115F)] [InlineData(0x1160)] [InlineData(0x3164)] [InlineData(0xFFA0)]
        [InlineData(0x180B)] [InlineData(0x180C)] [InlineData(0x180D)] [InlineData(0x180E)] [InlineData(0x180F)]
        [InlineData(0x17B4)] [InlineData(0xFDD0)] [InlineData(0xFFF0)] [InlineData(0x2065)]
        [InlineData(0x1FFFE)] [InlineData(0x10FFFF)] [InlineData(0x13430)] [InlineData(0x1D173)] [InlineData(0xE0001)]
        public void Invisible_Or_Default_Ignorable_Code_Points_Are_Refused(int cp)
        {
            Assert.NotNull(MessageValidator.CheckLine("Hi " + char.ConvertFromUtf32(cp) + " there"));
        }

        [Fact] public void Mc_Marks_Count_Toward_The_Combining_Run_Cap_And_Do_Not_Reset_It()
        {
            // a base letter, then Mn + Mc + Mn + Mc (four marks in one run) must be refused
            Assert.NotNull(MessageValidator.CheckLine("áः́ः"));
            Assert.Null(MessageValidator.CheckLine("áः́"));      // three is still fine
        }

        [Fact] public void A_Supplementary_Unassigned_Code_Point_Outside_The_Emoji_Blocks_Is_Refused()
        {
            Assert.NotNull(MessageValidator.CheckLine("x \U00013430 y"));
            Assert.NotNull(MessageValidator.CheckLine("x \U0002FFFD y".Replace("\U0002FFFD", "\U0003FFFD")));
        }

        [Fact] public void Emoji_In_The_Emoji_Blocks_Stay_Allowed()
        {
            Assert.Null(MessageValidator.CheckLine("Well done \U0001F984\U0001F680\U0001FAE0!"));
        }

        [Theory]
        [InlineData("Call at 1⃣ now")]
        [InlineData("Press 1️⃣ now")]
        [InlineData("# #️⃣ and *️⃣ and 0️⃣")]
        public void Keycap_Sequences_Are_Accepted(string line) => Assert.Null(MessageValidator.CheckLine(line));

        [Fact] public void A_Variation_Selector_After_A_Digit_Without_The_Keycap_Is_Refused()
        {
            Assert.NotNull(MessageValidator.CheckLine("Press 1️ now"));
        }

        [Fact] public void Each_Name_Token_Counts_As_Thirty_Characters()
        {
            var ok = string.Concat(Enumerable.Repeat("{name}", 4));              // 4 * 30 = 120
            Assert.Null(MessageValidator.CheckLine(ok));
            var tooLong = string.Concat(Enumerable.Repeat("{name}", 23));         // 138 raw characters, 690 worst case
            Assert.NotNull(MessageValidator.CheckLine(tooLong));
            Assert.NotNull(MessageValidator.CheckLine(string.Concat(Enumerable.Repeat("{name}", 5)))); // 150
        }

        [Fact] public void Names_Are_Composed_To_NFC_Before_They_Are_Judged()
        {
            string n;
            Assert.True(NameRules.TryNormalize("José", out n));
            Assert.Equal("José", n);
            Assert.False(NameRules.TryNormalize("Jo\uD800e", out n)); // a lone surrogate is refused, not thrown
        }
    }
}

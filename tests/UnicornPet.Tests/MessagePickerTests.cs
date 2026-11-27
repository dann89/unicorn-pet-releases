using System;
using System.Collections.Generic;
using UnicornPet.Core;
using Xunit;

namespace UnicornPet.Tests
{
    public class MessagePickerTests
    {
        [Fact]
        public void Pick_NeverReturnsAvoid_WhenPoolHasMoreThanOne()
        {
            var pool = new List<string> { "a", "b", "c" };
            for (int seed = 0; seed < 50; seed++)
            {
                var p = new MessagePicker(new Random(seed));
                for (int i = 0; i < 30; i++) Assert.NotEqual("b", p.Pick(pool, "b"));
            }
        }

        [Fact]
        public void Pick_SingleItemPool_ReturnsIt()
        {
            Assert.Equal("only", new MessagePicker(new Random(1)).Pick(new List<string> { "only" }, "only"));
        }

        [Fact]
        public void PickRandom_CheekyFalse_NeverReturnsCheekyLine()
        {
            var bank = MessageBank.Default;
            var cheeky = new HashSet<string>(bank.Cheeky);
            var p = new MessagePicker(new Random(7));
            for (int i = 0; i < 500; i++) Assert.DoesNotContain(p.PickRandom(bank, false), cheeky);
        }

        [Fact]
        public void PickRandom_CheekyTrue_CanReturnCheekyLine()
        {
            var bank = MessageBank.Default;
            var cheeky = new HashSet<string>(bank.Cheeky);
            var p = new MessagePicker(new Random(7));
            bool seen = false;
            for (int i = 0; i < 500 && !seen; i++) seen = cheeky.Contains(p.PickRandom(bank, true));
            Assert.True(seen);
        }
    }
}

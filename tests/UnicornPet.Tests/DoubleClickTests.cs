using System;
using System.IO;
using UnicornPet.Core;
using Xunit;

namespace UnicornPet.Tests
{
    // She talks on a double-click. Counted by time and screen position, so it still works while she walks under the pointer.
    public class DoubleClickTests
    {
        static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);
        static DoubleClick New() => new DoubleClick(Ms(500), 8);

        [Fact]
        public void OneClick_IsNotADoubleClick() => Assert.False(New().Click(Ms(1000), 100, 100));

        [Fact]
        public void TwoQuickClicksInOnePlace_AreADoubleClick()
        {
            var d = New();
            d.Click(Ms(1000), 100, 100);
            Assert.True(d.Click(Ms(1500), 108, 92));
        }

        [Fact]
        public void TooSlow_StartsAgain()
        {
            var d = New();
            d.Click(Ms(1000), 100, 100);
            Assert.False(d.Click(Ms(1501), 100, 100));
            Assert.True(d.Click(Ms(1700), 100, 100));
        }

        [Fact]
        public void TooFarApart_IsNotADoubleClick()
        {
            var d = New();
            d.Click(Ms(1000), 100, 100);
            Assert.False(d.Click(Ms(1100), 109, 100));
        }

        [Fact]
        public void AThirdClick_DoesNotCountTwice()
        {
            var d = New();
            d.Click(Ms(1000), 100, 100);
            Assert.True(d.Click(Ms(1100), 100, 100));
            Assert.False(d.Click(Ms(1200), 100, 100));
            Assert.True(d.Click(Ms(1300), 100, 100));
        }

        [Fact]
        public void Reset_ForgetsTheFirstClick()   // a drag in between
        {
            var d = New();
            d.Click(Ms(1000), 100, 100);
            d.Reset();
            Assert.False(d.Click(Ms(1100), 100, 100));
        }

        [Fact]
        public void Window_TalksOnlyOnADoubleClick()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "UnicornPet.sln"))) dir = Path.GetDirectoryName(dir);
            var src = File.ReadAllText(Path.Combine(dir!, "src", "UnicornPet.App", "PetWindow.xaml.cs"));
            Assert.Contains("if (_doubleClick.Click(_clock.Elapsed, _downScreen.X, _downScreen.Y)) Clicked?.Invoke();", src);
            Assert.DoesNotContain("if (click) Clicked?.Invoke();", src);
        }
    }
}

using UnicornPet.Core;
using UnicornPet.Core.MiniGame;
using Xunit;

namespace UnicornPet.Tests
{
    public class MiniGameHighScoreTests
    {
        [Fact]
        public void Submit_Higher_UpdatesAndReturnsTrue()
        {
            var s = new Settings { GameHighScore = 100 };
            Assert.True(HighScore.Submit(s, 101));
            Assert.Equal(101, s.GameHighScore);
        }

        [Fact]
        public void Submit_EqualOrLower_ReturnsFalse_AndKeepsValue()
        {
            var s = new Settings { GameHighScore = 100 };
            Assert.False(HighScore.Submit(s, 100));
            Assert.False(HighScore.Submit(s, 3));
            Assert.Equal(100, s.GameHighScore);
        }

        [Fact]
        public void Submit_AboveMax_StoresMax()
        {
            var s = new Settings();
            Assert.True(HighScore.Submit(s, int.MaxValue));
            Assert.Equal(HighScore.Max, s.GameHighScore);
            Assert.False(HighScore.Submit(s, HighScore.Max + 1));
        }

        [Fact]
        public void Submit_Negative_ReturnsFalse()
        {
            var s = new Settings();
            Assert.False(HighScore.Submit(s, -5));
            Assert.Equal(0, s.GameHighScore);
        }

        [Fact]
        public void Submit_NullSettings_ReturnsFalse()
        {
            Assert.False(HighScore.Submit(null!, 10));
        }
    }
}

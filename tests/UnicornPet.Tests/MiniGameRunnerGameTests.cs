using System.Collections.Generic;
using System.Linq;
using UnicornPet.Core.MiniGame;
using Xunit;

namespace UnicornPet.Tests
{
    public class MiniGameRunnerGameTests
    {
        static RunnerGame Started(uint seed)
        {
            var g = new RunnerGame(new Lcg(seed).Next);
            g.Start();
            return g;
        }

        static string State(RunnerGame g) =>
            string.Join("|", new object[] { g.Phase, g.Steps, g.Distance, g.Score, g.Speed, g.Runner.Y, g.Runner.VelocityY, g.StarsCollected,
                string.Join(",", g.Obstacles.Select(o => o.Kind + "@" + o.X.ToString("R"))), string.Join(",", g.Stars.Select(s => s.X.ToString("R") + "/" + s.Y.ToString("R"))) });

        [Fact]
        public void NewGame_IsReady_StepChangesNothing()
        {
            var g = new RunnerGame(new Lcg(1).Next);
            Assert.Equal(GamePhase.Ready, g.Phase);
            var before = State(g);
            for (int i = 0; i < 50; i++) Assert.Equal(GameEvents.None, g.Step(new GameInput { JumpPressed = true }));
            Assert.Equal(before, State(g));
        }

        [Fact]
        public void PrimaryAction_Ready_Starts_AndReturnsTrue()
        {
            var g = new RunnerGame(new Lcg(1).Next);
            Assert.True(g.PrimaryAction());
            Assert.Equal(GamePhase.Running, g.Phase);
            Assert.True(g.Runner.OnGround); // the start input does not jump
        }

        [Fact]
        public void PrimaryAction_Running_ReturnsFalse()
        {
            var g = Started(1);
            Assert.False(g.PrimaryAction());
            Assert.Equal(GamePhase.Running, g.Phase);
        }

        [Fact]
        public void Pause_FreezesEverything_ResumeContinues()
        {
            var g = Started(2);
            ScriptedPlayer.Play(g, 400);
            Assert.Equal(GamePhase.Running, g.Phase);
            g.Pause();
            Assert.Equal(GamePhase.Paused, g.Phase);
            var atPause = State(g);
            for (int i = 0; i < 100; i++) Assert.Equal(GameEvents.None, g.Step(new GameInput { JumpPressed = true, DuckHeld = true }));
            Assert.Equal(atPause, State(g));
            Assert.True(g.PrimaryAction());
            Assert.Equal(GamePhase.Running, g.Phase);
            int steps = g.Steps;
            g.Step(new GameInput());
            Assert.Equal(steps + 1, g.Steps);
        }

        [Fact]
        public void Pause_And_Resume_OutsideTheirPhase_DoNothing()
        {
            var g = new RunnerGame(new Lcg(1).Next);
            g.Pause();
            Assert.Equal(GamePhase.Ready, g.Phase);
            g.Resume();
            Assert.Equal(GamePhase.Ready, g.Phase);
        }

        [Fact]
        public void NoInput_RunEndsBetweenStep150And180()
        {
            for (uint seed = 1; seed <= 20; seed++)
            {
                var g = Started(seed);
                int n = 0;
                while (g.Phase == GamePhase.Running && n < 1000) { g.Step(new GameInput()); n++; }
                Assert.Equal(GamePhase.Over, g.Phase);
                Assert.InRange(n, 150, 180);
            }
        }

        static RunnerGame Bumped()
        {
            var g = Started(3);
            GameEvents e = GameEvents.None;
            while (g.Phase == GamePhase.Running) e = g.Step(new GameInput());
            Assert.True((e & GameEvents.Bumped) != 0);
            Assert.True((e & GameEvents.Milestone) == 0);
            return g;
        }

        [Fact]
        public void Bump_SetsOver_ScoreStopsChanging()
        {
            var g = Bumped();
            Assert.Equal(GamePhase.Over, g.Phase);
            var at = State(g);
            int score = g.Score;
            for (int i = 0; i < 100; i++) Assert.Equal(GameEvents.None, g.Step(new GameInput { JumpPressed = true }));
            Assert.Equal(score, g.Score);
            Assert.Equal(at, State(g));
        }

        [Fact]
        public void Over_RestartLockedFor36Steps()
        {
            var g = Bumped();
            for (int i = 0; i < GameTuning.RestartLockSteps - 1; i++)
            {
                Assert.False(g.CanRestart);
                Assert.True(g.PrimaryAction());
                Assert.Equal(GamePhase.Over, g.Phase);
                g.Step(new GameInput());
            }
            Assert.False(g.CanRestart);
            g.Step(new GameInput());
            Assert.True(g.CanRestart);
            Assert.Equal(GameTuning.RestartLockSteps, g.StepsSinceBump);
            for (int i = 0; i < 50; i++) g.Step(new GameInput());
            Assert.Equal(GameTuning.RestartLockSteps, g.StepsSinceBump); // stops counting
            Assert.True(g.PrimaryAction());
            Assert.Equal(GamePhase.Running, g.Phase);
        }

        [Fact]
        public void Start_AfterOver_ResetsScoreDistanceObstaclesStarsRunner()
        {
            var g = Started(4);
            ScriptedPlayer.Play(g, 900);
            for (int i = 0; i < 2000 && g.Phase == GamePhase.Running; i++) g.Step(new GameInput());
            Assert.Equal(GamePhase.Over, g.Phase);
            Assert.True(g.Score > 0);
            g.Start();
            Assert.Equal(GamePhase.Running, g.Phase);
            Assert.Equal(0, g.Score);
            Assert.Equal(0, g.Distance);
            Assert.Equal(0, g.Steps);
            Assert.Equal(0, g.StarsCollected);
            Assert.Empty(g.Obstacles);
            Assert.Empty(g.Stars);
            Assert.True(g.Runner.OnGround);
            Assert.Equal(0, g.Runner.Y);
            Assert.Equal(GameTuning.StartSpeed, g.Speed);
            Assert.False(g.CanRestart);
        }

        [Fact]
        public void Score_IsDistanceOver10_Plus10PerStar()
        {
            var g = Started(5);
            for (int i = 0; i < 3000 && g.Phase == GamePhase.Running; i++)
            {
                g.Step(ScriptedPlayer.Input(g));
                Assert.Equal((int)(g.Distance / 10) + 10 * g.StarsCollected, g.Score);
            }
            Assert.True(g.StarsCollected > 0);
        }

        [Fact]
        public void Milestone_FiresOncePer500()
        {
            var g = Started(6);
            int milestones = ScriptedPlayer.Play(g, 3600, 0, GameEvents.Milestone);
            Assert.Equal(GamePhase.Running, g.Phase);
            Assert.True(g.Score >= 1000);
            Assert.Equal(g.Score / 500, milestones);
        }

        [Fact]
        public void Star_Collected_IsRemovedAndCounted()
        {
            var g = Started(7);
            for (int i = 0; i < 5000 && g.Phase == GamePhase.Running; i++)
            {
                int stars = g.StarsCollected;
                var e = g.Step(ScriptedPlayer.Input(g));
                if ((e & GameEvents.StarCollected) == 0) { Assert.Equal(stars, g.StarsCollected); continue; }
                Assert.Equal(stars + 1, g.StarsCollected);
                Assert.DoesNotContain(g.Stars, s => s.Box.Intersects(g.Runner.Box));
                return;
            }
            Assert.Fail("no star collected");
        }

        [Fact]
        public void SameSeedSameInputs_GiveIdenticalRuns()
        {
            var a = Started(7);
            var b = Started(7);
            ScriptedPlayer.Play(a, 3000);
            ScriptedPlayer.Play(b, 3000);
            Assert.Equal(a.Score, b.Score);
            Assert.Equal(a.Distance, b.Distance);
            Assert.Equal(a.Steps, b.Steps);
            Assert.Equal(a.Obstacles.Count, b.Obstacles.Count);
            for (int i = 0; i < a.Obstacles.Count; i++)
            {
                Assert.Equal(a.Obstacles[i].Kind, b.Obstacles[i].Kind);
                Assert.Equal(a.Obstacles[i].X, b.Obstacles[i].X, 9);
            }
        }

        // The fairness proof: a simple player survives every one of 300 seeded three-minute courses, also when its jump comes
        // 0.12 s early or 0.12 s late. (Checked first with a Python model of exactly these rules.)
        [Theory]
        [InlineData(0.0)]
        [InlineData(0.12)]
        [InlineData(-0.12)]
        public void ScriptedPlayer_SurvivesEverySeed(double shift)
        {
            var failed = new List<string>();
            for (uint seed = 1; seed <= 300; seed++)
            {
                var g = Started(seed);
                ScriptedPlayer.Play(g, 10800, shift);
                if (g.Phase != GamePhase.Running || g.Steps != 10800) failed.Add(seed + "@" + g.Steps);
            }
            Assert.True(failed.Count == 0, "bumped: " + string.Join(", ", failed));
        }

        [Fact]
        public void ScriptedPlayer_ScoreAfter30s_IsBetween700And850()
        {
            for (uint seed = 1; seed <= 10; seed++)
            {
                var g = Started(seed);
                ScriptedPlayer.Play(g, 1800);
                Assert.Equal(GamePhase.Running, g.Phase);
                Assert.InRange(g.Score, 700, 850);
            }
        }

        [Fact]
        public void Speed_FollowsDifficulty_AndNeverExceedsMax()
        {
            var g = Started(8);
            for (int i = 0; i < 6000 && g.Phase == GamePhase.Running; i++)
            {
                g.Step(ScriptedPlayer.Input(g));
                Assert.Equal(Difficulty.Speed(g.Seconds), g.Speed);
                Assert.True(g.Speed <= GameTuning.MaxSpeed);
            }
            Assert.Equal(GameTuning.MaxSpeed, g.Speed);
        }

        [Fact]
        public void ObstacleList_StaysSmall()
        {
            var g = Started(9);
            for (int i = 0; i < 10800 && g.Phase == GamePhase.Running; i++)
            {
                g.Step(ScriptedPlayer.Input(g));
                Assert.True(g.Obstacles.Count <= 6);
                Assert.True(g.Stars.Count <= 6);
            }
            Assert.Equal(GamePhase.Running, g.Phase);
        }
    }
}

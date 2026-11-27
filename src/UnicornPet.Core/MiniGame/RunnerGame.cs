using System;
using System.Collections.Generic;

namespace UnicornPet.Core.MiniGame
{
    public struct GameInput
    {
        public bool JumpPressed;
        public bool DuckHeld;
    }

    public enum GamePhase { Ready, Running, Paused, Over }

    [Flags]
    public enum GameEvents { None = 0, Jumped = 1, Landed = 2, StarCollected = 4, Milestone = 8, Bumped = 16 }

    /// <summary>
    /// The whole world of the mini game. Deterministic: time is the step count, randomness is the injected source.
    /// The window calls Step a whole number of times per frame and draws what it finds here.
    /// </summary>
    public sealed class RunnerGame
    {
        readonly Spawner _spawner;
        readonly List<Obstacle> _obstacles = new List<Obstacle>();
        readonly List<Star> _stars = new List<Star>();
        double _toNextSpawn;
        int _lastMilestone;

        public RunnerGame(Func<double> rng01)
        {
            _spawner = new Spawner(rng01);
            Runner = new RunnerBody();
            Phase = GamePhase.Ready;
            Clear();
        }

        public GamePhase Phase { get; private set; }
        public int Steps { get; private set; }
        public double Seconds { get; private set; }
        public double Speed { get; private set; }
        public double Distance { get; private set; }
        public int StarsCollected { get; private set; }
        public int Score { get { return (int)(Distance / GameTuning.PixelsPerPoint) + GameTuning.PointsPerStar * StarsCollected; } }
        public RunnerBody Runner { get; private set; }
        public IReadOnlyList<Obstacle> Obstacles { get { return _obstacles; } }
        public IReadOnlyList<Star> Stars { get { return _stars; } }
        public int StepsSinceBump { get; private set; }
        public bool CanRestart { get { return Phase == GamePhase.Over && StepsSinceBump >= GameTuning.RestartLockSteps; } }

        void Clear()
        {
            Steps = 0;
            Seconds = 0;
            Speed = GameTuning.StartSpeed;
            Distance = 0;
            StarsCollected = 0;
            StepsSinceBump = 0;
            _obstacles.Clear();
            _stars.Clear();
            _toNextSpawn = GameTuning.FirstSpawnDistance;
            _lastMilestone = 0;
            Runner.Reset();
        }

        /// <summary>From any phase: a fresh run. The random stream is not reseeded; the next run simply continues it.</summary>
        public void Start()
        {
            Clear();
            Phase = GamePhase.Running;
        }

        public void Pause() { if (Phase == GamePhase.Running) Phase = GamePhase.Paused; }

        public void Resume() { if (Phase == GamePhase.Paused) Phase = GamePhase.Running; }

        /// <summary>
        /// The start / resume / play-again input. Returns true when the input was used for that (so it must not also jump);
        /// false while running, when the caller should queue a jump instead.
        /// </summary>
        public bool PrimaryAction()
        {
            switch (Phase)
            {
                case GamePhase.Ready: Start(); return true;
                case GamePhase.Paused: Resume(); return true;
                case GamePhase.Over: if (CanRestart) Start(); return true;
                default: return false;
            }
        }

        public GameEvents Step(GameInput input)
        {
            if (Phase == GamePhase.Over)
            {
                if (StepsSinceBump < GameTuning.RestartLockSteps) StepsSinceBump++;
                return GameEvents.None;
            }
            if (Phase != GamePhase.Running) return GameEvents.None;

            var events = GameEvents.None;

            // 1. time and speed
            Steps++;
            Seconds = Steps * GameTuning.StepSeconds;
            Speed = Difficulty.Speed(Seconds);

            // 2. the runner
            bool wasInAir = !Runner.OnGround;
            if (Runner.Step(input.JumpPressed, input.DuckHeld)) events |= GameEvents.Jumped;
            if (wasInAir && Runner.OnGround) events |= GameEvents.Landed;

            // 3. the world moves left
            double dx = Speed * GameTuning.StepSeconds;
            Distance += dx;
            foreach (var o in _obstacles) o.X -= dx;
            foreach (var s in _stars) s.X -= dx;
            _obstacles.RemoveAll(o => o.X + o.Box.W < GameTuning.RemoveBelowX);
            _stars.RemoveAll(s => s.X + GameTuning.StarSize < GameTuning.RemoveBelowX);

            // 4. new obstacle (and maybe a star)
            _toNextSpawn -= dx;
            if (_toNextSpawn <= 0)
            {
                var sp = _spawner.Next(Seconds, Speed);
                _obstacles.Add(sp.Obstacle);
                if (sp.Star != null) _stars.Add(sp.Star);
                _toNextSpawn = sp.DistanceToNext;
            }

            // 5. stars
            var body = Runner.Box;
            int before = _stars.Count;
            _stars.RemoveAll(s => s.Box.Intersects(body));
            if (_stars.Count < before)
            {
                StarsCollected += before - _stars.Count;
                events |= GameEvents.StarCollected;
            }

            // 6. one bump ends the run
            foreach (var o in _obstacles)
            {
                if (o.Box.Intersects(body))
                {
                    Phase = GamePhase.Over;
                    StepsSinceBump = 0;
                    return events | GameEvents.Bumped;
                }
            }

            // 7. milestones
            int m = Score / GameTuning.MilestoneEvery;
            if (m > _lastMilestone)
            {
                _lastMilestone = m;
                events |= GameEvents.Milestone;
            }
            return events;
        }
    }
}

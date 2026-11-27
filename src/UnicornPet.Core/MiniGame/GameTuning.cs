namespace UnicornPet.Core.MiniGame
{
    /// <summary>
    /// Every number of the mini game.
    /// World units are playfield pixels; y is height above the ground; one step is 1/60 s.
    /// </summary>
    public static class GameTuning
    {
        public const int StepsPerSecond = 60;
        public const double StepSeconds = 1.0 / 60;

        public const double FieldWidth = 420, FieldHeight = 164, GroundStrip = 26;   // drawing: the ground line is 26 above the bottom

        public const double RunnerX = 56, StandW = 26, StandH = 36, DuckW = 34, DuckH = 20;
        public const double JumpVelocity = 470, Gravity = 1350, FastFall = 2.5;
        public const int JumpBufferSteps = 6;

        public const double StartSpeed = 200, MaxSpeed = 380, SpeedPerSecond = 3.0;
        public const double RampSeconds = 60, MinGapStart = 1.35, MinGapEnd = 0.95, GapSpread = 0.8;
        public const double SpawnX = 440, FirstSpawnDistance = 200, RemoveBelowX = -10;
        public const double WideFromSeconds = 10, OverheadFromSeconds = 20;

        public const double StarSize = 16, StarChance = 0.45, StarLift = 14, GapStarY = 8;
        public const int PointsPerStar = 10, MilestoneEvery = 500, RestartLockSteps = 36;
        public const double PixelsPerPoint = 10;
    }
}

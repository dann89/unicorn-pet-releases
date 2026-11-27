namespace UnicornPet.Core.MiniGame
{
    /// <summary>The unicorn's jump and duck physics, one fixed step at a time.</summary>
    public sealed class RunnerBody
    {
        int _jumpBuffer;

        public RunnerBody() { Reset(); }

        public double Y { get; private set; }
        public double VelocityY { get; private set; }
        public bool OnGround { get; private set; }
        public bool Ducking { get; private set; }

        public Box Box
        {
            get
            {
                return Ducking
                    ? new Box(GameTuning.RunnerX, Y, GameTuning.DuckW, GameTuning.DuckH)
                    : new Box(GameTuning.RunnerX, Y, GameTuning.StandW, GameTuning.StandH);
            }
        }

        /// <summary>On the ground, still, not ducking, no remembered jump.</summary>
        public void Reset()
        {
            Y = 0;
            VelocityY = 0;
            OnGround = true;
            Ducking = false;
            _jumpBuffer = 0;
        }

        /// <summary>One step. Returns true when a jump started in this step.</summary>
        public bool Step(bool jumpPressed, bool duckHeld)
        {
            bool jumped = false;
            // 1. remember a jump press for a few steps (pressed just before landing still counts)
            if (jumpPressed) _jumpBuffer = GameTuning.JumpBufferSteps;
            else if (_jumpBuffer > 0) _jumpBuffer--;
            // 2. take off
            if (OnGround && _jumpBuffer > 0)
            {
                VelocityY = GameTuning.JumpVelocity;
                OnGround = false;
                _jumpBuffer = 0;
                jumped = true;
            }
            // 3. fly (falling faster while duck is held), land
            if (!OnGround)
            {
                VelocityY -= GameTuning.Gravity * (duckHeld ? GameTuning.FastFall : 1) * GameTuning.StepSeconds;
                Y += VelocityY * GameTuning.StepSeconds;
                if (Y <= 0)
                {
                    Y = 0;
                    VelocityY = 0;
                    OnGround = true;
                }
            }
            // 4. duck only on the ground
            Ducking = duckHeld && OnGround;
            return jumped;
        }
    }
}

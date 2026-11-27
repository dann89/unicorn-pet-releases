using System;

namespace UnicornPet.Core
{
    public enum PetState { Idle, WalkLeft, WalkRight, Sleep, Hop, Talk }

    public class PetBehaviour
    {
        public const double WalkSpeed = 45;
        static readonly TimeSpan HopDuration = TimeSpan.FromMilliseconds(700);
        static readonly TimeSpan MaxStep = TimeSpan.FromMilliseconds(250);

        readonly Func<double> _rng;
        double _remaining;
        bool _sleeping;

        public PetState State { get; private set; } = PetState.Idle;
        public double X { get; set; }

        /// <summary>Walking speed multiplier (the pet's size scale, so a smaller pet takes smaller steps). 1 = the original speed.</summary>
        public double SpeedScale { get; set; } = 1;

        /// <summary>Diagnostics only: pins the pet to Idle, Sleep or walking (WalkLeft/WalkRight).</summary>
        public PetState? Force { get; set; }

        /// <summary>True while a pet mode (idle poop, zoomies) owns her pose and position: Step changes nothing.</summary>
        public bool Held { get; private set; }

        /// <summary>Shows one pose (Idle, WalkLeft, WalkRight or Sleep; anything else is Idle) and freezes the state and X until Release.</summary>
        public void Hold(PetState state)
        {
            Held = true;
            State = state == PetState.WalkLeft || state == PetState.WalkRight || state == PetState.Sleep ? state : PetState.Idle;
        }

        /// <summary>Ends a hold: normal behaviour resumes from Idle (or Sleep when asleep). Does nothing when not held.</summary>
        public void Release()
        {
            if (!Held) return;
            Held = false;
            EnterIdle();
        }

        public PetBehaviour(Func<double> rng01)
        {
            _rng = rng01;
            EnterIdle();
        }

        double R() { var v = _rng(); return v < 0 ? 0 : v > 1 ? 1 : v; }

        void EnterIdle()
        {
            State = _sleeping ? PetState.Sleep : PetState.Idle;
            _remaining = 2 + R() * 4;
        }

        public void Sleep()
        {
            Held = false;
            _sleeping = true;
            State = PetState.Sleep;
        }

        public void Wake()
        {
            _sleeping = false;
            if (State == PetState.Sleep) EnterIdle();
        }

        public void Hop()
        {
            Held = false;
            State = PetState.Hop;
            _remaining = HopDuration.TotalSeconds;
        }

        public void Talk(TimeSpan duration)
        {
            Held = false;
            State = PetState.Talk;
            _remaining = Math.Max(0.1, duration.TotalSeconds);
        }

        public void Step(TimeSpan dt, double minX, double maxX)
        {
            if (maxX < minX) maxX = minX;
            if (X < minX) X = minX;
            if (X > maxX) X = maxX;

            if (Force.HasValue)
            {
                bool walking = State == PetState.WalkLeft || State == PetState.WalkRight;
                bool wantWalk = Force == PetState.WalkLeft || Force == PetState.WalkRight;
                if (!(wantWalk ? walking : State == Force.Value))
                {
                    State = wantWalk ? PetState.WalkRight : Force.Value;
                    _remaining = 1e9;
                }
            }
            if (Held) return;

            var step = dt > MaxStep ? MaxStep : dt;
            if (step < TimeSpan.Zero) return;
            double s = step.TotalSeconds;

            switch (State)
            {
                case PetState.Sleep:
                    return;
                case PetState.Hop:
                case PetState.Talk:
                    _remaining -= s;
                    if (_remaining <= 0) EnterIdle();
                    return;
                case PetState.Idle:
                    _remaining -= s;
                    if (_remaining <= 0) StartWalk(minX, maxX);
                    return;
                case PetState.WalkLeft:
                case PetState.WalkRight:
                    X += (State == PetState.WalkRight ? 1 : -1) * WalkSpeed * (SpeedScale > 0 && SpeedScale <= 1 ? SpeedScale : 1) * s;
                    if (X >= maxX) { X = maxX; State = PetState.WalkLeft; }
                    else if (X <= minX) { X = minX; State = PetState.WalkRight; }
                    _remaining -= s;
                    if (_remaining <= 0) EnterIdle();
                    return;
            }
        }

        void StartWalk(double minX, double maxX)
        {
            bool right = R() >= 0.5;
            if (X >= maxX) right = false;
            else if (X <= minX) right = true;
            State = right ? PetState.WalkRight : PetState.WalkLeft;
            _remaining = 3 + R() * 5;
        }
    }
}


using System;

namespace GhostHands
{
    public sealed class StepProgress
    {
        public float ElapsedMs { get; private set; }
        public float HeldMs { get; private set; }
        public int Retries { get; private set; }
        public int TrackingLosses { get; private set; }
        public bool Finished { get; private set; }
        public bool Passed { get; private set; }
        private bool wasUnavailable;

        // Returns true exactly once, when this step completes or its recall deadline expires.
        public bool Tick(LessonStep step, float deltaMs, bool tracked, bool allMatched, bool recall)
        {
            if (Finished) return false;
            if (float.IsNaN(deltaMs) || float.IsInfinity(deltaMs) || deltaMs < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaMs));
            ElapsedMs += deltaMs;
            if (step.mode == "pass")
            {
                if (ElapsedMs >= step.durationMs) return Finish(true);
                return false;
            }
            if (!tracked)
            {
                if (!wasUnavailable) TrackingLosses++;
                wasUnavailable = true;
                HeldMs = 0;
            }
            else
            {
                wasUnavailable = false;
                if (!allMatched)
                {
                    if (HeldMs > 0) Retries++;
                    HeldMs = 0;
                }
                else HeldMs += deltaMs;
            }
            // Crossing the deadline and finishing the hold in the same tick must fail.
            if (recall && ElapsedMs > 20000) return Finish(false);
            if (tracked && allMatched && HeldMs >= step.holdMs.GetValueOrDefault()) return Finish(true);
            if (recall && ElapsedMs >= 20000) return Finish(false);
            return false;
        }

        private bool Finish(bool passed) { Finished = true; Passed = passed; return true; }
    }
}

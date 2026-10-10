using System;
using System.Collections.Generic;
using UnityEngine;

namespace GhostHands
{
    [Serializable]
    public sealed class FingerTolerances
    {
        [Min(1)] public float thumb = 25;
        [Min(1)] public float index = 20;
        [Min(1)] public float middle = 20;
        [Min(1)] public float ring = 20;
        [Min(1)] public float pinky = 20;
        [Min(1)] public float wrist = 35;
        [Range(0.01f, 1)] public float smoothingAlpha = 0.3f;

        public float ForFinger(string name)
        {
            switch (name)
            {
                case "thumb": return thumb;
                case "index": return index;
                case "middle": return middle;
                case "ring": return ring;
                case "pinky": return pinky;
                default: throw new ArgumentException("Unknown finger: " + name);
            }
        }
    }

    public sealed class HandMatch
    {
        public readonly Dictionary<string, FingerStatus> Fingers = new Dictionary<string, FingerStatus>();
        public readonly Dictionary<string, float> Errors = new Dictionary<string, float>();
        public bool Tracked;
        public bool WristMatched = true;
        public float WristError;
        public string Hint;
        public bool AllMatched
        {
            get
            {
                if (!Tracked || !WristMatched) return false;
                foreach (var status in Fingers.Values) if (status != FingerStatus.Matched) return false;
                return true;
            }
        }
    }

    // One instance per hand. Reset on a new step and on tracking loss.
    public sealed class FingerMatcher
    {
        public static readonly string[] FingerNames = { "thumb", "index", "middle", "ring", "pinky" };
        private readonly FingerTolerances tolerances;
        private TargetAngles smoothed;

        public FingerMatcher(FingerTolerances tolerances = null) => this.tolerances = tolerances ?? new FingerTolerances();
        public void Reset() => smoothed = null;

        public HandMatch Match(LessonHand target, Vector3[] liveWorldJoints, bool isRight,
            Quaternion? headYawRotation = null, bool checkWrist = false)
        {
            var result = new HandMatch();
            if (target?.targetAngles == null || !HandConventions.ValidJoints(liveWorldJoints))
            {
                Reset();
                foreach (var finger in FingerNames) result.Fingers[finger] = FingerStatus.Unavailable;
                return result;
            }
            var frame = HandConventions.HandFrame(liveWorldJoints, isRight);
            var angles = HandConventions.ComputeAngles(HandConventions.ToLocal(liveWorldJoints, frame));
            if (smoothed == null) smoothed = angles;
            else
                foreach (var finger in FingerNames)
                {
                    var previous = smoothed.ForFinger(finger);
                    var current = angles.ForFinger(finger);
                    for (int i = 0; i < previous.Length; i++)
                        previous[i] = Mathf.Lerp(previous[i], current[i], Mathf.Clamp01(tolerances.smoothingAlpha));
                }
            result.Tracked = true;
            string worst = null;
            float worstRatio = 1;
            foreach (var finger in FingerNames)
            {
                var actual = smoothed.ForFinger(finger);
                var desired = target.targetAngles.ForFinger(finger);
                float error = 0;
                for (int i = 0; i < actual.Length; i++) error = Mathf.Max(error, Mathf.Abs(actual[i] - desired[i]));
                float tolerance = Mathf.Max(1, tolerances.ForFinger(finger));
                result.Errors[finger] = error;
                result.Fingers[finger] = WithinTolerance(error, tolerance) ? FingerStatus.Matched :
                    WithinTolerance(error, 1.75f * tolerance) ? FingerStatus.Close : FingerStatus.Incorrect;
                if (result.Fingers[finger] != FingerStatus.Matched && error / tolerance > worstRatio)
                { worstRatio = error / tolerance; worst = finger; }
            }
            if (worst != null)
            {
                var actual = smoothed.ForFinger(worst);
                var desired = target.targetAngles.ForFinger(worst);
                float tolerance = Mathf.Max(1, tolerances.ForFinger(worst));
                bool bendMore = false, straighten = false;
                for (int i = 0; i < actual.Length; i++)
                {
                    if (desired[i] - actual[i] > tolerance) bendMore = true;
                    if (actual[i] - desired[i] > tolerance) straighten = true;
                }
                if (bendMore != straighten)
                    result.Hint = bendMore ? "Bend your " + worst + " more" : "Straighten your " + worst;
            }
            if (checkWrist)
            {
                if (!headYawRotation.HasValue) result.WristMatched = false;
                else
                {
                    result.WristError = Quaternion.Angle(Quaternion.Inverse(headYawRotation.Value) * frame, target.Rotation());
                    result.WristMatched = WithinTolerance(result.WristError, Mathf.Max(1, tolerances.wrist));
                }
            }
            return result;
        }

        // Preserve inclusive boundaries despite roundoff in angle and target subtraction.
        private static bool WithinTolerance(float error, float tolerance) =>
            error <= tolerance || Mathf.Approximately(error, tolerance);
    }
}

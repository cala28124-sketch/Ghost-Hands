using System;
using System.Collections.Generic;
using UnityEngine;

namespace GhostHands
{
    [Serializable]
    public sealed class FingerTolerances
    {
        // Degrees. Loose enough for a real hand that differs from the expert's: ring and pinky can't move on their own
        // as well as the others, and they're the least reliably tracked (by the headset and in the video).
        [Min(1)] public float thumb = 35;
        [Min(1)] public float index = 30;
        [Min(1)] public float middle = 30;
        [Min(1)] public float ring = 35;
        [Min(1)] public float pinky = 40;
        [Min(1)] public float wrist = 40;
        // The fingertip joint (DIP) counts this much: it's the noisiest joint and follows the middle joint (PIP) anyway.
        [Range(0, 1)] public float fingertipWeight = 0.5f;
        // A finger is judged by its overall curl (the average of its joints), so a learner who bends a little more at
        // the knuckle and less in the middle still matches. A single joint counts this much on its own, so one joint
        // can be off by up to tolerance / singleJointWeight (40 degrees for the index) before it fails the finger.
        [Range(0, 1)] public float singleJointWeight = 0.75f;
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
            float worstRatio = 1, worstCurl = 0;
            foreach (var finger in FingerNames)
            {
                var actual = smoothed.ForFinger(finger);
                var desired = target.targetAngles.ForFinger(finger);
                float curl = 0, weights = 0, joint = 0;  // curl > 0: the learner needs to bend more
                for (int i = 0; i < actual.Length; i++)
                {
                    float weight = actual.Length == 3 && i == 2 ? tolerances.fingertipWeight : 1;  // [MCP, PIP, DIP]
                    float difference = desired[i] - actual[i];
                    curl += weight * difference;
                    weights += weight;
                    joint = Mathf.Max(joint, weight * Mathf.Abs(difference));
                }
                curl /= weights;
                float error = Mathf.Max(Mathf.Abs(curl), tolerances.singleJointWeight * joint);
                float tolerance = Mathf.Max(1, tolerances.ForFinger(finger));
                result.Errors[finger] = error;
                result.Fingers[finger] = WithinTolerance(error, tolerance) ? FingerStatus.Matched :
                    WithinTolerance(error, 1.75f * tolerance) ? FingerStatus.Close : FingerStatus.Incorrect;
                if (result.Fingers[finger] != FingerStatus.Matched && error / tolerance > worstRatio)
                { worstRatio = error / tolerance; worst = finger; worstCurl = curl; }
            }
            // Joints off in opposite directions with the overall curl right: no simple advice, so no hint.
            if (worst != null && Mathf.Abs(worstCurl) > 1)
                result.Hint = worstCurl > 0 ? "Bend your " + worst + " more" : "Straighten your " + worst;
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

using UnityEngine;

namespace GhostHands
{
    // The end-of-lesson overview: one rating out of 10 made of three stats, over the whole lesson (practice and recall).
    //   Accuracy:     how closely each pose was held. Every hold step scores 100% when each finger is exactly on the
    //                 expert's pose, 50% at the edge of its tolerance (the loosest that still passes), 0% if it failed.
    //   Times tried:  one try per pose, plus one for every time a held pose broke and had to start again.
    //   Total time:   from the first step to the results, compared with par: the time the lesson itself takes
    //                 (holds, ghost transitions, motions) plus a moment to find each pose.
    // Rating = 10 x (half accuracy + a quarter tries score + a quarter time score), rounded to one decimal.
    public sealed class LessonPerformance
    {
        public const float AccuracyWeight = 0.5f, TriesWeight = 0.25f, TimeWeight = 0.25f;
        public const float FindPoseAllowanceMs = 1500;  // par time to find each pose, on top of holding it

        public int Poses { get; private set; }
        public int Retries { get; private set; }
        public float TotalMs { get; private set; }
        public float ParMs { get; private set; }
        private float accuracySum;

        public float Accuracy => Poses == 0 ? 1 : accuracySum / Poses;                // 0..1
        public int TimesTried => Poses + Retries;
        public float TriesScore => TimesTried == 0 ? 1 : (float)Poses / TimesTried;  // 1 when every pose held first time
        public float TimeScore => TotalMs <= 0 ? 1 : Mathf.Clamp01(ParMs / TotalMs);  // 1 at or under par
        public float Rating =>
            Mathf.Round(100 * (AccuracyWeight * Accuracy + TriesWeight * TriesScore + TimeWeight * TimeScore)) / 10;

        // How close one moment is: errorRatio is a finger's error / its tolerance, averaged over the fingers.
        public static float Closeness(float errorRatio) => Mathf.Clamp01(1 - 0.5f * errorRatio);

        public void AddTime(float ms) => TotalMs += ms;

        public void AddPose(bool passed, float accuracy, int retries, float parMs)
        {
            Poses++;
            Retries += retries;
            accuracySum += passed ? Mathf.Clamp01(accuracy) : 0;
            ParMs += parMs;
        }

        // A motion step: nothing to score, but it adds to the lesson's own time.
        public void AddMotion(float parMs) => ParMs += parMs;

        public static string FormatTime(float ms)
        {
            int seconds = Mathf.RoundToInt(ms / 1000);
            return seconds < 60 ? (ms / 1000).ToString("0.0") + " s" : (seconds / 60) + ":" + (seconds % 60).ToString("00");
        }
    }
}

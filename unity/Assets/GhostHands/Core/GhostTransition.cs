using UnityEngine;

namespace GhostHands
{
    // The ghost's move from one step's pose to the next. Purely visual: LessonFlow doesn't track the learner until
    // it lands. The renderer draws it and the flow waits for it, so both take its length from here.
    public static class GhostTransition
    {
        public const float MinSeconds = 0.6f, MaxSeconds = 1.5f;

        // A hold step's transition takes the expert's move time from the video (durationMs), kept between min and
        // max: quick moves look quick, a slow one doesn't keep the learner waiting. A pass step is a motion, so the
        // ghost takes its whole duration. The first step has nothing to move from.
        public static float Seconds(LessonStep previous, LessonStep next, float min = MinSeconds, float max = MaxSeconds)
        {
            if (previous == null || next == null) return 0;
            float seconds = next.durationMs / 1000f;
            return next.mode == "pass" ? seconds : Mathf.Clamp(seconds, Mathf.Min(min, max), max);
        }

        // Starts and stops gently instead of at full speed.
        public static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3 - 2 * t);
        }

        // The pose a fraction t of the way from one to the other. Each bone swings from its old direction to its new
        // one, chained out from the wrist, so a finger curls around its knuckles and keeps its length (sliding each
        // joint in a straight line would shrink a curling finger through the middle).
        public static Vector3[] Pose(Vector3[] from, Vector3[] to, float t, Vector3[] result)
        {
            result[0] = Vector3.Lerp(from[0], to[0], t);
            for (int i = 1; i < 21; i++)
            {
                int parent = HandConventions.Parent(i);
                result[i] = result[parent] + Vector3.Slerp(from[i] - from[parent], to[i] - to[parent], t);
            }
            return result;
        }
    }
}

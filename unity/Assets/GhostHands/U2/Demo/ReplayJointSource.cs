#if UNITY_EDITOR
using UnityEngine;

namespace GhostHands
{
    // Editor-only rehearsal source. Never included in headset builds.
    public sealed class ReplayJointSource : MonoBehaviour, IHandJointSource
    {
        public LessonFlow flow;
        public bool tracked = true;
        private Transform fallbackHead;
        public Transform Head
        {
            get
            {
                if (Camera.main != null) return Camera.main.transform;
                if (fallbackHead == null)
                {
                    var obj = new GameObject("Replay Head");
                    obj.transform.SetParent(transform, false);
                    obj.transform.localPosition = new Vector3(0, 1.6f, 0);
                    fallbackHead = obj.transform;
                }
                return fallbackHead;
            }
        }

        public bool TryGetJoints(bool isRight, out Vector3[] worldJoints21)
        {
            worldJoints21 = null;
            if (!tracked || flow?.CurrentStep == null ||
                !flow.CurrentStep.hands.TryGetValue(isRight ? "right" : "left", out var hand)) return false;
            worldJoints21 = WorldPose(hand, isRight, Head);
            return true;
        }

        public static Vector3[] WorldPose(LessonHand hand, bool isRight, Transform head)
        {
            var local = hand.JointVectors();
            // The mock's rounded knuckle points are slightly rotated within its nominal local frame.
            var frameCorrection = Quaternion.Inverse(HandConventions.HandFrame(local, isRight));
            var yaw = head == null ? Quaternion.identity : Quaternion.Euler(0, head.eulerAngles.y, 0);
            var rotation = yaw * hand.Rotation() * frameCorrection;
            var anchor = (head == null ? Vector3.zero : head.position) + yaw * new Vector3(isRight ? 0.15f : -0.15f, -0.25f, 0.45f);
            for (int i = 0; i < local.Length; i++) local[i] = anchor + rotation * local[i];
            return local;
        }
    }

    public static class EditorReplayBootstrap
    {
        public const string EnabledPreference = "GhostHands.EditorReplay";
    }
}
#endif

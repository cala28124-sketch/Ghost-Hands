using Oculus.Interaction.Input;
using UnityEngine;

namespace GhostHands
{
    // Interaction SDK GetJointPose already returns WORLD space; do not apply TrackingSpace again.
    public sealed class InteractionHandJointSource : MonoBehaviour, IHandJointSource
    {
        public Transform head;
        public MonoBehaviour leftHand, rightHand;
        private float nextDiscovery;
        public Transform Head => head != null ? head : Camera.main != null ? Camera.main.transform : null;
        public static readonly HandJointId[] LessonJointOrder =
        {
            HandJointId.HandWristRoot, HandJointId.HandThumb1, HandJointId.HandThumb2,
            HandJointId.HandThumb3, HandJointId.HandThumbTip,
            HandJointId.HandIndex1, HandJointId.HandIndex2, HandJointId.HandIndex3, HandJointId.HandIndexTip,
            HandJointId.HandMiddle1, HandJointId.HandMiddle2, HandJointId.HandMiddle3, HandJointId.HandMiddleTip,
            HandJointId.HandRing1, HandJointId.HandRing2, HandJointId.HandRing3, HandJointId.HandRingTip,
            HandJointId.HandPinky1, HandJointId.HandPinky2, HandJointId.HandPinky3, HandJointId.HandPinkyTip
        };

        public bool TryGetJoints(bool isRight, out Vector3[] worldJoints21)
        {
            worldJoints21 = null;
            if ((leftHand == null || rightHand == null) && Time.unscaledTime >= nextDiscovery)
            {
                nextDiscovery = Time.unscaledTime + 1;
                foreach (var component in FindObjectsByType<MonoBehaviour>())
                    if (component is IHand candidate)
                    {
                        if (candidate.Handedness == Handedness.Right && rightHand == null) rightHand = component;
                        if (candidate.Handedness == Handedness.Left && leftHand == null) leftHand = component;
                    }
            }
            var componentHand = isRight ? rightHand : leftHand;
            if (componentHand == null) return false;
            var hand = componentHand as IHand;
            if (hand == null || !hand.IsConnected || !hand.IsTrackedDataValid) return false;
            var points = new Vector3[21];
            for (int i = 0; i < points.Length; i++)
            {
                if (!hand.GetJointPose(LessonJointOrder[i], out var pose)) return false;
                points[i] = pose.position;
            }
            if (!HandConventions.ValidJoints(points)) return false;
            worldJoints21 = points;
            return true;
        }
    }
}

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

        private static bool Connected(MonoBehaviour component) => component != null && component is IHand hand && hand.IsConnected;

        public string Describe(bool isRight)
        {
            var hand = (isRight ? rightHand : leftHand) as IHand;
            if (hand == null) return "no IHand found";
            return $"{((MonoBehaviour)hand).name} connected: {hand.IsConnected}, valid data: {hand.IsTrackedDataValid}";
        }

        public bool TryGetJoints(bool isRight, out Vector3[] worldJoints21)
        {
            worldJoints21 = null;
            // Rediscover while a hand is missing or disconnected: the scene has several IHands per side (e.g. a
            // SyntheticHand for grab visuals), and the connected one is the real tracked hand.
            if ((!Connected(leftHand) || !Connected(rightHand)) && Time.unscaledTime >= nextDiscovery)
            {
                nextDiscovery = Time.unscaledTime + 1;
                foreach (var component in FindObjectsByType<MonoBehaviour>())
                    if (component is IHand candidate)
                    {
                        bool better = Connected(component);
                        if (candidate.Handedness == Handedness.Right && (rightHand == null || better && !Connected(rightHand))) rightHand = component;
                        if (candidate.Handedness == Handedness.Left && (leftHand == null || better && !Connected(leftHand))) leftHand = component;
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

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace GhostHands
{
    public sealed class XRHandsJointSource : MonoBehaviour, IHandJointSource
    {
        public Transform trackingSpace, head;
        public InteractionHandJointSource interactionFallback;
        private readonly List<XRHandSubsystem> subsystems = new List<XRHandSubsystem>();
        private XRHandSubsystem subsystem;
        private float nextDiscovery;
        private readonly string[] providers = { "Unavailable", "Unavailable" };
        private readonly int[] sampledFrames = { -1, -1 };
        private readonly Vector3[][] sampledJoints = new Vector3[2][];
        public Transform Head => head != null ? head : Camera.main != null ? Camera.main.transform : null;
        public string Provider(bool isRight) => providers[isRight ? 1 : 0];

        private void Awake() => ResolveRig();

        public void ResolveRig()
        {
            var rig = trackingSpace == null || head == null ? FindAnyObjectByType<OVRCameraRig>() : null;
            if (rig != null)
            {
                if (trackingSpace == null) trackingSpace = rig.trackingSpace;
                if (head == null) head = rig.centerEyeAnchor;
            }
            if (interactionFallback == null) interactionFallback = GetComponent<InteractionHandJointSource>();
            if (interactionFallback != null) interactionFallback.head = Head;
        }

        public bool TryGetJoints(bool isRight, out Vector3[] worldJoints21)
        {
            int index = isRight ? 1 : 0;
            if (Application.isPlaying && sampledFrames[index] == Time.frameCount)
            { worldJoints21 = sampledJoints[index]; return worldJoints21 != null; }
            ResolveRig();
            if ((subsystem == null || !subsystem.running) && Time.unscaledTime >= nextDiscovery)
            {
                nextDiscovery = Time.unscaledTime + 1;
                SubsystemManager.GetSubsystems(subsystems);
                subsystem = subsystems.Find(value => value.running);
            }
            worldJoints21 = null;
            providers[index] = "Unavailable";
            if (subsystem != null && subsystem.running && trackingSpace != null)
            {
                var hand = isRight ? subsystem.rightHand : subsystem.leftHand;
                if (hand.isTracked)
                {
                    var points = new Vector3[21];
                    bool valid = true;
                    for (int i = 0; i < points.Length; i++)
                    {
                        if (!hand.GetJoint(HandConventions.LessonJointOrder[i]).TryGetPose(out var pose))
                        { valid = false; break; }
                        points[i] = OriginToWorld(pose.position);
                    }
                    if (valid && HandConventions.ValidJoints(points))
                    { worldJoints21 = points; providers[index] = "XR Hands"; }
                }
            }
            if (worldJoints21 == null && interactionFallback != null &&
                interactionFallback.TryGetJoints(isRight, out worldJoints21)) providers[index] = "Interaction SDK";
            sampledFrames[index] = Time.frameCount;
            sampledJoints[index] = worldJoints21;
            return worldJoints21 != null;
        }

        public Vector3 OriginToWorld(Vector3 originPoint)
        {
            if (trackingSpace == null) throw new System.InvalidOperationException("TrackingSpace is required for XR Hands poses.");
            return trackingSpace.TransformPoint(originPoint);
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace GhostHands
{
    public static class HandConventions
    {
        public static readonly XRHandJointID[] LessonJointOrder =
        {
            XRHandJointID.Wrist, XRHandJointID.ThumbMetacarpal, XRHandJointID.ThumbProximal,
            XRHandJointID.ThumbDistal, XRHandJointID.ThumbTip,
            XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal, XRHandJointID.IndexTip,
            XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip,
            XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, XRHandJointID.RingDistal, XRHandJointID.RingTip,
            XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal, XRHandJointID.LittleTip
        };

        // The joint each bone starts from. Every finger starts at the wrist: joints 1, 5, 9, 13 and 17.
        public static int Parent(int joint) => (joint - 1) % 4 == 0 ? 0 : joint - 1;

        public static Quaternion HandFrame(Vector3[] p, bool isRight)
        {
            if (!ValidJoints(p)) throw new ArgumentException("Expected 21 finite, non-degenerate joints.");
            Vector3 forward = (p[9] - p[0]).normalized;
            Vector3 across = p[17] - p[5];
            Vector3 right = isRight ? across : -across;
            right = (right - Vector3.Dot(right, forward) * forward).normalized;
            if (right.sqrMagnitude < 0.5f) throw new ArgumentException("Degenerate hand frame.");
            return Quaternion.LookRotation(forward, Vector3.Cross(forward, right));
        }

        public static Vector3[] ToLocal(Vector3[] p, Quaternion frame)
        {
            var result = new Vector3[p.Length];
            var inverse = Quaternion.Inverse(frame);
            for (int i = 0; i < p.Length; i++) result[i] = inverse * (p[i] - p[0]);
            return result;
        }

        public static float Bend(Vector3 a, Vector3 b, Vector3 c, bool signed)
        {
            Vector3 bone1 = b - a, bone2 = c - b;
            Vector3 cross = Vector3.Cross(bone1, bone2);
            float angle = Mathf.Atan2(cross.magnitude, Vector3.Dot(bone1, bone2)) * Mathf.Rad2Deg;
            return signed && cross.x < 0 ? -angle : angle;
        }

        public static TargetAngles ComputeAngles(Vector3[] local)
        {
            return new TargetAngles
            {
                thumb = new[] { Bend(local[1], local[2], local[3], false), Bend(local[2], local[3], local[4], false) },
                index = Finger(local, 5), middle = Finger(local, 9), ring = Finger(local, 13), pinky = Finger(local, 17)
            };
        }

        private static float[] Finger(Vector3[] p, int k) => new[]
        {
            Bend(p[0], p[k], p[k + 1], true), Bend(p[k], p[k + 1], p[k + 2], true),
            Bend(p[k + 1], p[k + 2], p[k + 3], true)
        };

        public static bool ValidJoints(Vector3[] p)
        {
            if (p == null || p.Length != 21) return false;
            foreach (var v in p)
                if (!Finite(v.x) || !Finite(v.y) || !Finite(v.z)) return false;
            if ((p[9] - p[0]).sqrMagnitude < 1e-10f) return false;
            foreach (int start in new[] { 1, 5, 9, 13, 17 })
            {
                if (start != 1 && (p[start] - p[0]).sqrMagnitude < 1e-10f) return false;
                for (int i = start; i < start + 3; i++)
                    if ((p[i + 1] - p[i]).sqrMagnitude < 1e-10f) return false;
            }
            var forward = (p[9] - p[0]).normalized;
            var across = p[17] - p[5];
            return (across - Vector3.Dot(across, forward) * forward).sqrMagnitude >= 1e-10f;
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}

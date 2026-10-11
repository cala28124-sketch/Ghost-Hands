using UnityEngine;

namespace GhostHands
{
    // A right hand that tests can put anywhere by its index fingertip, for touching UI.
    public sealed class FingertipSource : MonoBehaviour, IHandJointSource
    {
        public Transform head;
        public Vector3? tip;  // world position of the index fingertip; null: no hand
        public Vector3[] pose;  // any valid 21-joint hand

        public Transform Head => head;

        public bool TryGetJoints(bool isRight, out Vector3[] worldJoints21)
        {
            worldJoints21 = null;
            if (!isRight || !tip.HasValue || pose == null) return false;
            worldJoints21 = new Vector3[21];
            var offset = tip.Value - pose[8];
            for (int i = 0; i < 21; i++) worldJoints21[i] = pose[i] + offset;
            return true;
        }
    }
}

using UnityEngine;

namespace GhostHands
{
    public interface IHandJointSource
    {
        bool TryGetJoints(bool isRight, out Vector3[] worldJoints21);
        Transform Head { get; }
    }
}

#if UNITY_EDITOR
using UnityEngine;

namespace GhostHands
{
    public sealed class FakeGhostRenderer : MonoBehaviour
    {
        public bool Visible;
        public bool Hidden;
        public int ShownSteps;
        public void ShowStep(LessonStep previous, LessonStep next) { ShownSteps++; }
        public void SetFingerStatus(string finger, FingerStatus status) { }
        public void SetVisible(bool value) { Visible = value; if (value) Hidden = false; }
        public void Hide() { Hidden = true; Visible = false; }
    }
}
#endif

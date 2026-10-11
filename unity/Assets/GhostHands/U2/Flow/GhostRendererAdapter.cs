using System;
using System.Reflection;
using UnityEngine;

namespace GhostHands
{
    // Keeps U2 compilable while U1's renderer is being built. No rendering lives here.
    // Assign Paul's GhostHandRenderer component in LessonFlow's inspector.
    public sealed class GhostRendererAdapter
    {
        private readonly MonoBehaviour renderer;
        private readonly MethodInfo show, showTimed, status, visible, hide;
        public GhostRendererAdapter(MonoBehaviour renderer)
        {
            this.renderer = renderer;
            if (renderer == null) return;
            var type = renderer.GetType();
            show = type.GetMethod("ShowStep", new[] { typeof(LessonStep), typeof(LessonStep) });
            showTimed = type.GetMethod("ShowStep", new[] { typeof(LessonStep), typeof(LessonStep), typeof(float) });  // optional
            status = type.GetMethod("SetFingerStatus", new[] { typeof(string), typeof(FingerStatus) });
            visible = type.GetMethod("SetVisible", new[] { typeof(bool) });
            hide = type.GetMethod("Hide", Type.EmptyTypes);
            if (show == null || status == null || visible == null || hide == null)
                Debug.LogError("Ghost renderer must expose ShowStep, SetFingerStatus, SetVisible and Hide with the agreed Core types.");
        }
        // The renderer takes this long to move the ghost to the new pose, if it supports a transition length.
        public void ShowStep(LessonStep previous, LessonStep next, float transitionSeconds)
        {
            if (showTimed != null) Invoke(showTimed, previous, next, transitionSeconds);
            else Invoke(show, previous, next);
        }
        public void SetFingerStatus(string finger, FingerStatus value) => Invoke(status, finger, value);
        public void SetVisible(bool value) => Invoke(visible, value);
        public void Hide() => Invoke(hide);
        private void Invoke(MethodInfo method, params object[] args)
        {
            if (renderer == null || method == null) return;
            try { method.Invoke(renderer, args); }
            catch (TargetInvocationException ex) { Debug.LogException(ex.InnerException ?? ex); }
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.UI;

namespace GhostHands
{
    // Touching world-space UI with an index fingertip, like Quest's own panels. Positions are in the surface's local
    // space, in millimetres (the canvases are scaled 1 unit = 1 mm): -z is toward the viewer, z = 0 is the surface.

    // A press is a fingertip pushed through the button from the front, like a real button. Sliding in from the side
    // or resting a hand behind it does nothing, so the hands can't press anything by accident while posing.
    public sealed class PokeDetector
    {
        public const float Margin = 15;      // the target is a little bigger than the button: fingertips aren't precise
        public const float ArmDepth = 80;    // a finger this close in front, over the button, is about to press
        public const float ReleaseDepth = 5; // pull back this far before the next press
        public const float PassDepth = 60;   // further behind than this, the finger is past the button
        private bool armed;
        public bool Hovering { get; private set; }

        public void Reset() { armed = false; Hovering = false; }

        public bool Update(Vector3 tip, Rect rect)
        {
            bool over = tip.x >= rect.xMin - Margin && tip.x <= rect.xMax + Margin &&
                tip.y >= rect.yMin - Margin && tip.y <= rect.yMax + Margin;
            Hovering = over && tip.z < 0 && tip.z >= -ArmDepth;
            if (!over || tip.z < -ArmDepth || tip.z > PassDepth) { armed = false; return false; }
            if (tip.z < -ReleaseDepth) { armed = true; return false; }
            if (!armed || tip.z < 0) return false;
            armed = false;
            return true;
        }
    }

    // A touch that can be a tap or a sideways swipe, for a scrolling row. It starts like a press (from the front)
    // and ends when the finger pulls back: a tap if it barely moved sideways, otherwise the end of a drag.
    public sealed class TouchTracker
    {
        public enum Result { None, Began, Tap, DragEnd }
        public const float DragDistance = 12;  // mm sideways before a touch becomes a swipe
        public const float LeaveDistance = 60; // mm above or below the row: the swipe has left it
        private bool armed;
        public bool Touching { get; private set; }
        public bool Dragging { get; private set; }
        public bool Hovering { get; private set; }
        public Vector2 Start { get; private set; }
        public Vector2 Position { get; private set; }
        public float DeltaX { get; private set; }  // sideways movement since the last update, while dragging

        public void Reset() { armed = Touching = Dragging = Hovering = false; DeltaX = 0; }

        public Result Update(Vector3 tip, Rect rect)
        {
            DeltaX = 0;
            bool over = tip.x >= rect.xMin && tip.x <= rect.xMax && tip.y >= rect.yMin && tip.y <= rect.yMax;
            Hovering = !Touching && over && tip.z < 0 && tip.z >= -PokeDetector.ArmDepth;
            if (Touching)
            {
                bool left = tip.z < -PokeDetector.ReleaseDepth || tip.z > 2 * PokeDetector.PassDepth ||
                    tip.y < rect.yMin - LeaveDistance || tip.y > rect.yMax + LeaveDistance;
                if (left)
                {
                    var result = Dragging ? Result.DragEnd : Result.Tap;
                    Touching = Dragging = false;
                    return result;
                }
                if (!Dragging && Mathf.Abs(tip.x - Start.x) > DragDistance) Dragging = true;
                if (Dragging) DeltaX = tip.x - Position.x;
                Position = tip;
                return Result.None;
            }
            if (!over || tip.z < -PokeDetector.ArmDepth || tip.z > PokeDetector.PassDepth) { armed = false; return Result.None; }
            if (tip.z < -PokeDetector.ReleaseDepth) { armed = true; return Result.None; }
            if (!armed || tip.z < 0) return Result.None;
            armed = false;
            Touching = true;
            Start = Position = tip;
            return Result.Began;
        }
    }

    // A uGUI Button that an index fingertip can press. The mouse still clicks it in the editor.
    [RequireComponent(typeof(RectTransform), typeof(Button))]
    public sealed class PokeButton : MonoBehaviour
    {
        // All buttons share this: one push can't press two of them (say, a button on the screen it just opened).
        private const float Cooldown = 0.4f;
        private static float nextPress;

        public Func<IHandJointSource> source;
        public Graphic fill, border;              // tinted on hover
        public Color fillIdle, fillHover, borderIdle, borderHover;
        private readonly PokeDetector[] pokes = { new PokeDetector(), new PokeDetector() };
        private RectTransform rect;
        private Button button;
        public bool Hovering { get; private set; }
        public Button Button => button != null ? button : button = GetComponent<Button>();

        private void Awake() { rect = (RectTransform)transform; button = GetComponent<Button>(); }

        private void OnDisable() { foreach (var poke in pokes) poke.Reset(); SetHover(false); }

        private void LateUpdate() => Poke();

        public void Poke()
        {
            if (rect == null) Awake();
            var joints = source?.Invoke();
            bool hovering = false, pressed = false;
            for (int side = 0; side < 2; side++)
            {
                if (joints == null || !joints.TryGetJoints(side == 1, out var p) || !HandConventions.ValidJoints(p))
                { pokes[side].Reset(); continue; }
                pressed |= pokes[side].Update(rect.InverseTransformPoint(p[8]), rect.rect);  // 8: index fingertip
                hovering |= pokes[side].Hovering;
            }
            SetHover(hovering);
            if (pressed && Time.unscaledTime >= nextPress && Button.interactable)
            {
                nextPress = Time.unscaledTime + Cooldown;
                Button.onClick.Invoke();
            }
        }

        public static void ResetCooldown() => nextPress = 0;

        private void SetHover(bool value)
        {
            Hovering = value;
            if (fill != null) fill.color = value ? fillHover : fillIdle;
            if (border != null) border.color = value ? borderHover : borderIdle;
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace GhostHands
{
    public sealed class GhostHandRenderer : MonoBehaviour
    {
        public MonoBehaviour handJointSource;
        public Transform head;
        public Material ghostMaterial;
        [Min(0)] public float anchorSmoothing = 10;
        [Min(0.001f)] public float jointDiameter = 0.008f;
        [Min(0.001f)] public float boneDiameter = 0.005f;
        private static readonly string[] Fingers = { "thumb", "index", "middle", "ring", "pinky" };
        private static readonly int[] Starts = { 1, 5, 9, 13, 17 };
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private readonly Color[] colors = new Color[5];
        private readonly HandVisual[] hands = new HandVisual[2];
        private Material material;
        private MaterialPropertyBlock properties;
        private bool visible = true;
        private IHandJointSource source;

        private sealed class HandVisual
        {
            public GameObject Root;
            public readonly Transform[] Joints = new Transform[21];
            public readonly Transform[] Bones = new Transform[20];
            public readonly Renderer[] JointRenderers = new Renderer[21];
            public readonly Renderer[] BoneRenderers = new Renderer[20];
            public readonly Vector3[] From = new Vector3[21], To = new Vector3[21], Current = new Vector3[21];
            public Quaternion FromRotation, ToRotation, Rotation;
            public Vector3 Anchor;
            public float Elapsed, Duration;
            public bool Active, Anchored;
        }

        public bool IsVisible => visible && isActiveAndEnabled;
        public bool HandVisible(bool isRight) => hands[isRight ? 1 : 0]?.Root.activeInHierarchy == true;
        public Vector3 JointPosition(bool isRight, int index) => hands[isRight ? 1 : 0].Joints[index].position;
        public Color FingerColor(string finger) => colors[Array.IndexOf(Fingers, finger)];

        private void Initialize()
        {
            if (material != null) return;
            var template = ghostMaterial != null ? ghostMaterial : Resources.Load<Material>("GhostHands/GhostHand");
            if (template == null) throw new InvalidOperationException("Missing Resources/GhostHands/GhostHand material.");
            material = new Material(template) { name = "Ghost Hands runtime material" };
            properties = new MaterialPropertyBlock();
            for (int i = 0; i < colors.Length; i++) colors[i] = new Color(0.55f, 0.95f, 1, 0.35f);
            source = handJointSource as IHandJointSource;
            for (int side = 0; side < 2; side++)
            {
                var hand = hands[side] = new HandVisual();
                hand.Root = new GameObject(side == 0 ? "Left Ghost Hand" : "Right Ghost Hand");
                hand.Root.transform.SetParent(transform, false);
                for (int i = 0; i < 21; i++)
                {
                    hand.Joints[i] = Primitive("Joint " + i, PrimitiveType.Sphere, hand.Root.transform, out hand.JointRenderers[i]);
                    hand.Joints[i].localScale = Vector3.one * jointDiameter;
                }
                for (int i = 0; i < 20; i++)
                    hand.Bones[i] = Primitive("Bone " + i, PrimitiveType.Cylinder, hand.Root.transform, out hand.BoneRenderers[i]);
                hand.Root.SetActive(false);
            }
        }

        private Transform Primitive(string name, PrimitiveType type, Transform parent, out Renderer renderer)
        {
            var obj = GameObject.CreatePrimitive(type); obj.name = name; obj.transform.SetParent(parent, false);
            var collider = obj.GetComponent<Collider>(); collider.enabled = false; Release(collider);
            renderer = obj.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            return obj.transform;
        }

        public void Show(LessonHand hand, bool isRight)
        {
            Initialize(); ValidateHand(hand);
            visible = true;
            Configure(hands[isRight ? 1 : 0], null, hand, 0);
            RefreshPose(0);
        }

        public void ShowStep(LessonStep previous, LessonStep next)
        {
            Initialize();
            if (next == null) { Hide(); return; }
            foreach (var pair in next.hands) ValidateHand(pair.Value);
            for (int side = 0; side < 2; side++)
            {
                string key = side == 0 ? "left" : "right";
                var visual = hands[side];
                if (!next.hands.TryGetValue(key, out var target))
                { visual.Active = false; visual.Root.SetActive(false); continue; }
                LessonHand from = null;
                if (previous != null) previous.hands.TryGetValue(key, out from);
                Configure(visual, from, target, next.durationMs / 1000f);
            }
            // Each new step starts cyan until the matcher supplies feedback.
            for (int i = 0; i < colors.Length; i++) colors[i] = new Color(0.55f, 0.95f, 1, 0.35f);
            RefreshPose(0);
        }

        private static void ValidateHand(LessonHand hand)
        {
            if (hand == null || hand.joints == null || hand.joints.Length != 21 ||
                hand.wristRotation == null || hand.wristRotation.Length != 4)
                throw new ArgumentException("Ghost pose requires 21 joints and a wrist quaternion.");
        }

        private static void Configure(HandVisual visual, LessonHand previous, LessonHand target, float duration)
        {
            var next = target.JointVectors();
            var from = previous != null ? previous.JointVectors() : next;
            Array.Copy(from, visual.From, 21); Array.Copy(next, visual.To, 21);
            visual.FromRotation = previous != null ? previous.Rotation() : target.Rotation();
            visual.ToRotation = target.Rotation();
            visual.Elapsed = 0; visual.Duration = duration;
            if (!visual.Active) visual.Anchored = false;
            visual.Active = true;
        }

        public void SetFingerStatus(string finger, FingerStatus status)
        {
            Initialize();
            int index = Array.IndexOf(Fingers, finger);
            if (index < 0) throw new ArgumentException("Unknown finger: " + finger);
            colors[index] = StatusColor(status);
        }

        public static Color StatusColor(FingerStatus status)
        {
            switch (status)
            {
                case FingerStatus.Matched: return new Color(0.2f, 1, 0.4f, 0.35f);
                case FingerStatus.Close: return new Color(1, 0.65f, 0.1f, 0.35f);
                case FingerStatus.Incorrect: return new Color(1, 0.2f, 0.2f, 0.35f);
                default: return new Color(0.5f, 0.5f, 0.5f, 0.35f);
            }
        }

        public void SetVisible(bool value)
        {
            visible = value;
            foreach (var hand in hands) if (hand != null) hand.Root.SetActive(value && hand.Active && isActiveAndEnabled);
        }
        public void Hide() => SetVisible(false);
        private void OnDisable() => SetVisible(visible);
        private void OnEnable() => SetVisible(visible);
        private void LateUpdate() => RefreshPose(Time.unscaledDeltaTime);

        public void RefreshPose(float deltaSeconds)
        {
            if (deltaSeconds < 0 || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds))
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (material == null) return;
            source = handJointSource as IHandJointSource ?? source;
            var viewer = head != null ? head : source?.Head != null ? source.Head : Camera.main != null ? Camera.main.transform : null;
            if (viewer == null) { foreach (var h in hands) h.Root.SetActive(false); return; }
            var yaw = Quaternion.Euler(0, viewer.eulerAngles.y, 0);
            for (int side = 0; side < 2; side++)
            {
                var h = hands[side];
                h.Root.SetActive(visible && h.Active && isActiveAndEnabled);
                if (!h.Active) continue;
                h.Elapsed += deltaSeconds;
                float blend = h.Duration <= 0 ? 1 : Mathf.Clamp01(h.Elapsed / h.Duration);
                for (int i = 0; i < 21; i++) h.Current[i] = Vector3.Lerp(h.From[i], h.To[i], blend);
                h.Rotation = Quaternion.Slerp(h.FromRotation, h.ToRotation, blend);
                Vector3[] live = null;
                bool tracked = source != null && source.TryGetJoints(side == 1, out live) && HandConventions.ValidJoints(live);
                var anchor = tracked ? live[0] : viewer.position + yaw * new Vector3(0, -0.15f, 0.35f);
                float scale = tracked && h.Current[9].magnitude > 0.0001f ?
                    Mathf.Clamp(Vector3.Distance(live[9], live[0]) / h.Current[9].magnitude, 0.7f, 1.4f) : 1;
                h.Anchor = h.Anchored ? Vector3.Lerp(h.Anchor, anchor, 1 - Mathf.Exp(-anchorSmoothing * deltaSeconds)) : anchor;
                h.Anchored = true;
                for (int i = 0; i < 21; i++) h.Joints[i].position = h.Anchor + yaw * (h.Rotation * h.Current[i] * scale);
                SetColor(h.JointRenderers[0], new Color(0.55f, 0.95f, 1, 0.35f), properties);
                for (int finger = 0; finger < 5; finger++)
                {
                    // An untracked hand is gray, including during a pass or standalone demo.
                    var color = tracked ? colors[finger] : StatusColor(FingerStatus.Unavailable);
                    for (int j = 0; j < 4; j++)
                    {
                        int end = Starts[finger] + j, start = j == 0 ? 0 : end - 1;
                        int bone = finger * 4 + j;
                        var a = h.Joints[start].position; var b = h.Joints[end].position; var direction = b - a;
                        h.Bones[bone].position = (a + b) * 0.5f;
                        h.Bones[bone].rotation = direction.sqrMagnitude > 1e-10f ? Quaternion.FromToRotation(Vector3.up, direction) : Quaternion.identity;
                        h.Bones[bone].localScale = new Vector3(boneDiameter, direction.magnitude * 0.5f, boneDiameter);
                        SetColor(h.BoneRenderers[bone], color, properties);
                        SetColor(h.JointRenderers[end], color, properties);
                    }
                }
            }
        }

        private static void SetColor(Renderer renderer, Color color, MaterialPropertyBlock properties)
        { properties.SetColor(BaseColor, color); renderer.SetPropertyBlock(properties); }

        internal static void Release(UnityEngine.Object obj)
        { if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }

        private void OnDestroy()
        {
            foreach (var h in hands) if (h != null && h.Root != null) Release(h.Root);
            if (material != null) Release(material);
        }
    }
}

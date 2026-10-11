using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GhostHands
{
    // The Ghost Hands look: translucent glass over passthrough, with the content on solid boxes edged in cyan.
    // Every size is in millimetres: the canvases are scaled 1 unit = 1 mm.
    public static class MenuStyle
    {
        public static readonly Color Glass = new Color(0.03f, 0.07f, 0.11f, 0.55f);
        public static readonly Color GlassEdge = new Color(0.36f, 0.88f, 0.90f, 0.45f);
        public static readonly Color BoxFill = new Color(0.055f, 0.095f, 0.14f, 0.97f);
        public static readonly Color BoxHover = new Color(0.08f, 0.15f, 0.21f, 0.98f);
        public static readonly Color Accent = new Color(0.36f, 0.88f, 0.90f);
        public static readonly Color AccentHover = new Color(0.62f, 0.96f, 0.98f);
        public static readonly Color AccentDim = new Color(0.36f, 0.88f, 0.90f, 0.35f);
        public static readonly Color Text = new Color(0.93f, 0.97f, 1f);
        public static readonly Color Muted = new Color(0.58f, 0.68f, 0.74f);
        public static readonly Color Ink = new Color(0.03f, 0.08f, 0.11f);  // text on an accent fill
        public static readonly Color Good = new Color(0.35f, 0.95f, 0.5f);   // matched, passed
        public static readonly Color Warn = new Color(1f, 0.75f, 0.25f);     // close, recall
        public static readonly Color Bad = new Color(1f, 0.4f, 0.35f);       // off, failed

        public static string Hex(Color color) => "#" + ColorUtility.ToHtmlStringRGB(color);

        public static Color ForStatus(FingerStatus status)
        {
            switch (status)
            {
                case FingerStatus.Matched: return Good;
                case FingerStatus.Close: return Warn;
                case FingerStatus.Incorrect: return Bad;
                default: return Muted;
            }
        }

        public enum Weight { Medium, SemiBold, Bold }
        private static readonly TMP_FontAsset[] fonts = new TMP_FontAsset[3];
        private static Sprite rounded;

        // Rajdhani (SIL Open Font License, Resources/GhostHands/Fonts), made into a TextMeshPro font at runtime.
        // Falls back to TextMeshPro's default font if it's missing; the default also covers any glyph Rajdhani lacks.
        public static TMP_FontAsset FontFor(Weight weight)
        {
            int i = (int)weight;
            if (fonts[i] != null) return fonts[i];
            var file = Resources.Load<Font>("GhostHands/Fonts/Rajdhani-" + weight);
            var fallback = TMP_Settings.defaultFontAsset;
            if (file == null) return fallback;
            fonts[i] = TMP_FontAsset.CreateFontAsset(file);
            fonts[i].name = "Rajdhani " + weight;
            if (fallback != null) fonts[i].fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { fallback };
            return fonts[i];
        }

        // A white rounded rectangle, 9-sliced so any box keeps the same corners.
        public static Sprite Rounded
        {
            get
            {
                if (rounded != null) return rounded;
                const int size = 64, radius = 20;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                { name = "Ghost Hands rounded box", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        // Distance outside the rounded corner, antialiased over one pixel.
                        float cx = Mathf.Clamp(x + 0.5f, radius, size - radius), cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                        float outside = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)) - radius;
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(0.5f - outside)));
                    }
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                rounded = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0,
                    SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
                return rounded;
            }
        }

        public static RectTransform Node(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        public static Image Fill(RectTransform rect, Color color, float cornerMm)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = Rounded;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 20 / Mathf.Max(0.1f, cornerMm);  // the sprite's corner is 20 units
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        // A box with an edge: the edge is the outer shape, the fill sits inside it. Returns the fill's rect,
        // which holds the content.
        public static RectTransform Box(Transform parent, string name, Vector2 position, Vector2 size, Color fill,
            Color edge, float edgeMm, float cornerMm, out Image fillImage, out Image edgeImage)
        {
            var outer = Node(parent, name, position, size);
            edgeImage = Fill(outer, edge, cornerMm);
            var inner = Node(outer, "Fill", Vector2.zero, size - 2 * edgeMm * Vector2.one);
            fillImage = Fill(inner, fill, Mathf.Max(0.5f, cornerMm - edgeMm));
            return inner;
        }

        // Rajdhani's line is 1.28 x its size (ascender 930 + descender 346 per 1000). A label's box is never shorter
        // than one line: TextMeshPro hides a line that doesn't fit its box.
        public const float LineHeight = 1.28f;

        public static TMP_Text Label(Transform parent, string name, string text, Vector2 position, Vector2 size,
            float fontMm, Weight weight, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.Center,
            float spacing = 0)
        {
            var rect = Node(parent, name, position, new Vector2(size.x, Mathf.Max(size.y, Mathf.Ceil(fontMm * LineHeight) + 2)));
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = FontFor(weight);
            label.text = text;
            label.fontSize = fontMm;
            label.color = color;
            label.alignment = alignment;
            label.characterSpacing = spacing;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;  // never hide text; a long card title opts into Ellipsis
            return label;
        }

        public static void Line(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            var image = Node(parent, name, position, size).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        // A primary button is filled with the accent; a secondary one is a box edged with it.
        public static PokeButton MakeButton(Transform parent, string name, string text, Vector2 position, Vector2 size,
            bool primary, float fontMm, Func<IHandJointSource> source, Action pressed)
        {
            var inner = Box(parent, name, position, size, primary ? Accent : BoxFill, Accent, 1.5f, 8,
                out var fill, out var edge);
            var root = inner.parent.gameObject;
            var graphic = root.GetComponent<Image>();
            graphic.raycastTarget = true;  // the mouse clicks the whole button in the editor
            var button = root.AddComponent<Button>();
            button.targetGraphic = graphic;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => pressed());
            var label = Label(inner, "Label", text, Vector2.zero, inner.sizeDelta, fontMm, Weight.Bold,
                primary ? Ink : Text, TextAlignmentOptions.Center, 8);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            var poke = root.AddComponent<PokeButton>();
            poke.source = source;
            poke.fill = fill; poke.border = edge;
            poke.fillIdle = primary ? Accent : BoxFill; poke.fillHover = primary ? AccentHover : BoxHover;
            poke.borderIdle = primary ? Accent : AccentDim; poke.borderHover = primary ? AccentHover : Accent;
            fill.color = poke.fillIdle; edge.color = poke.borderIdle;
            return poke;
        }

        public static void Release(UnityEngine.Object obj)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj); else UnityEngine.Object.DestroyImmediate(obj);
        }

        // A world-space canvas, 1 unit = 1 mm, that the mouse can click in the editor.
        public static RectTransform WorldCanvas(Transform parent, string name, Vector2 size)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            rect.localScale = Vector3.one * 0.001f;
            root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            return rect;
        }
    }
}

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GhostHands
{
    // A row of tutorial cards, scrolled left and right by swiping it with a fingertip (or the arrow buttons).
    // Tapping a card picks it; a swipe never does. After a swipe the row settles on the nearest card.
    public sealed class TutorialCarousel : MonoBehaviour
    {
        public const float CardWidth = 130, CardHeight = 160, Gap = 15, Pitch = CardWidth + Gap;
        private const float SettleSeconds = 0.12f, FlingSeconds = 0.15f, OverscrollGive = 0.35f;

        public Func<IHandJointSource> source;
        public event Action<LessonSummary> Selected;
        private RectTransform viewport, content;
        private readonly List<Card> cards = new List<Card>();
        private readonly TouchTracker[] touches = { new TouchTracker(), new TouchTracker() };
        private int activeHand = -1;
        private float scroll, target, settleVelocity, swipeVelocity;

        private sealed class Card
        {
            public LessonSummary Lesson;
            public RectTransform Rect;
            public Image Fill, Edge;
        }

        public int Count => cards.Count;
        public float Scroll => scroll;
        public float Target => target;
        public float MaxScroll => Mathf.Max(0, ContentWidth - viewport.rect.width);
        public RectTransform Viewport => viewport;
        private float ContentWidth => cards.Count == 0 ? 0 : cards.Count * CardWidth + (cards.Count - 1) * Gap;
        // A row narrower than the view sits in the middle.
        private float Lead => Mathf.Max(0, (viewport.rect.width - ContentWidth) / 2);

        public static TutorialCarousel Create(Transform parent, Vector2 position, Vector2 size, Func<IHandJointSource> source)
        {
            var view = MenuStyle.Node(parent, "Tutorials", position, size);
            view.gameObject.AddComponent<RectMask2D>();
            var carousel = view.gameObject.AddComponent<TutorialCarousel>();
            carousel.viewport = view;
            carousel.source = source;
            carousel.content = MenuStyle.Node(view, "Row", Vector2.zero, new Vector2(0, CardHeight));
            carousel.content.anchorMin = carousel.content.anchorMax = carousel.content.pivot = new Vector2(0, 0.5f);
            return carousel;
        }

        public void SetLessons(IList<LessonSummary> lessons)
        {
            foreach (var card in cards) MenuStyle.Release(card.Rect.parent.gameObject);
            cards.Clear();
            for (int i = 0; i < lessons.Count; i++) cards.Add(MakeCard(lessons[i], i));
            content.sizeDelta = new Vector2(ContentWidth, CardHeight);
            scroll = target = settleVelocity = 0;
            Apply();
        }

        private Card MakeCard(LessonSummary lesson, int index)
        {
            var inner = MenuStyle.Box(content, "Card " + (index + 1), Vector2.zero, new Vector2(CardWidth, CardHeight),
                MenuStyle.BoxFill, MenuStyle.AccentDim, 1.5f, 10, out var fill, out var edge);
            var root = (RectTransform)inner.parent;
            root.anchorMin = root.anchorMax = new Vector2(0, 0.5f);
            root.anchoredPosition = new Vector2(index * Pitch + CardWidth / 2, 0);
            float w = inner.sizeDelta.x, h = inner.sizeDelta.y;
            MenuStyle.Label(inner, "Number", (index + 1).ToString("00"), new Vector2(0, h / 2 - 18), new Vector2(w - 24, 18),
                13, MenuStyle.Weight.SemiBold, MenuStyle.Accent, TextAlignmentOptions.Left, 6);
            var title = MenuStyle.Label(inner, "Title", string.IsNullOrEmpty(lesson.title) ? lesson.lessonId : lesson.title,
                new Vector2(0, 8), new Vector2(w - 20, 80), 21, MenuStyle.Weight.Bold, MenuStyle.Text);
            title.maxVisibleLines = 3;
            title.lineSpacing = -12;
            title.overflowMode = TextOverflowModes.Ellipsis;  // a very long name ends in "…" (80 mm holds 3 lines)
            MenuStyle.Line(inner, "Rule", new Vector2(0, -h / 2 + 34), new Vector2(w - 24, 1), MenuStyle.AccentDim);
            int hands = lesson.hands?.Length ?? 0;
            MenuStyle.Label(inner, "Details", lesson.stepCount + (lesson.stepCount == 1 ? " STEP" : " STEPS") + "  ·  " +
                (hands == 2 ? "BOTH HANDS" : hands == 1 ? lesson.hands[0].ToUpperInvariant() + " HAND" : ""),
                new Vector2(0, -h / 2 + 18), new Vector2(w - 20, 18), 11, MenuStyle.Weight.SemiBold, MenuStyle.Muted,
                TextAlignmentOptions.Center, 4);
            edge.raycastTarget = true;  // the mouse picks it in the editor; the row's mask hides it outside the view
            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = edge;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => Selected?.Invoke(lesson));
            return new Card { Lesson = lesson, Rect = inner, Fill = fill, Edge = edge };
        }

        // The card under a point in the view's space, or -1.
        public int CardAt(Vector2 point)
        {
            float x = point.x - viewport.rect.xMin - (Lead - scroll);
            int index = Mathf.FloorToInt(x / Pitch);
            bool inside = index >= 0 && index < cards.Count && x - index * Pitch <= CardWidth && Mathf.Abs(point.y) <= CardHeight / 2;
            return inside ? index : -1;
        }

        public void Page(int direction) => target = SnapTarget(Mathf.Round(scroll / Pitch) + direction);

        private float SnapTarget(float cardIndex) => Mathf.Clamp(cardIndex * Pitch, 0, MaxScroll);

        private void Update() => Step(Time.unscaledDeltaTime);

        public void Step(float deltaSeconds)
        {
            if (viewport == null) return;
            var joints = source?.Invoke();
            int hovered = -1;
            for (int side = 0; side < 2; side++)
            {
                var touch = touches[side];
                if (joints == null || !joints.TryGetJoints(side == 1, out var p) || !HandConventions.ValidJoints(p))
                {
                    if (activeHand == side) { activeHand = -1; target = SnapTarget(Mathf.Round(scroll / Pitch)); }
                    touch.Reset();
                    continue;
                }
                Vector3 tip = viewport.InverseTransformPoint(p[8]);  // 8: index fingertip
                var result = touch.Update(tip, viewport.rect);
                if (touch.Hovering && hovered < 0) hovered = CardAt(tip);
                if (result == TouchTracker.Result.Began && activeHand < 0) { activeHand = side; swipeVelocity = 0; }
                if (activeHand != side) continue;
                if (touch.Touching && !touch.Dragging && hovered < 0) hovered = CardAt(touch.Start);
                if (touch.Dragging) Drag(touch.DeltaX, deltaSeconds);
                if (result == TouchTracker.Result.Tap)
                {
                    activeHand = -1;
                    int index = CardAt(touch.Start);
                    if (index >= 0) Selected?.Invoke(cards[index].Lesson);
                }
                else if (result == TouchTracker.Result.DragEnd)
                {
                    activeHand = -1;
                    target = SnapTarget(Mathf.Round((scroll + swipeVelocity * FlingSeconds) / Pitch));
                }
            }
            if (activeHand < 0)
                scroll = deltaSeconds > 0 ? Mathf.SmoothDamp(scroll, target, ref settleVelocity, SettleSeconds,
                    Mathf.Infinity, deltaSeconds) : target;
            for (int i = 0; i < cards.Count; i++)
            {
                cards[i].Fill.color = i == hovered ? MenuStyle.BoxHover : MenuStyle.BoxFill;
                cards[i].Edge.color = i == hovered ? MenuStyle.Accent : MenuStyle.AccentDim;
            }
            Apply();
        }

        // Moving the finger right moves the row right, showing earlier cards. Past either end the row gives a little.
        private void Drag(float deltaX, float deltaSeconds)
        {
            bool outside = scroll < 0 || scroll > MaxScroll;
            scroll -= outside ? deltaX * OverscrollGive : deltaX;
            if (deltaSeconds > 0) swipeVelocity = Mathf.Lerp(swipeVelocity, -deltaX / deltaSeconds, 0.5f);
            target = scroll;
            settleVelocity = 0;
        }

        private void Apply()
        {
            if (content != null) content.anchoredPosition = new Vector2(Lead - scroll, 0);
        }

        // How much of the row is in view, and where: for the scroll bar under it (0..1 each).
        public float VisibleFraction => ContentWidth <= 0 ? 1 : Mathf.Clamp01(viewport.rect.width / ContentWidth);
        public float ScrollFraction => MaxScroll <= 0 ? 0 : Mathf.Clamp01(scroll / MaxScroll);
    }
}

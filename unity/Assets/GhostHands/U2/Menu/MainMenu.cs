using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace GhostHands
{
    // The main menu, floating within arm's reach: Start (pick a tutorial) and Credits. A translucent glass panel over
    // passthrough, with each screen's content on a solid box. Shown whenever no lesson is running (LessonFlow Idle).
    // Everything is touched with an index fingertip; the mouse also works in the editor.
    [RequireComponent(typeof(LessonFlow))]
    public sealed class MainMenu : MonoBehaviour
    {
        public enum Screen { Home, Tutorials, Credits }

        [Tooltip("Shown at the top of the menu. A stand-in until there are real accounts.")]
        public string userId = "98320384";
        [Tooltip("How far ahead of the eyes the menu floats, in metres: close enough to touch.")]
        public float distance = 0.5f;

        public static readonly (string name, string role, string detail)[] Team =
        {
            ("DOMINIC", "LEAD DEVELOPER", "Computer vision · Unity · Integration"),
            ("JUSTIN", "UNITY DEVELOPER", "Lesson flow · Matching"),
            ("PAUL", "UNITY DEVELOPER", "Hand tracking · Rendering"),
            ("ANTHONY", "WEB & DATABASE", "Server · Database · Website"),
        };

        private const float Width = 480, Height = 320;
        private LessonFlow flow;
        private LessonLoader loader;
        private RectTransform canvas, home, tutorials, credits, scrollThumb;
        private TutorialCarousel carousel;
        private TMP_Text tutorialStatus, tutorialCount;
        private bool placed, listing;
        public Screen Current { get; private set; }
        public bool IsOpen => canvas != null && canvas.gameObject.activeSelf;
        public RectTransform Canvas => canvas;
        public TutorialCarousel Carousel => carousel;

        private void Start()
        {
            Build();
            if (flow.State != LessonState.Practice && flow.State != LessonState.Recall) flow.Stop();
            else Hide();
        }

        private void OnDestroy() { if (flow != null) flow.StateChanged -= OnStateChanged; }

        public void Build()
        {
            if (canvas != null) return;
            flow = GetComponent<LessonFlow>();
            loader = GetComponent<LessonLoader>();
            flow.StateChanged += OnStateChanged;
            canvas = MenuStyle.WorldCanvas(transform, "Ghost Hands Main Menu", new Vector2(Width, Height));
            var glass = MenuStyle.Box(canvas, "Glass", Vector2.zero, new Vector2(Width, Height), MenuStyle.Glass,
                MenuStyle.GlassEdge, 1, 18, out _, out _);
            BuildTopBar(glass);
            home = Page(glass, "Home"); BuildHome(home);
            tutorials = Page(glass, "Tutorials"); BuildTutorials(tutorials);
            credits = Page(glass, "Credits"); BuildCredits(credits);
            Show(Screen.Home);
        }

        private Func<IHandJointSource> Hands => () => flow.Source;

        private static RectTransform Page(RectTransform parent, string name)
        {
            var page = MenuStyle.Node(parent, name, Vector2.zero, Vector2.zero);
            page.anchorMin = Vector2.zero; page.anchorMax = Vector2.one;
            return page;
        }

        // User ID on the left, event on the right, a hairline under both: on every screen.
        private void BuildTopBar(RectTransform glass)
        {
            const float y = Height / 2 - 20, edge = Width / 2 - 24;
            MenuStyle.Label(glass, "User Label", "USER ID", new Vector2(-edge + 30, y), new Vector2(60, 16), 11,
                MenuStyle.Weight.SemiBold, MenuStyle.Muted, TextAlignmentOptions.Left, 8);
            MenuStyle.Label(glass, "User ID", userId, new Vector2(-edge + 130, y), new Vector2(140, 16), 13,
                MenuStyle.Weight.Bold, MenuStyle.Accent, TextAlignmentOptions.Left, 4);
            MenuStyle.Label(glass, "Event", "KNIGHT HACKS IX", new Vector2(edge - 70, y), new Vector2(140, 16), 11,
                MenuStyle.Weight.SemiBold, MenuStyle.Muted, TextAlignmentOptions.Right, 8);
            MenuStyle.Line(glass, "Top Rule", new Vector2(0, y - 15), new Vector2(Width - 48, 1), MenuStyle.AccentDim);
        }

        private void BuildHome(RectTransform page)
        {
            var box = MenuStyle.Box(page, "Box", new Vector2(0, -18), new Vector2(400, 224), MenuStyle.BoxFill,
                MenuStyle.Accent, 2, 14, out _, out _);
            MenuStyle.Label(box, "Title", "GHOST HANDS", new Vector2(0, 62), new Vector2(380, 60), 50,
                MenuStyle.Weight.Bold, MenuStyle.Text, TextAlignmentOptions.Center, 14).textWrappingMode = TextWrappingModes.NoWrap;
            MenuStyle.Line(box, "Title Rule", new Vector2(0, 28), new Vector2(64, 2), MenuStyle.Accent);
            MenuStyle.MakeButton(box, "Start", "START", new Vector2(0, -22), new Vector2(220, 44), true, 18, Hands,
                () => Show(Screen.Tutorials));
            MenuStyle.MakeButton(box, "Credits", "CREDITS", new Vector2(0, -76), new Vector2(220, 44), false, 18, Hands,
                () => Show(Screen.Credits));
        }

        // A back button, the screen's title and an optional note on the right, in one row under the top bar.
        private TMP_Text Header(RectTransform page, string title)
        {
            const float y = 100;
            MenuStyle.MakeButton(page, "Back", "BACK", new Vector2(-Width / 2 + 72, y), new Vector2(96, 32), false, 13, Hands,
                () => Show(Screen.Home));
            MenuStyle.Label(page, "Heading", title, new Vector2(0, y), new Vector2(240, 32), 24, MenuStyle.Weight.Bold,
                MenuStyle.Text, TextAlignmentOptions.Center, 8).textWrappingMode = TextWrappingModes.NoWrap;
            return MenuStyle.Label(page, "Note", "", new Vector2(Width / 2 - 82, y), new Vector2(116, 32), 12,
                MenuStyle.Weight.SemiBold, MenuStyle.Muted, TextAlignmentOptions.Right, 6);
        }

        private void BuildTutorials(RectTransform page)
        {
            tutorialCount = Header(page, "SELECT A TUTORIAL");
            var box = MenuStyle.Box(page, "Box", new Vector2(0, -12), new Vector2(452, 200), MenuStyle.BoxFill,
                MenuStyle.Accent, 2, 14, out _, out _);
            carousel = TutorialCarousel.Create(box, Vector2.zero, new Vector2(350, TutorialCarousel.CardHeight + 8), Hands);
            carousel.Selected += lesson => flow.StartLesson(lesson.lessonId);
            Arrow(box, "Previous", "<", -198, -1);
            Arrow(box, "Next", ">", 198, 1);
            tutorialStatus = MenuStyle.Label(box, "Status", "", Vector2.zero, new Vector2(320, 60), 16,
                MenuStyle.Weight.SemiBold, MenuStyle.Muted);
            MenuStyle.Line(page, "Scroll Track", new Vector2(0, -126), new Vector2(120, 3), MenuStyle.AccentDim);
            scrollThumb = MenuStyle.Node(page, "Scroll Thumb", new Vector2(0, -126), new Vector2(120, 3));
            scrollThumb.gameObject.AddComponent<UnityEngine.UI.Image>().color = MenuStyle.Accent;
            MenuStyle.Label(page, "Hint", "SWIPE TO BROWSE   ·   TAP A TUTORIAL TO START", new Vector2(0, -145),
                new Vector2(400, 16), 11, MenuStyle.Weight.SemiBold, MenuStyle.Muted, TextAlignmentOptions.Center, 6);
        }

        private void Arrow(RectTransform box, string name, string glyph, float x, int direction)
        {
            var button = MenuStyle.MakeButton(box, name, glyph, new Vector2(x, 0), new Vector2(34, 60), false, 22, Hands,
                () => carousel.Page(direction));
            button.GetComponentInChildren<TMP_Text>().richText = false;  // "<" is text here, not a tag
        }

        private void BuildCredits(RectTransform page)
        {
            Header(page, "CREDITS");
            for (int i = 0; i < Team.Length; i++)
            {
                var position = new Vector2(i % 2 == 0 ? -108 : 108, i < 2 ? 30 : -66);
                var box = MenuStyle.Box(page, Team[i].name, position, new Vector2(205, 86), MenuStyle.BoxFill,
                    MenuStyle.Accent, 2, 12, out _, out _);
                MenuStyle.Label(box, "Name", Team[i].name, new Vector2(0, 18), new Vector2(185, 30), 26,
                    MenuStyle.Weight.Bold, MenuStyle.Text, TextAlignmentOptions.Center, 6);
                MenuStyle.Label(box, "Role", Team[i].role, new Vector2(0, -6), new Vector2(185, 18), 13,
                    MenuStyle.Weight.SemiBold, MenuStyle.Accent, TextAlignmentOptions.Center, 6);
                MenuStyle.Label(box, "Detail", Team[i].detail, new Vector2(0, -25), new Vector2(185, 18), 12,
                    MenuStyle.Weight.Medium, MenuStyle.Muted);
            }
            MenuStyle.Label(page, "Footer", "BUILT AT KNIGHT HACKS IX", new Vector2(0, -135), new Vector2(300, 16), 11,
                MenuStyle.Weight.SemiBold, MenuStyle.Muted, TextAlignmentOptions.Center, 8);
        }

        public void Show(Screen screen)
        {
            Build();
            Current = screen;
            home.gameObject.SetActive(screen == Screen.Home);
            tutorials.gameObject.SetActive(screen == Screen.Tutorials);
            credits.gameObject.SetActive(screen == Screen.Credits);
            if (screen == Screen.Tutorials) RefreshTutorials();
        }

        // The tutorials are every real lesson on the server (processed by the CV pipeline and validated when uploaded).
        // "example" is the placeholder from shared/lessons (made-up numbers, seeded into the database for testing):
        // it stays the app's offline fallback, but isn't offered as a tutorial unless it's the only lesson there is.
        public const string PlaceholderLessonId = "example";

        public static List<LessonSummary> Tutorials(List<LessonSummary> lessons)
        {
            var real = lessons.FindAll(lesson => lesson.lessonId != PlaceholderLessonId);
            return real.Count > 0 ? real : lessons;
        }

        private void RefreshTutorials()
        {
            if (listing || loader == null || !isActiveAndEnabled) return;
            listing = true;
            tutorialStatus.text = "LOADING TUTORIALS…";
            tutorialCount.text = "";
            carousel.SetLessons(new List<LessonSummary>());
            StartCoroutine(loader.ListLessons(ShowTutorials, error =>
            {
                listing = false;
                tutorialStatus.text = "COULDN'T LOAD TUTORIALS";
            }));
        }

        public void ShowTutorials(List<LessonSummary> lessons)
        {
            listing = false;
            lessons = Tutorials(lessons);
            carousel.SetLessons(lessons);
            tutorialStatus.text = lessons.Count == 0 ? "NO TUTORIALS YET" : "";
            tutorialCount.text = lessons.Count + " AVAILABLE";
        }

        private void OnStateChanged(LessonState state)
        {
            if (state == LessonState.Idle) Open();
            else Hide();
        }

        public void Open()
        {
            Build();
            canvas.gameObject.SetActive(true);
            placed = false;
            Show(Screen.Home);
        }

        public void Hide()
        {
            if (canvas != null) canvas.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (!IsOpen) return;
            var head = flow.Head != null ? flow.Head : Camera.main != null ? Camera.main.transform : null;
            if (head == null) return;
            canvas.GetComponent<UnityEngine.Canvas>().worldCamera = head.GetComponent<Camera>() ?? Camera.main;
            var direction = Vector3.ProjectOnPlane(canvas.position - head.position, Vector3.up);
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (!placed || direction.magnitude > 1.2f || Vector3.Angle(forward, direction) > 50) Place(head);
            if (carousel.MaxScroll > 0)
            {
                float width = 120 * carousel.VisibleFraction;
                scrollThumb.sizeDelta = new Vector2(width, 3);
                scrollThumb.anchoredPosition = new Vector2(-60 + width / 2 + (120 - width) * carousel.ScrollFraction, -126);
            }
            else scrollThumb.sizeDelta = new Vector2(120, 3);
        }

        // Straight ahead at arm's reach, a little below the eyes, upright and facing the learner.
        public void Place(Transform head)
        {
            Build();
            var yaw = Quaternion.Euler(0, head.eulerAngles.y, 0);
            canvas.position = head.position + yaw * new Vector3(0, -0.08f, distance);
            canvas.rotation = yaw;
            placed = true;
        }
    }
}

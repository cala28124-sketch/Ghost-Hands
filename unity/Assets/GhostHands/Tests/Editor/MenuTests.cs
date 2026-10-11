using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace GhostHands
{
    public sealed class MenuTests
    {
        private GameObject root;
        private LessonFlow flow;
        private MainMenu menu;
        private LessonPanel panel;
        private FingertipSource hand;
        private Transform head;

        [SetUp]
        public void SetUp()
        {
            var lesson = JsonConvert.DeserializeObject<Lesson>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Lessons/example.json")));
            root = new GameObject("Menu Test") { hideFlags = HideFlags.HideAndDontSave };
            head = new GameObject("Head").transform; head.SetParent(root.transform, false);
            head.position = new Vector3(0, 1.6f, 0);
            hand = root.AddComponent<FingertipSource>();
            hand.head = head;
            hand.pose = lesson.steps[0].hands["right"].JointVectors();
            flow = root.AddComponent<LessonFlow>(); flow.autoStart = false; flow.submitResults = false;
            flow.handJointSource = hand; flow.RefreshBindings();
            root.GetComponent<LessonLoader>().serverUrl = "http://127.0.0.1:9";  // nothing listens: never a real server
            panel = root.AddComponent<LessonPanel>();
            menu = root.AddComponent<MainMenu>();
            menu.Build();
            flow.Stop();
            panel.RefreshView();
            PokeButton.ResetCooldown();
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(root);

        private static List<LessonSummary> Lessons(int count) => Enumerable.Range(1, count).Select(i =>
            new LessonSummary { lessonId = "lesson-" + i, title = "Lesson " + i, hands = new[] { "left", "right" }, stepCount = 4 }).ToList();

        private static string Texts(Transform t) => string.Join("|", t.GetComponentsInChildren<TMP_Text>(true).Select(x => x.text));

        // Push a fingertip into a button from the front, as a learner would.
        private void Poke(PokeButton button)
        {
            var rect = (RectTransform)button.transform;
            foreach (float mm in new[] { -100f, -40f, 5f })
            {
                hand.tip = rect.TransformPoint(new Vector3(0, 0, mm));
                button.Poke();
            }
            hand.tip = null;
            PokeButton.ResetCooldown();
        }

        private PokeButton ButtonNamed(string name) =>
            menu.Canvas.GetComponentsInChildren<PokeButton>(true).First(b => b.name == name && b.gameObject.activeInHierarchy);

        [Test]
        public void OpensAtTheStartScreenWithTitleUserIdStartAndCredits()
        {
            Assert.That(menu.IsOpen, Is.True); Assert.That(menu.Current, Is.EqualTo(MainMenu.Screen.Home));
            string texts = Texts(menu.Canvas);
            StringAssert.Contains("GHOST HANDS", texts); StringAssert.Contains(menu.userId, texts);
            StringAssert.Contains("START", texts); StringAssert.Contains("CREDITS", texts);
            Assert.That(panel.Panel.gameObject.activeSelf || panel.Dock.gameObject.activeSelf, Is.False, "no lesson UI at the menu");
        }

        [Test]
        public void EveryLabelIsTallEnoughToShow()
        {
            // TextMeshPro hides a line that doesn't fit its box: that's how the title, names and user ID went missing.
            var lesson = JsonConvert.DeserializeObject<Lesson>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Lessons/example.json")));
            flow.BeginLesson(lesson, hand);
            panel.RefreshView();
            foreach (var canvas in new[] { menu.Canvas, panel.Panel, panel.Dock })
                foreach (var label in canvas.GetComponentsInChildren<TMP_Text>(true))
                    Assert.That(label.rectTransform.rect.height, Is.GreaterThanOrEqualTo(label.fontSize * MenuStyle.LineHeight),
                        canvas.name + " / " + label.transform.parent.name + " / " + label.name);
        }

        [Test]
        public void UserIdIsANumber()
        {
            StringAssert.Contains("98320384", Texts(menu.Canvas));
        }

        [Test]
        public void MenuUsesRajdhani()
        {
            Assert.That(MenuStyle.FontFor(MenuStyle.Weight.Bold).name, Is.EqualTo("Rajdhani Bold"));
        }

        [Test]
        public void MenuFloatsWithinReach()
        {
            menu.Place(head);
            float distance = Vector3.Distance(menu.Canvas.position, head.position);
            Assert.That(distance, Is.InRange(0.4f, 0.6f));
        }

        [Test]
        public void StartOpensTutorialsAndBackReturns()
        {
            Poke(ButtonNamed("Start"));
            Assert.That(menu.Current, Is.EqualTo(MainMenu.Screen.Tutorials));
            Poke(ButtonNamed("Back"));
            Assert.That(menu.Current, Is.EqualTo(MainMenu.Screen.Home));
        }

        [Test]
        public void CreditsListTheTeamWithRoles()
        {
            Poke(ButtonNamed("Credits"));
            Assert.That(menu.Current, Is.EqualTo(MainMenu.Screen.Credits));
            string texts = Texts(menu.Canvas);
            foreach (var person in MainMenu.Team)
            { StringAssert.Contains(person.name, texts); StringAssert.Contains(person.role, texts); }
        }

        [Test]
        public void TutorialsShowEveryLessonAndPickingOneStartsIt()
        {
            menu.Show(MainMenu.Screen.Tutorials);
            menu.ShowTutorials(Lessons(5));
            Assert.That(menu.Carousel.Count, Is.EqualTo(5));
            StringAssert.Contains("5 AVAILABLE", Texts(menu.Canvas));
            // Tap the first card: push in, pull back.
            var view = menu.Carousel.Viewport;
            float x = view.rect.xMin + TutorialCarousel.CardWidth / 2;
            foreach (float mm in new[] { -100f, -40f, 5f, -20f })
            {
                hand.tip = view.TransformPoint(new Vector3(x, 0, mm));
                menu.Carousel.Step(0.016f);
            }
            Assert.That(flow.lessonId, Is.EqualTo("lesson-1"));
            Assert.That(flow.State, Is.EqualTo(LessonState.Loading));
            Assert.That(menu.IsOpen, Is.False, "the menu closes when a lesson starts");
        }

        [Test]
        public void ThePlaceholderLessonIsNotATutorialUnlessItsAllThereIs()
        {
            // What the server lists on October 11, 2026.
            var server = JsonConvert.DeserializeObject<List<LessonSummary>>(
                "[{\"lessonId\":\"asl-17\",\"title\":\"ASL\",\"hands\":[\"right\"],\"stepCount\":5}," +
                "{\"lessonId\":\"count-14\",\"title\":\"Count\",\"hands\":[\"left\",\"right\"],\"stepCount\":5}," +
                "{\"lessonId\":\"example\",\"title\":\"Example lesson (Rubik's cube R U R' U')\",\"hands\":[\"left\",\"right\"],\"stepCount\":5}," +
                "{\"lessonId\":\"paulcube-16\",\"title\":\"PaulCube\",\"hands\":[\"left\",\"right\"],\"stepCount\":2}]");
            menu.Show(MainMenu.Screen.Tutorials);
            menu.ShowTutorials(server);
            Assert.That(menu.Carousel.Count, Is.EqualTo(3));
            string texts = Texts(menu.Canvas);
            StringAssert.Contains("ASL", texts); StringAssert.Contains("Count", texts); StringAssert.Contains("PaulCube", texts);
            StringAssert.DoesNotContain("Rubik", texts);
            StringAssert.Contains("3 AVAILABLE", texts);
            // Offline, the bundled example is all there is: it's still offered.
            Assert.That(MainMenu.Tutorials(server.FindAll(l => l.lessonId == "example")).Count, Is.EqualTo(1));
        }

        [Test]
        public void SwipingScrollsTheRowWithoutPickingACard()
        {
            menu.Show(MainMenu.Screen.Tutorials);
            menu.ShowTutorials(Lessons(5));
            string picked = null;
            menu.Carousel.Selected += lesson => picked = lesson.lessonId;
            var view = menu.Carousel.Viewport;
            var path = new List<Vector3> { new Vector3(60, 0, -100), new Vector3(60, 0, -40), new Vector3(60, 0, 5) };
            for (float x = 50; x >= -60; x -= 10) path.Add(new Vector3(x, 0, 5));  // drag left 120 mm
            path.Add(new Vector3(-60, 0, -20));                                     // let go
            foreach (var point in path) { hand.tip = view.TransformPoint(point); menu.Carousel.Step(0.016f); }
            hand.tip = null;
            for (int i = 0; i < 60; i++) menu.Carousel.Step(0.016f);  // settle
            Assert.That(picked, Is.Null);
            Assert.That(menu.Carousel.Scroll, Is.GreaterThan(TutorialCarousel.Pitch / 2), "the row moved on to a later card");
            Assert.That(menu.Carousel.Scroll % TutorialCarousel.Pitch, Is.LessThan(1).Or.GreaterThan(TutorialCarousel.Pitch - 1),
                "it settles on a card");
        }

        [Test]
        public void ArrowsPageOneCardAndStopAtTheEnds()
        {
            menu.Show(MainMenu.Screen.Tutorials);
            menu.ShowTutorials(Lessons(5));
            var carousel = menu.Carousel;
            Assert.That(carousel.MaxScroll, Is.EqualTo(5 * TutorialCarousel.CardWidth + 4 * TutorialCarousel.Gap - 350).Within(0.01f));
            Poke(ButtonNamed("Next")); carousel.Step(0);
            Assert.That(carousel.Scroll, Is.EqualTo(TutorialCarousel.Pitch));
            for (int i = 0; i < 6; i++) { Poke(ButtonNamed("Next")); carousel.Step(0); }
            Assert.That(carousel.Scroll, Is.EqualTo(carousel.MaxScroll));
            for (int i = 0; i < 6; i++) { Poke(ButtonNamed("Previous")); carousel.Step(0); }
            Assert.That(carousel.Scroll, Is.Zero);
        }

        [Test]
        public void AFewTutorialsSitInTheMiddle()
        {
            menu.Show(MainMenu.Screen.Tutorials);
            menu.ShowTutorials(Lessons(1));
            Assert.That(menu.Carousel.MaxScroll, Is.Zero);
            Assert.That(menu.Carousel.CardAt(Vector2.zero), Is.EqualTo(0), "the only card is in the centre");
        }

        [Test]
        public void TheLessonMenuButtonReturnsToTheMainMenu()
        {
            var lesson = JsonConvert.DeserializeObject<Lesson>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Lessons/example.json")));
            flow.BeginLesson(lesson, hand);
            Assert.That(menu.IsOpen, Is.False);
            panel.RefreshView();
            Assert.That(panel.Dock.gameObject.activeSelf, Is.True);
            panel.MenuButton.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            Assert.That(flow.State, Is.EqualTo(LessonState.Idle));
            Assert.That(menu.IsOpen, Is.True); Assert.That(menu.Current, Is.EqualTo(MainMenu.Screen.Home));
            panel.RefreshView();
            Assert.That(panel.Dock.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void TouchTrackerTellsTapsFromSwipes()
        {
            var rect = new Rect(-100, -50, 200, 100);
            var touch = new TouchTracker();
            Assert.That(touch.Update(new Vector3(0, 0, -40), rect), Is.EqualTo(TouchTracker.Result.None));
            Assert.That(touch.Update(new Vector3(0, 0, 3), rect), Is.EqualTo(TouchTracker.Result.Began));
            Assert.That(touch.Update(new Vector3(5, 0, 3), rect), Is.EqualTo(TouchTracker.Result.None), "a small wobble");
            Assert.That(touch.Update(new Vector3(5, 0, -20), rect), Is.EqualTo(TouchTracker.Result.Tap));

            touch.Update(new Vector3(0, 0, -40), rect); touch.Update(new Vector3(0, 0, 3), rect);
            touch.Update(new Vector3(-30, 0, 3), rect);
            Assert.That(touch.Dragging, Is.True);
            touch.Update(new Vector3(-40, 0, 3), rect);
            Assert.That(touch.DeltaX, Is.EqualTo(-10));
            Assert.That(touch.Update(new Vector3(-40, 0, -20), rect), Is.EqualTo(TouchTracker.Result.DragEnd));

            Assert.That(touch.Update(new Vector3(-300, 0, 3), rect), Is.EqualTo(TouchTracker.Result.None));
            Assert.That(touch.Update(new Vector3(0, 0, 3), rect), Is.EqualTo(TouchTracker.Result.None), "slid in from the side");
        }
    }
}

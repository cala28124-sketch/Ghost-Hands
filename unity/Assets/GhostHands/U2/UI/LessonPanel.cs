using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace GhostHands
{
    // The lesson's heads-up panel, in the main menu's style: translucent glass, content on solid cyan-edged boxes.
    //   Top:    PRACTICE / RECALL chip, lesson name, STEP n / N, and a bar segment per step.
    //   Middle: what to do (the step's instruction) on a solid box.
    //   Below:  how it's going (status, hold timer and bar), each finger's match, and a hint.
    //   End:    the performance overview.
    // Creates its canvases at runtime; leaves the saved scene unchanged.
    [RequireComponent(typeof(LessonFlow))]
    public sealed class LessonPanel : MonoBehaviour
    {
        private const float Width = 600, Height = 380, Inner = 552;  // millimetres
        public static readonly string[] StatNames = { "Accuracy", "Times tried", "Total time spent" };
        private static readonly string[] FingerLabels = { "THUMB", "INDEX", "MIDDLE", "RING", "PINKY" };

        private LessonFlow flow;
        private RectTransform panel, lessonView, resultsView, segmentRow, holdBar, performance;
        private Image glassEdge, boxEdge, chipFill, chipEdge, holdFill;
        private TMP_Text chipLabel, lessonTitle, stepCounter, stepLabel, instruction, status, holdReadout, hint, loading;
        private TMP_Text rating, resultStatus;
        private readonly TMP_Text[] stats = new TMP_Text[3];
        private readonly Image[] fingerEdges = new Image[5];
        private readonly TMP_Text[] fingerLabels = new TMP_Text[5];
        private readonly List<Image> segments = new List<Image>();
        private bool placed;

        // The dock, MENU and RETRY: 0.22 m left, 0.30 m down and 0.40 m ahead of the eyes, within reach of either hand.
        private static readonly Vector3 DockOffset = new Vector3(-0.22f, -0.30f, 0.40f);
        private RectTransform dock, menu, retry;
        private bool dockPlaced;

        public RectTransform Panel => panel;
        public RectTransform Dock => dock;
        public RectTransform MenuButton => menu;
        public RectTransform RetryButton => retry;
        public RectTransform PerformanceView => performance;
        public string RatingText => rating.text;
        public string StatText(int row) => stats[row].text;
        public string InstructionText => instruction.text;
        public string StatusText => status.text;
        public string StepText => stepCounter.text;
        public string HoldText => holdReadout.text;
        public Color FingerColor(int finger) => fingerEdges[finger].color;

        private void Start() => RefreshView();

        private void BuildPanel()
        {
            if (panel != null) return;
            flow = GetComponent<LessonFlow>();
            panel = MenuStyle.WorldCanvas(transform, "Ghost Hands Lesson Panel", new Vector2(Width, Height));
            var glass = MenuStyle.Box(panel, "Glass", Vector2.zero, new Vector2(Width, Height), MenuStyle.Glass,
                MenuStyle.GlassEdge, 1.5f, 24, out _, out glassEdge);
            lessonView = Page(glass, "Lesson"); BuildLesson(lessonView);
            resultsView = Page(glass, "Results"); BuildResults(resultsView);
            loading = MenuStyle.Label(glass, "Loading", "", Vector2.zero, new Vector2(Inner, 40), 26,
                MenuStyle.Weight.SemiBold, MenuStyle.Muted, TextAlignmentOptions.Center, 8);
            BuildDock();
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("Ghost Hands EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
            }
        }

        private static RectTransform Page(RectTransform parent, string name)
        {
            var page = MenuStyle.Node(parent, name, Vector2.zero, Vector2.zero);
            page.anchorMin = Vector2.zero; page.anchorMax = Vector2.one;
            return page;
        }

        private void BuildLesson(RectTransform view)
        {
            const float top = 160, left = -Inner / 2;
            var chip = MenuStyle.Box(view, "Mode", new Vector2(left + 50, top), new Vector2(100, 30), MenuStyle.Accent,
                MenuStyle.Accent, 1, 15, out chipFill, out chipEdge);
            chipLabel = MenuStyle.Label(chip, "Label", "PRACTICE", Vector2.zero, new Vector2(96, 26), 14,
                MenuStyle.Weight.Bold, MenuStyle.Ink, TextAlignmentOptions.Center, 8);
            lessonTitle = MenuStyle.Label(view, "Lesson", "", new Vector2(left + 112 + 125, top), new Vector2(250, 26), 18,
                MenuStyle.Weight.SemiBold, MenuStyle.Text, TextAlignmentOptions.Left, 2);
            lessonTitle.textWrappingMode = TextWrappingModes.NoWrap;
            lessonTitle.overflowMode = TextOverflowModes.Ellipsis;
            stepCounter = MenuStyle.Label(view, "Step", "", new Vector2(-left - 80, top), new Vector2(160, 26), 18,
                MenuStyle.Weight.Bold, MenuStyle.Text, TextAlignmentOptions.Right, 6);
            segmentRow = MenuStyle.Node(view, "Steps", new Vector2(0, 128), new Vector2(Inner, 6));

            var box = MenuStyle.Box(view, "Instruction Box", new Vector2(0, 42), new Vector2(Inner, 130), MenuStyle.BoxFill,
                MenuStyle.AccentDim, 2, 14, out _, out boxEdge);
            stepLabel = MenuStyle.Label(box, "Step Label", "", new Vector2(0, 40), new Vector2(520, 22), 16,
                MenuStyle.Weight.SemiBold, MenuStyle.Accent, TextAlignmentOptions.Center, 10);
            instruction = MenuStyle.Label(box, "Instruction", "", new Vector2(0, -10), new Vector2(520, 84), 32,
                MenuStyle.Weight.Bold, MenuStyle.Text);

            status = MenuStyle.Label(view, "Status", "", new Vector2(-60, -52), new Vector2(Inner - 120, 30), 22,
                MenuStyle.Weight.SemiBold, MenuStyle.Text, TextAlignmentOptions.Left);
            holdReadout = MenuStyle.Label(view, "Hold", "", new Vector2(Inner / 2 - 60, -52), new Vector2(120, 30), 20,
                MenuStyle.Weight.Bold, MenuStyle.Text, TextAlignmentOptions.Right, 4);
            MenuStyle.Fill(MenuStyle.Node(view, "Hold Track", new Vector2(0, -80), new Vector2(Inner, 8)), MenuStyle.AccentDim, 4);
            holdBar = MenuStyle.Node(view, "Hold Bar", new Vector2(left, -80), new Vector2(0, 8));
            holdBar.pivot = new Vector2(0, 0.5f);
            holdFill = MenuStyle.Fill(holdBar, MenuStyle.Good, 4);

            for (int i = 0; i < 5; i++)
            {
                var chipBox = MenuStyle.Box(view, FingerLabels[i], new Vector2(left + 50 + i * 113, -120), new Vector2(100, 30),
                    MenuStyle.BoxFill, MenuStyle.AccentDim, 1.5f, 15, out _, out fingerEdges[i]);
                fingerLabels[i] = MenuStyle.Label(chipBox, "Label", FingerLabels[i], Vector2.zero, new Vector2(96, 26), 13,
                    MenuStyle.Weight.Bold, MenuStyle.Muted, TextAlignmentOptions.Center, 6);
            }
            hint = MenuStyle.Label(view, "Hint", "", new Vector2(0, -160), new Vector2(Inner, 28), 20,
                MenuStyle.Weight.Medium, MenuStyle.Accent);
        }

        // "PERFORMANCE" and the rating out of 10 in large type, then the three stats it's made of on a solid box.
        private void BuildResults(RectTransform view)
        {
            performance = view;
            MenuStyle.Label(view, "Title", "PERFORMANCE", new Vector2(0, 148), new Vector2(Inner, 40), 30,
                MenuStyle.Weight.Bold, MenuStyle.Text, TextAlignmentOptions.Center, 16);
            rating = MenuStyle.Label(view, "Rating", "", new Vector2(0, 66), new Vector2(Inner, 117), 90, MenuStyle.Weight.Bold,
                MenuStyle.Text);
            var box = MenuStyle.Box(view, "Stats", new Vector2(0, -62), new Vector2(440, 126), MenuStyle.BoxFill,
                MenuStyle.Accent, 2, 14, out _, out _);
            for (int row = 0; row < StatNames.Length; row++)
            {
                float y = 40 - 40 * row;
                MenuStyle.Label(box, StatNames[row], StatNames[row].ToUpperInvariant(), new Vector2(-95, y), new Vector2(220, 30),
                    20, MenuStyle.Weight.SemiBold, MenuStyle.Muted, TextAlignmentOptions.Left, 6);
                stats[row] = MenuStyle.Label(box, StatNames[row] + " Value", "", new Vector2(95, y), new Vector2(220, 30), 22,
                    MenuStyle.Weight.Bold, MenuStyle.Text, TextAlignmentOptions.Right);
                if (row > 0) MenuStyle.Line(box, "Rule " + row, new Vector2(0, y + 20), new Vector2(400, 1), MenuStyle.AccentDim);
            }
            resultStatus = MenuStyle.Label(view, "Status", "", new Vector2(0, -150), new Vector2(Inner, 22), 16,
                MenuStyle.Weight.SemiBold, MenuStyle.Muted, TextAlignmentOptions.Center, 4);
        }

        // The panel is too far away to touch, so MENU and RETRY are their own small canvas within arm's reach,
        // pressed with an index fingertip (PokeButton). The mouse still clicks them in the editor.
        private void BuildDock()
        {
            dock = MenuStyle.WorldCanvas(transform, "Ghost Hands Lesson Dock", new Vector2(240, 50));
            Func<IHandJointSource> hands = () => flow.Source;
            menu = (RectTransform)MenuStyle.MakeButton(dock, "Menu", "MENU", new Vector2(-62, 0), new Vector2(110, 44),
                false, 15, hands, flow.Stop).transform;
            retry = (RectTransform)MenuStyle.MakeButton(dock, "Retry", "RETRY", new Vector2(62, 0), new Vector2(110, 44),
                true, 15, hands, flow.Retry).transform;
        }

        private void LateUpdate() => RefreshView();

        public void RefreshView()
        {
            BuildPanel();
            if (flow.State == LessonState.Idle)  // the main menu is showing
            {
                panel.gameObject.SetActive(false);
                dock.gameObject.SetActive(false);
                return;
            }
            var head = flow.Head != null ? flow.Head : Camera.main != null ? Camera.main.transform : null;
            if (head != null)
            {
                var camera = head.GetComponent<Camera>() ?? Camera.main;
                panel.GetComponent<Canvas>().worldCamera = camera;
                Vector3 direction = panel.position - head.position;
                bool outsideView = !placed || direction.magnitude < 0.6f || direction.magnitude > 1.8f ||
                    Vector3.Angle(head.forward, direction) > 30;
                if (camera != null)
                {
                    var viewport = camera.WorldToViewportPoint(panel.position);
                    outsideView |= viewport.z <= 0 || viewport.x < 0.2f || viewport.x > 0.8f || viewport.y < 0.2f || viewport.y > 0.8f;
                }
                if (outsideView) Recenter(head);
                if (DockOutOfReach(head)) PlaceDock(head);
            }
            panel.gameObject.SetActive(head != null);
            dock.gameObject.SetActive(head != null);

            bool results = flow.State == LessonState.Results && flow.Performance != null;
            bool lesson = (flow.State == LessonState.Practice || flow.State == LessonState.Recall) && flow.CurrentStep != null;
            lessonView.gameObject.SetActive(lesson);
            resultsView.gameObject.SetActive(results);
            loading.gameObject.SetActive(!lesson && !results);
            if (lesson) ShowStep();
            else if (results) ShowPerformance(flow.Performance);
            else loading.text = flow.State == LessonState.Loading && flow.Message.StartsWith("Loading") ? "LOADING LESSON…" : flow.Message;
            // A completed step flashes the edges green.
            bool pulse = Time.unscaledTime < flow.PulseUntil;
            glassEdge.color = pulse ? MenuStyle.Good : MenuStyle.GlassEdge;
            boxEdge.color = pulse ? MenuStyle.Good : MenuStyle.AccentDim;
        }

        private void ShowStep()
        {
            var step = flow.CurrentStep;
            bool recall = flow.State == LessonState.Recall, hold = step.mode == "hold";
            var modeColor = recall ? MenuStyle.Warn : MenuStyle.Accent;
            chipFill.color = chipEdge.color = modeColor;
            chipLabel.text = recall ? "RECALL" : "PRACTICE";
            lessonTitle.text = flow.Lesson.title;
            int count = flow.Lesson.steps.Count, index = flow.StepIndex;
            stepCounter.text = $"STEP {index + 1} / {count}";
            ShowSegments(count, index, modeColor);
            stepLabel.text = !string.IsNullOrEmpty(step.label) ? step.label.ToUpperInvariant() :
                hold ? "HOLD THIS POSE" : "FOLLOW THE MOTION";
            instruction.text = step.instruction;

            // How it's going: the status line, the bar and its readout.
            status.text = flow.Message;
            float progress;
            if (flow.InTransition)
            {
                status.color = MenuStyle.Muted;
                progress = flow.TransitionLengthMs <= 0 ? 1 : 1 - flow.TransitionMs / flow.TransitionLengthMs;
                holdFill.color = MenuStyle.AccentDim;
                holdReadout.text = "GET READY";
            }
            else if (hold)
            {
                status.color = StatusColor();
                progress = flow.Progress.HeldMs / Mathf.Max(1, step.holdMs.GetValueOrDefault());
                holdFill.color = MenuStyle.Good;
                holdReadout.text = $"{flow.Progress.HeldMs / 1000:0.0} / {step.holdMs.GetValueOrDefault() / 1000f:0.0} S";
            }
            else
            {
                status.color = MenuStyle.Text;
                progress = flow.Progress.ElapsedMs / Mathf.Max(1, step.durationMs);
                holdFill.color = MenuStyle.Accent;
                holdReadout.text = "MOTION";
            }
            holdBar.sizeDelta = new Vector2(Inner * Mathf.Clamp01(progress), 8);

            // Each finger: the worst status across the hands in this step; neutral while there's nothing to match.
            bool matching = hold && !flow.InTransition && flow.HandMatches.Count > 0;
            for (int i = 0; i < 5; i++)
            {
                var color = MenuStyle.AccentDim;
                if (matching)
                {
                    var worst = FingerStatus.Matched;
                    foreach (var match in flow.HandMatches.Values)
                        if (match.Fingers.TryGetValue(FingerMatcher.FingerNames[i], out var s) && (int)s > (int)worst) worst = s;
                    color = MenuStyle.ForStatus(worst);
                }
                fingerEdges[i].color = color;
                fingerLabels[i].color = matching ? color : MenuStyle.Muted;
            }
            hint.text = flow.Hint ?? "";
        }

        // Green when every finger matches, red when a hand is lost, amber while adjusting.
        private Color StatusColor()
        {
            if (flow.HandMatches.Count == 0) return MenuStyle.Text;
            bool all = true;
            foreach (var match in flow.HandMatches.Values)
            {
                if (!match.Tracked) return MenuStyle.Bad;
                all &= match.AllMatched;
            }
            return all ? MenuStyle.Good : MenuStyle.Warn;
        }

        // One bar segment per step: done, current, still to come.
        private void ShowSegments(int count, int index, Color done)
        {
            if (segments.Count != count)
            {
                foreach (var segment in segments) MenuStyle.Release(segment.gameObject);
                segments.Clear();
                float width = (Inner - (count - 1) * 6f) / count;
                for (int i = 0; i < count; i++)
                    segments.Add(MenuStyle.Fill(MenuStyle.Node(segmentRow, "Step " + (i + 1),
                        new Vector2(-Inner / 2 + width / 2 + i * (width + 6), 0), new Vector2(width, 6)), MenuStyle.AccentDim, 3));
            }
            for (int i = 0; i < count; i++)
                segments[i].color = i < index ? done : i == index ? MenuStyle.Text : MenuStyle.AccentDim;
        }

        private void ShowPerformance(LessonPerformance p)
        {
            var color = p.Rating >= 8 ? MenuStyle.Good : p.Rating >= 5 ? MenuStyle.Warn : MenuStyle.Bad;
            rating.text = $"<color={MenuStyle.Hex(color)}>{p.Rating:0.0}</color>" +
                $"<size=40%><color={MenuStyle.Hex(MenuStyle.Muted)}> / 10</color></size>";
            stats[0].text = Mathf.RoundToInt(100 * p.Accuracy) + "%";
            stats[1].text = p.TimesTried + (p.Retries == 0 ? "" : $" <size=70%><color={MenuStyle.Hex(MenuStyle.Muted)}>" +
                $"({p.Poses} poses + {p.Retries} {(p.Retries == 1 ? "retry" : "retries")})</color></size>");
            stats[2].text = LessonPerformance.FormatTime(p.TotalMs);
            resultStatus.text = flow.Message.ToUpperInvariant();
        }

        public void Recenter(Transform head)
        {
            if (head == null) return;
            BuildPanel();
            panel.position = head.TransformPoint(new Vector3(0, 0.12f, 1.2f));
            panel.rotation = head.rotation;
            placed = true;
        }

        // Low and to the left, out of the way of the hands doing the lesson. It stays put while the learner looks
        // or reaches for it, and only follows when they walk away or turn around.
        private bool DockOutOfReach(Transform head)
        {
            if (!dockPlaced) return true;
            var direction = Vector3.ProjectOnPlane(dock.position - head.position, Vector3.up);
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            return direction.magnitude > 0.9f || Vector3.Angle(forward, direction) > 100;
        }

        public void PlaceDock(Transform head)
        {
            if (head == null) return;
            BuildPanel();
            dock.position = head.position + Quaternion.Euler(0, head.eulerAngles.y, 0) * DockOffset;
            dock.rotation = Quaternion.LookRotation(dock.position - head.position);  // facing the eyes
            dockPlaced = true;
        }
    }
}

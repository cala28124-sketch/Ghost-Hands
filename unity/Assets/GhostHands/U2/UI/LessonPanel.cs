using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace GhostHands
{
    // Creates its Canvas at runtime; leaves the saved scene unchanged.
    [RequireComponent(typeof(LessonFlow))]
    public sealed class LessonPanel : MonoBehaviour
    {
        private LessonFlow flow;
        private RectTransform panel;
        private TMP_Text heading, body, feedback;
        private UnityEngine.UI.Image background;
        private bool placed;
        public RectTransform Panel => panel;

        private void Start() => RefreshView();

        private void BuildPanel()
        {
            if (panel != null) return;
            flow = GetComponent<LessonFlow>();
            var root = new GameObject("Ghost Hands Lesson Panel", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster), typeof(UnityEngine.UI.Image));
            panel = root.GetComponent<RectTransform>();
            panel.SetParent(transform, false);
            panel.sizeDelta = new Vector2(700, 460);
            panel.localScale = Vector3.one * 0.001f;
            root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            background = root.GetComponent<UnityEngine.UI.Image>();
            background.color = new Color(0.035f, 0.06f, 0.08f, 0.94f);
            background.raycastTarget = false;
            heading = Label("Heading", 160, 38);
            body = Label("Instruction", 55, 29);
            feedback = Label("Feedback", -70, 24);
            var buttonObject = new GameObject("Retry", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
            var rect = buttonObject.GetComponent<RectTransform>();
            rect.SetParent(panel, false); rect.sizeDelta = new Vector2(220, 60); rect.anchoredPosition = new Vector2(0, -185);
            buttonObject.GetComponent<UnityEngine.UI.Image>().color = new Color(0.1f, 0.35f, 0.42f);
            buttonObject.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(flow.Retry);
            var caption = new GameObject("Caption", typeof(RectTransform), typeof(TextMeshProUGUI));
            var captionRect = caption.GetComponent<RectTransform>();
            captionRect.SetParent(rect, false); captionRect.anchorMin = Vector2.zero; captionRect.anchorMax = Vector2.one;
            captionRect.sizeDelta = Vector2.zero;
            var text = caption.GetComponent<TextMeshProUGUI>(); text.text = "Retry lesson"; text.fontSize = 26;
            text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("Ghost Hands EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
            }
        }

        private TMP_Text Label(string name, float y, float size)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rect = obj.GetComponent<RectTransform>(); rect.SetParent(panel, false);
            rect.sizeDelta = new Vector2(650, 105); rect.anchoredPosition = new Vector2(0, y);
            var text = obj.GetComponent<TextMeshProUGUI>(); text.fontSize = size; text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
            return text;
        }

        private void LateUpdate() => RefreshView();

        public void RefreshView()
        {
            BuildPanel();
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
            }
            panel.gameObject.SetActive(head != null);
            var step = flow.CurrentStep;
            bool results = flow.State == LessonState.Results;
            body.rectTransform.sizeDelta = new Vector2(650, results ? 210 : 105);
            body.rectTransform.anchoredPosition = new Vector2(0, results ? 0 : 55);
            heading.text = flow.State == LessonState.Results ? "Lesson results" :
                flow.State + (step == null ? "" : " · Step " + (flow.StepIndex + 1) + " of " + flow.Lesson.steps.Count +
                    (string.IsNullOrEmpty(step.label) ? "" : " · " + step.label));
            body.text = flow.State == LessonState.Results ? flow.Message : step?.instruction ?? flow.Message;
            feedback.text = flow.State == LessonState.Results ? "" : flow.Message +
                (step?.mode == "hold" ? "\nHold: " + (flow.Progress.HeldMs / 1000).ToString("0.0") + " / " +
                    (step.holdMs.Value / 1000f).ToString("0.0") + " s" : "") +
                (string.IsNullOrEmpty(flow.Hint) ? "" : "\n" + flow.Hint);
            background.color = Time.unscaledTime < flow.PulseUntil ? new Color(0.05f, 0.4f, 0.25f, 0.94f) :
                new Color(0.035f, 0.06f, 0.08f, 0.94f);
        }

        public void Recenter(Transform head)
        {
            if (head == null) return;
            BuildPanel();
            panel.position = head.TransformPoint(new Vector3(0, 0.12f, 1.2f));
            panel.rotation = head.rotation;
            placed = true;
        }
    }
}

using System.Collections;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

namespace GhostHands
{
    // Standalone alignment tool. The integrated bootstrap never runs this beside LessonFlow.
    [RequireComponent(typeof(GhostHandRenderer))]
    public sealed class GhostDemo : MonoBehaviour
    {
        public MonoBehaviour handJointSource;
        public bool autoLoad = true;
        public Lesson Lesson { get; private set; }
        public int StepIndex { get; private set; }
        public LessonStep CurrentStep => Lesson?.steps[StepIndex];
        public string Message { get; private set; } = "Loading alignment lesson…";
        private GhostHandRenderer ghost;
        private bool wasPinching;

        private IEnumerator Start()
        {
            ghost = GetComponent<GhostHandRenderer>(); ghost.handJointSource = handJointSource;
            if (!autoLoad) yield break;
            var path = Path.Combine(Application.streamingAssetsPath, "Lessons/example.json");
            string url = path.Contains("://") ? path : new System.Uri(path).AbsoluteUri;
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = 3;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                { Message = "Alignment lesson failed: " + request.error; Debug.LogWarning(Message); yield break; }
                Lesson parsed = null;
                try { parsed = JsonConvert.DeserializeObject<Lesson>(request.downloadHandler.text); }
                catch (JsonException ex) { Message = "Alignment lesson JSON failed: " + ex.Message; }
                if (parsed == null || parsed.schemaVersion != 2 || parsed.steps == null || parsed.steps.Count == 0)
                { Debug.LogWarning(Message = "Alignment demo requires a schema v2 lesson with steps."); yield break; }
                BeginLesson(parsed);
            }
        }

        public void BeginLesson(Lesson lesson)
        {
            if (lesson == null || lesson.schemaVersion != 2 || lesson.steps == null || lesson.steps.Count == 0)
                throw new System.ArgumentException("Alignment demo requires a schema v2 lesson with steps.");
            ghost = GetComponent<GhostHandRenderer>(); ghost.handJointSource = handJointSource;
            Lesson = lesson; StepIndex = 0; wasPinching = false;
            Show(null);
        }

        public void NextStep() => Move(1);
        public void PreviousStep() => Move(-1);
        private void Move(int direction)
        {
            if (Lesson == null) return;
            var previous = CurrentStep;
            StepIndex = (StepIndex + direction + Lesson.steps.Count) % Lesson.steps.Count;
            Show(previous);
        }
        private void Show(LessonStep previous)
        {
            ghost.ShowStep(previous, CurrentStep); ghost.SetVisible(true);
            Message = "Alignment · Step " + (StepIndex + 1) + " of " + Lesson.steps.Count + " · " + CurrentStep.instruction;
            Debug.Log(Message + " (A / pinch / N: next; B / P: previous)");
        }

        private void Update()
        {
            if (Lesson == null) return;
            bool next = OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch) ||
                (Keyboard.current != null && Keyboard.current.nKey.wasPressedThisFrame);
            bool previous = OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch) ||
                (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame);
            bool pinching = false;
            var source = handJointSource as IHandJointSource;
            if (source != null)
                for (int side = 0; side < 2; side++)
                    if (source.TryGetJoints(side == 1, out var p) && p != null && p.Length == 21)
                        pinching |= Vector3.Distance(p[4], p[8]) < 0.025f;
            if (pinching && !wasPinching) next = true;
            wasPinching = pinching;
            if (previous) PreviousStep(); else if (next) NextStep();
        }
    }
}

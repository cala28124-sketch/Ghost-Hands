using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GhostHands
{
    public enum LessonState { Loading, Practice, Recall, Results }

    [RequireComponent(typeof(LessonLoader))]
    public sealed class LessonFlow : MonoBehaviour
    {
        public string lessonId = "example";
        public string tester = "Justin";
        public bool autoStart = true;
        public bool checkLabeledWrist = true;
        public bool submitResults = true;
        public MonoBehaviour handJointSource;
        public MonoBehaviour ghostHandRenderer;
        public FingerTolerances tolerances = new FingerTolerances();
        public AudioSource successAudio;
        public LessonState State { get; private set; } = LessonState.Loading;
        public Lesson Lesson { get; private set; }
        public int StepIndex { get; private set; }
        public LessonStep CurrentStep => Lesson != null && StepIndex < Lesson.steps.Count ? Lesson.steps[StepIndex] : null;
        public StepProgress Progress { get; private set; }
        public string Message { get; private set; } = "Loading lesson…";
        public string Hint { get; private set; }
        public float PulseUntil { get; private set; }
        public bool RecallPassed { get; private set; }
        public SessionResults Results { get; private set; }
        public readonly Dictionary<string, HandMatch> HandMatches = new Dictionary<string, HandMatch>();
        public event Action<LessonStep> StepCompleted;
        public event Action<LessonState> StateChanged;
        public Transform Head => source?.Head;
        private IHandJointSource source;
        private LessonLoader loader;
        private FingerMatcher leftMatcher, rightMatcher;
        private GhostRendererAdapter ghost;
        private MonoBehaviour boundRenderer;
        private float nextBindTime;
        private bool ticking;

        private void Awake()
        {
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (leftMatcher != null) return;
            loader = GetComponent<LessonLoader>();
            BindComponents();
            leftMatcher = new FingerMatcher(tolerances);
            rightMatcher = new FingerMatcher(tolerances);
        }

        private void Start() { if (autoStart) Retry(); }

        public void Retry()
        {
            EnsureInitialized();
            StopAllCoroutines();
            ghost?.Hide();
            Lesson = null;
            StepIndex = 0;
            Results = null;
            Progress = null;
            RecallPassed = false;
            PulseUntil = 0;
            leftMatcher.Reset();
            rightMatcher.Reset();
            HandMatches.Clear();
            Hint = null;
            Message = "Loading lesson…";
            SetState(LessonState.Loading);
            StartCoroutine(Load());
        }

        private IEnumerator Load()
        {
            yield return loader.LoadLesson(lessonId, lesson => BeginLesson(lesson, source), error => Message = error);
        }

        public void BeginLesson(Lesson lesson, IHandJointSource jointSource)
        {
            EnsureInitialized();
            var error = LessonLoader.Validate(lesson);
            if (error != null) throw new ArgumentException(error);
            source = jointSource;
            Lesson = lesson;
            StepIndex = 0;
            RecallPassed = true;
            ResetResults();
            SetState(LessonState.Practice);
            EnterStep(null);
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextBindTime)
            { BindComponents(); nextBindTime = Time.unscaledTime + 1; }
            Tick(Time.unscaledDeltaTime * 1000);
        }

        public void Tick(float deltaMs)
        {
            if (ticking) return;
            ticking = true;
            try { Advance(deltaMs); }
            finally { ticking = false; }
        }

        private void Advance(float deltaMs)
        {
            if (State != LessonState.Practice && State != LessonState.Recall) return;
            var step = CurrentStep;
            bool tracked = true, allMatched = true;
            Hint = null;
            HandMatches.Clear();
            if (step.mode == "hold")
            {
                Quaternion? yaw = Head != null ? Quaternion.Euler(0, Head.eulerAngles.y, 0) : (Quaternion?)null;
                float worstError = -1;
                foreach (var hand in step.hands)
                {
                    bool right = hand.Key == "right";
                    Vector3[] joints = null;
                    if (source != null && !source.TryGetJoints(right, out joints)) joints = null;
                    var match = (right ? rightMatcher : leftMatcher).Match(hand.Value, joints, right, yaw,
                        checkLabeledWrist && !string.IsNullOrEmpty(step.label));
                    HandMatches[hand.Key] = match;
                    tracked &= match.Tracked;
                    allMatched &= match.AllMatched;
                    foreach (var finger in FingerMatcher.FingerNames)
                    {
                        if (match.Errors.TryGetValue(finger, out float error) && error / Mathf.Max(1, tolerances.ForFinger(finger)) > worstError)
                        {
                            worstError = error / Mathf.Max(1, tolerances.ForFinger(finger));
                            Hint = string.IsNullOrEmpty(match.Hint) ? null : (right ? "Right hand: " : "Left hand: ") + match.Hint;
                        }
                    }
                }
                // U1's API has no hand argument: show the worst status across the required hands.
                foreach (var finger in FingerMatcher.FingerNames)
                {
                    FingerStatus combined = FingerStatus.Matched;
                    foreach (var match in HandMatches.Values)
                        if ((int)match.Fingers[finger] > (int)combined) combined = match.Fingers[finger];
                    ghost?.SetFingerStatus(finger, combined);
                }
                Message = !tracked ? "Hands not tracked — hold timer reset" :
                    !allMatched ? (HasWristMismatch() ? "Match the finger pose and wrist rotation" : "Match the finger pose") : "Hold steady";
            }
            else Message = State == LessonState.Recall ? "Continue through this motion" : "Watch the ghost motion";
            if (!Progress.Tick(step, deltaMs, tracked, allMatched, State == LessonState.Recall)) return;
            OnStepFinished(step);
            bool passed = Progress.Passed;
            if (passed)
            {
                PulseUntil = Time.unscaledTime + 0.25f;
                if (successAudio != null) successAudio.Play();
            }
            StepIndex++;
            if (StepIndex < Lesson.steps.Count) EnterStep(step);
            else OnRoundFinished();
            // Notify after advancement so listeners cannot advance the completed step a second time.
            if (passed) StepCompleted?.Invoke(step);
        }

        private bool HasWristMismatch()
        {
            foreach (var match in HandMatches.Values) if (!match.WristMatched) return true;
            return false;
        }

        private void EnterStep(LessonStep previous)
        {
            Progress = new StepProgress();
            leftMatcher.Reset(); rightMatcher.Reset();
            HandMatches.Clear(); Hint = null;
            ghost?.ShowStep(previous, CurrentStep);
            ghost?.SetVisible(State == LessonState.Practice);
        }

        public void RefreshBindings() => BindComponents();

        private void BindComponents()
        {
            if (source is UnityEngine.Object sourceObject && sourceObject == null) source = null;
            if (handJointSource != null) source = handJointSource as IHandJointSource;
            if (source == null)
                foreach (var component in FindObjectsByType<MonoBehaviour>())
                    if (component is IHandJointSource joints) { source = joints; break; }
            if (ghostHandRenderer == null)
                foreach (var component in FindObjectsByType<MonoBehaviour>())
                    if (component.GetType().Name == "GhostHandRenderer") { ghostHandRenderer = component; break; }
            if (ghost != null && boundRenderer == ghostHandRenderer) return;
            boundRenderer = ghostHandRenderer;
            ghost = new GhostRendererAdapter(boundRenderer);
            if (State == LessonState.Practice || State == LessonState.Recall)
            {
                ghost.ShowStep(StepIndex == 0 ? null : Lesson.steps[StepIndex - 1], CurrentStep);
                ghost.SetVisible(State == LessonState.Practice);
            }
            else ghost.Hide();
        }

        private void SetState(LessonState value) { State = value; StateChanged?.Invoke(value); }
        private void ResetResults()
        {
            Results = new SessionResults { lessonId = Lesson.lessonId, tester = tester };
        }
        private void OnStepFinished(LessonStep step)
        {
            if (State != LessonState.Recall) return;
            Results.steps.Add(new StepResult { stepId = step.id, timeToMatchMs = Mathf.RoundToInt(Progress.ElapsedMs),
                retries = Progress.Retries, trackingLosses = Progress.TrackingLosses });
            if (step.mode == "hold" && !Progress.Passed) RecallPassed = false;
        }
        private void OnRoundFinished()
        {
            if (State == LessonState.Practice)
            {
                StepIndex = 0;
                SetState(LessonState.Recall);
                ghost?.SetVisible(false);
                Message = "Recall: repeat the lesson with the ghost hidden";
                EnterStep(null);
                return;
            }
            Results.completed = true;
            Results.recallPassed = RecallPassed;
            SetState(LessonState.Results);
            ghost?.Hide();
            Message = RecallPassed ? "Recall passed" : "Recall finished — a hold exceeded 20 seconds";
            Message += "\n" + Results.steps.Count + " recall steps completed";
            int retries = 0, losses = 0, totalMs = 0;
            foreach (var result in Results.steps)
            { retries += result.retries; losses += result.trackingLosses; totalMs += result.timeToMatchMs; }
            Message += "\n" + (totalMs / 1000f).ToString("0.0") + " s · " + retries + " retries · " + losses + " tracking losses";
            if (submitResults) StartCoroutine(Submit());
        }

        private IEnumerator Submit()
        {
            string summary = Message;
            Message = summary + "\nSaving results…";
            yield return ResultsClient.Post(loader.ServerUrl, Results,
                id => Message = summary + "\nSaved session " + id,
                error => Message = summary + "\nCould not save to server; results kept here and in the log");
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GhostHands
{
    // Idle: no lesson running (the main menu is showing).
    public enum LessonState { Loading, Practice, Recall, Results, Idle }

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
        [Tooltip("In practice, the ghost moves to each new pose over the expert's move time, kept within these limits. " +
            "Tracking starts when it lands. 0 and 0: no transition.")]
        [Min(0)] public float minTransitionSeconds = GhostTransition.MinSeconds;
        [Min(0)] public float maxTransitionSeconds = GhostTransition.MaxSeconds;
        public AudioSource successAudio;
        public LessonState State { get; private set; } = LessonState.Loading;
        public Lesson Lesson { get; private set; }
        public int StepIndex { get; private set; }
        public LessonStep CurrentStep => Lesson != null && StepIndex < Lesson.steps.Count ? Lesson.steps[StepIndex] : null;
        public StepProgress Progress { get; private set; }
        public string Message { get; private set; } = "Loading lesson…";
        public string Hint { get; private set; }
        public float PulseUntil { get; private set; }
        // Time left while the ghost moves to the current step's pose. Nothing is tracked or scored until it's 0.
        public float TransitionMs { get; private set; }
        public bool InTransition => TransitionMs > 0;
        public float TransitionLengthMs => stepTransitionMs;  // the current step's whole transition
        public bool RecallPassed { get; private set; }
        public SessionResults Results { get; private set; }
        public LessonPerformance Performance { get; private set; }
        public readonly Dictionary<string, HandMatch> HandMatches = new Dictionary<string, HandMatch>();
        public event Action<LessonStep> StepCompleted;
        public event Action<LessonState> StateChanged;
        public Transform Head => source?.Head;
        public IHandJointSource Source => source;
        private IHandJointSource source;
        private LessonLoader loader;
        private FingerMatcher leftMatcher, rightMatcher;
        private GhostRendererAdapter ghost;
        private float stepTransitionMs, stepClosenessMs, stepMatchedMs;  // this step, for the performance overview
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
            Performance = null;
            Progress = null;
            RecallPassed = false;
            PulseUntil = 0;
            TransitionMs = 0;
            leftMatcher.Reset();
            rightMatcher.Reset();
            HandMatches.Clear();
            Hint = null;
            Message = "Loading lesson…";
            SetState(LessonState.Loading);
            StartCoroutine(Load());
        }

        // From the main menu: load and play this lesson.
        public void StartLesson(string id)
        {
            lessonId = id;
            Retry();
        }

        // Back to the main menu: stop the lesson and hide the ghost.
        public void Stop()
        {
            EnsureInitialized();
            StopAllCoroutines();
            ghost?.Hide();
            Lesson = null;
            StepIndex = 0;
            Results = null;
            Performance = null;
            Progress = null;
            TransitionMs = 0;
            HandMatches.Clear();
            Hint = null;
            Message = "";
            SetState(LessonState.Idle);
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
            Performance = new LessonPerformance();
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
            Performance.AddTime(deltaMs);
            if (InTransition)
            {
                // Only the ghost moves: no matching, no hold timer, no tracking-loss count. The fingers stay cyan.
                TransitionMs = Mathf.Max(0, TransitionMs - deltaMs);
                Message = "Watch the ghost move to the next pose";
                return;
            }
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
                if (tracked && allMatched)
                {
                    stepClosenessMs += deltaMs * LessonPerformance.Closeness(MeanErrorRatio());
                    stepMatchedMs += deltaMs;
                }
            }
            else Message = State == LessonState.Recall ? "Continue through this motion" : "Watch the ghost motion";
            if (!Progress.Tick(step, deltaMs, tracked, allMatched, State == LessonState.Recall)) return;
            OnStepFinished(step);
            bool passed = Progress.Passed;
            if (step.mode == "hold")
                Performance.AddPose(passed, stepMatchedMs > 0 ? stepClosenessMs / stepMatchedMs : 0, Progress.Retries,
                    stepTransitionMs + step.holdMs.GetValueOrDefault() + LessonPerformance.FindPoseAllowanceMs);
            else Performance.AddMotion(step.durationMs);
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

        // Each finger's error as a fraction of its tolerance, averaged over the fingers of the hands in this step.
        private float MeanErrorRatio()
        {
            float sum = 0; int count = 0;
            foreach (var match in HandMatches.Values)
                foreach (var pair in match.Errors)
                { sum += pair.Value / Mathf.Max(1, tolerances.ForFinger(pair.Key)); count++; }
            return count == 0 ? 0 : sum / count;
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
            // Only practice shows the move; recall hides the ghost, so it tracks straight away.
            float seconds = GhostTransition.Seconds(previous, CurrentStep, minTransitionSeconds, maxTransitionSeconds);
            TransitionMs = State == LessonState.Practice && CurrentStep.mode == "hold" ? Mathf.Round(seconds * 1000) : 0;
            stepTransitionMs = TransitionMs; stepClosenessMs = 0; stepMatchedMs = 0;
            ghost?.ShowStep(previous, CurrentStep, seconds);
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
                ghost.ShowStep(StepIndex == 0 ? null : Lesson.steps[StepIndex - 1], CurrentStep, TransitionMs / 1000);
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
            // The panel shows the performance overview; this line goes under it.
            Message = RecallPassed ? "Recall passed" : "Recall finished — a pose took over 20 seconds";
            var p = Performance;
            Debug.Log($"[GhostHands] {Lesson.lessonId}: performance {p.Rating:0.0}/10 — accuracy {p.Accuracy:P0}, " +
                $"{p.TimesTried} tries for {p.Poses} poses, {LessonPerformance.FormatTime(p.TotalMs)} (par {LessonPerformance.FormatTime(p.ParMs)})");
            if (submitResults) StartCoroutine(Submit());
        }

        private IEnumerator Submit()
        {
            string summary = Message;
            Message = summary + " · Saving results…";
            yield return ResultsClient.Post(loader.ServerUrl, Results,
                id => Message = summary + " · Saved session " + id,
                error => Message = summary + " · Not saved to the server (kept in the log)");
        }
    }
}

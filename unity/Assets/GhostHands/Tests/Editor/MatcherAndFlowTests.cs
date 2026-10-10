using System;
using System.IO;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace GhostHands
{
    public sealed class MatcherAndFlowTests
    {
        private Lesson lesson;
        private GameObject root;
        private LessonFlow flow;
        private FakeSource source;
        private FakeGhostRenderer renderer;

        private sealed class FakeSource : IHandJointSource
        {
            public Transform Head { get; set; }
            public LessonFlow Flow;
            public bool Tracked = true;
            public bool RightTracked = true;
            public bool Incorrect;
            public bool TryGetJoints(bool isRight, out Vector3[] worldJoints21)
            {
                worldJoints21 = null;
                if (!Tracked || (isRight && !RightTracked) || Flow.CurrentStep == null ||
                    !Flow.CurrentStep.hands.TryGetValue(isRight ? "right" : "left", out var hand)) return false;
                worldJoints21 = ReplayJointSource.WorldPose(hand, isRight, Head);
                if (Incorrect) BendIndex(worldJoints21, isRight, 80);
                return true;
            }
        }

        [SetUp]
        public void SetUp()
        {
            lesson = JsonConvert.DeserializeObject<Lesson>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Lessons/example.json")));
            root = new GameObject("U2 Test") { hideFlags = HideFlags.HideAndDontSave };
            flow = root.AddComponent<LessonFlow>(); flow.autoStart = false; flow.submitResults = false;
            renderer = root.AddComponent<FakeGhostRenderer>(); flow.ghostHandRenderer = renderer;
            source = new FakeSource { Head = root.transform, Flow = flow };
            flow.BeginLesson(lesson, source);
        }

        [TearDown] public void TearDown() { UnityEngine.Object.DestroyImmediate(root); }

        [Test]
        public void OwnExampleJointsMatchEveryStepAndHand()
        {
            foreach (var step in lesson.steps)
                foreach (var hand in step.hands)
                {
                    bool right = hand.Key == "right";
                    var matcher = new FingerMatcher();
                    var match = matcher.Match(hand.Value, hand.Value.JointVectors(), right);
                    foreach (var finger in FingerMatcher.FingerNames)
                        Assert.That(match.Fingers[finger], Is.EqualTo(FingerStatus.Matched), step.id + " " + hand.Key + " " + finger);
                }
        }

        [TestCase(false)] [TestCase(true)]
        public void FortyDegreeIndexBendOnlyChangesIndex(bool right)
        {
            var hand = lesson.steps[0].hands[right ? "right" : "left"];
            var joints = hand.JointVectors();
            BendIndex(joints, right, 40);
            var match = new FingerMatcher().Match(hand, joints, right);
            foreach (var finger in FingerMatcher.FingerNames)
                Assert.That(match.Fingers[finger], Is.EqualTo(finger == "index" ? FingerStatus.Incorrect : FingerStatus.Matched), finger);
            Assert.That(match.Hint, Is.EqualTo("Straighten your index"));
        }

        private static void BendIndex(Vector3[] joints, bool right, float degrees)
        {
            var axis = HandConventions.HandFrame(joints, right) * Vector3.right;
            var rotation = Quaternion.AngleAxis(degrees, axis);
            for (int i = 6; i <= 8; i++) joints[i] = joints[5] + rotation * (joints[i] - joints[5]);
        }

        [Test]
        public void SmoothingConvergesAndResetsAfterTrackingLoss()
        {
            var hand = lesson.steps[0].hands["right"];
            var matcher = new FingerMatcher(); var joints = hand.JointVectors();
            matcher.Match(hand, joints, true);
            BendIndex(joints, true, 80);
            Assert.That(matcher.Match(hand, joints, true).Errors["index"], Is.LessThan(40));
            for (int i = 0; i < 25; i++) matcher.Match(hand, joints, true);
            Assert.That(matcher.Match(hand, joints, true).Fingers["index"], Is.EqualTo(FingerStatus.Incorrect));
            Assert.That(matcher.Match(hand, null, true).Fingers["index"], Is.EqualTo(FingerStatus.Unavailable));
            Assert.That(matcher.Match(hand, hand.JointVectors(), true).AllMatched, Is.True);
        }

        [Test]
        public void AnglesAreInvariantUnderTranslationRotationAndScale()
        {
            var hand = lesson.steps[0].hands["left"]; var joints = hand.JointVectors();
            var rotation = Quaternion.Euler(34, 77, -52);
            for (int i = 0; i < joints.Length; i++) joints[i] = rotation * joints[i] * 1.3f + new Vector3(2, -1, 5);
            Assert.That(new FingerMatcher().Match(hand, joints, false).AllMatched, Is.True);
        }

        [Test]
        public void WristCheckUsesHeadYawAndBlocksWrongRotation()
        {
            var hand = lesson.steps[1].hands["right"];
            root.transform.rotation = Quaternion.Euler(20, 45, 0);
            var joints = ReplayJointSource.WorldPose(hand, true, root.transform);
            var yaw = Quaternion.Euler(0, 45, 0);
            Assert.That(new FingerMatcher().Match(hand, joints, true, yaw, true).AllMatched, Is.True);
            for (int i = 0; i < joints.Length; i++) joints[i] = joints[0] + Quaternion.Euler(0, 90, 0) * (joints[i] - joints[0]);
            var result = new FingerMatcher().Match(hand, joints, true, yaw, true);
            Assert.That(result.WristMatched, Is.False);
            foreach (var status in result.Fingers.Values) Assert.That(status, Is.EqualTo(FingerStatus.Matched));
        }

        [Test]
        public void NullAndDegenerateJointsAreUnavailable()
        {
            var matcher = new FingerMatcher();
            foreach (var joints in new[] { null, new Vector3[21], new Vector3[20] })
                Assert.That(matcher.Match(lesson.steps[0].hands["right"], joints, true).Tracked, Is.False);
        }

        [Test]
        public void HoldAdvancesExactlyOnceAtThreshold()
        {
            var progress = new StepProgress(); var step = lesson.steps[0];
            Assert.That(progress.Tick(step, 699, true, true, false), Is.False);
            Assert.That(progress.Tick(step, 1, true, true, false), Is.True);
            Assert.That(progress.Tick(step, 1000, true, true, false), Is.False);
        }

        [Test]
        public void BreakingToleranceResetsAndCountsOncePerBreak()
        {
            var progress = new StepProgress(); var step = lesson.steps[0];
            progress.Tick(step, 600, true, true, false);
            progress.Tick(step, 20, true, false, false);
            progress.Tick(step, 20, true, false, false);
            Assert.That(progress.HeldMs, Is.Zero); Assert.That(progress.Retries, Is.EqualTo(1));
            Assert.That(progress.Tick(step, 100, true, true, false), Is.False);
        }

        [Test]
        public void PassAdvancesAfterDurationRegardlessOfTracking()
        {
            var step = lesson.steps[2]; var progress = new StepProgress();
            Assert.That(progress.Tick(step, step.durationMs - 1, false, false, false), Is.False);
            Assert.That(progress.Tick(step, 1, false, false, false), Is.True);
            Assert.That(progress.TrackingLosses, Is.Zero);
            Assert.That(progress.Tick(step, 1, false, false, false), Is.False);
        }

        [Test]
        public void TrackingLossResetsAndCountsEpisodes()
        {
            var progress = new StepProgress(); var step = lesson.steps[0];
            progress.Tick(step, 600, true, true, false);
            progress.Tick(step, 50, false, false, false);
            progress.Tick(step, 50, false, false, false);
            Assert.That(progress.HeldMs, Is.Zero); Assert.That(progress.TrackingLosses, Is.EqualTo(1));
            progress.Tick(step, 100, true, true, false);
            progress.Tick(step, 50, false, false, false);
            Assert.That(progress.TrackingLosses, Is.EqualTo(2));
        }

        [Test]
        public void LiveToleranceBreakResetsFlowHoldAndCountsRetry()
        {
            flow.Tick(300);
            source.Incorrect = true;
            flow.Tick(100);
            Assert.That(flow.StepIndex, Is.Zero);
            Assert.That(flow.Progress.HeldMs, Is.Zero);
            Assert.That(flow.Progress.Retries, Is.EqualTo(1));
            for (int i = 0; i < 20; i++) flow.Tick(20);
            Assert.That(flow.StepIndex, Is.Zero);
            Assert.That(flow.Progress.Retries, Is.EqualTo(1));
        }

        [Test]
        public void TwoHandsMustBothMatchContinuously()
        {
            source.RightTracked = false; flow.Tick(1000);
            Assert.That(flow.StepIndex, Is.Zero); Assert.That(flow.Progress.TrackingLosses, Is.EqualTo(1));
            source.RightTracked = true; flow.Tick(699); Assert.That(flow.StepIndex, Is.Zero);
            flow.Tick(1); Assert.That(flow.StepIndex, Is.EqualTo(1));
        }

        [Test]
        public void ReplayCompletesAllFiveStepsThenRecallAndResults()
        {
            int pulses = 0; flow.StepCompleted += step => pulses++;
            for (int i = 0; i < 10; i++)
            {
                var step = flow.CurrentStep;
                flow.Tick(step.mode == "pass" ? step.durationMs : step.holdMs.Value);
                if (i == 4)
                {
                    Assert.That(flow.State, Is.EqualTo(LessonState.Recall));
                    Assert.That(renderer.Visible, Is.False);
                }
            }
            Assert.That(flow.State, Is.EqualTo(LessonState.Results));
            Assert.That(flow.Results.completed, Is.True); Assert.That(flow.Results.recallPassed, Is.True);
            Assert.That(flow.Results.steps.Count, Is.EqualTo(5)); Assert.That(pulses, Is.EqualTo(10));
            Assert.That(renderer.ShownSteps, Is.EqualTo(10)); Assert.That(renderer.Hidden, Is.True);
            flow.Tick(1000); Assert.That(pulses, Is.EqualTo(10));
        }

        [Test]
        public void RecallTimeoutFailsAndContinuesToResults()
        {
            for (int i = 0; i < 5; i++) flow.Tick(1000);
            source.Tracked = false; flow.Tick(20000);
            Assert.That(flow.RecallPassed, Is.False); Assert.That(flow.StepIndex, Is.EqualTo(1));
            Assert.That(flow.Results.steps[0].trackingLosses, Is.EqualTo(1));
            source.Tracked = true;
            for (int i = 0; i < 4; i++) flow.Tick(1000);
            Assert.That(flow.State, Is.EqualTo(LessonState.Results)); Assert.That(flow.Results.recallPassed, Is.False);
        }

        [Test]
        public void RecallCannotSucceedAfterDeadline()
        {
            var progress = new StepProgress(); var step = lesson.steps[0];
            progress.Tick(step, 19900, true, false, true);
            Assert.That(progress.Tick(step, 700, true, true, true), Is.True);
            Assert.That(progress.Passed, Is.False);
        }

        [Test]
        public void InvalidLessonsAreRejectedWithReason()
        {
            Assert.That(LessonLoader.TryParse("{bad", out _, out var reason), Is.False); Assert.That(reason, Does.Contain("JSON"));
            lesson.steps[0].holdMs = null; Assert.That(LessonLoader.Validate(lesson), Does.Contain("holdMs"));
            lesson.steps[0].holdMs = 700; lesson.steps[0].hands["right"].joints = new float[20][];
            Assert.That(LessonLoader.Validate(lesson), Does.Contain("21 joints"));
        }

        [Test]
        public void ResultPayloadHasOnlyContractFields()
        {
            for (int i = 0; i < 10; i++) flow.Tick(1000);
            var json = Newtonsoft.Json.Linq.JObject.Parse(JsonConvert.SerializeObject(flow.Results));
            Assert.That(json.Count, Is.EqualTo(6));
            Assert.That((string)json["device"], Is.EqualTo("Quest 3S"));
            Assert.That(((Newtonsoft.Json.Linq.JObject)json["steps"][0]).Count, Is.EqualTo(4));
        }

        [Test]
        public void BendSignAndThumbUnsignedFollowHandoff()
        {
            Assert.That(HandConventions.Bend(Vector3.zero, Vector3.forward, Vector3.forward + Vector3.down, true), Is.EqualTo(90).Within(0.001));
            Assert.That(HandConventions.Bend(Vector3.zero, Vector3.forward, Vector3.forward + Vector3.up, true), Is.EqualTo(-90).Within(0.001));
            Assert.That(HandConventions.Bend(Vector3.zero, Vector3.forward, Vector3.forward + Vector3.up, false), Is.EqualTo(90).Within(0.001));
        }

        [TestCase(20, FingerStatus.Matched)]
        [TestCase(35, FingerStatus.Close)]
        [TestCase(35.1f, FingerStatus.Incorrect)]
        public void FingerClassificationHonorsInclusiveBoundaries(float error, FingerStatus expected)
        {
            var hand = lesson.steps[0].hands["right"];
            var joints = hand.JointVectors();
            hand.targetAngles = HandConventions.ComputeAngles(HandConventions.ToLocal(joints, HandConventions.HandFrame(joints, true)));
            hand.targetAngles.index[0] += error;
            Assert.That(new FingerMatcher().Match(hand, joints, true).Fingers["index"], Is.EqualTo(expected));
        }

        [Test]
        public void WristCheckWithoutHeadCannotPass()
        {
            var hand = lesson.steps[1].hands["right"];
            var result = new FingerMatcher().Match(hand, hand.JointVectors(), true, null, true);
            Assert.That(result.Tracked, Is.True); Assert.That(result.WristMatched, Is.False); Assert.That(result.AllMatched, Is.False);
        }

        [Test]
        public void RecallAllowsHoldCompletionExactlyAtDeadline()
        {
            var progress = new StepProgress(); var step = lesson.steps[0];
            progress.Tick(step, 19300, true, false, true);
            Assert.That(progress.Tick(step, 700, true, true, true), Is.True);
            Assert.That(progress.Passed, Is.True);
        }

        [Test]
        public void InvalidDeltaTimeCannotAdvanceTimer()
        {
            var progress = new StepProgress(); var step = lesson.steps[0];
            Assert.Throws<ArgumentOutOfRangeException>(() => progress.Tick(step, -1, true, true, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => progress.Tick(step, float.NaN, true, true, false));
            Assert.That(progress.HeldMs, Is.Zero);
        }

        [Test]
        public void ReentrantCompletionCallbackCannotDoubleAdvance()
        {
            int count = 0;
            flow.StepCompleted += _ => { count++; flow.Tick(1000); };
            flow.Tick(700);
            Assert.That(flow.StepIndex, Is.EqualTo(1)); Assert.That(count, Is.EqualTo(1));
            Assert.That(flow.Progress.HeldMs, Is.Zero);
        }

        [Test]
        public void RendererConnectedLateReceivesCurrentStepExactlyOnce()
        {
            flow.ghostHandRenderer = null; flow.RefreshBindings();
            flow.Tick(700);
            int previousCalls = renderer.ShownSteps;
            flow.ghostHandRenderer = renderer; flow.RefreshBindings(); flow.RefreshBindings();
            Assert.That(renderer.ShownSteps, Is.EqualTo(previousCalls + 1)); Assert.That(renderer.Visible, Is.True);
            for (int i = 0; i < 4; i++) flow.Tick(1000);
            flow.ghostHandRenderer = null; flow.RefreshBindings();
            flow.ghostHandRenderer = renderer; flow.RefreshBindings();
            Assert.That(renderer.Visible, Is.False);
        }

        [Test]
        public void NewLessonClearsPreviousMetricsAndResetsHold()
        {
            for (int i = 0; i < 10; i++) flow.Tick(1000);
            Assert.That(flow.Results.steps.Count, Is.EqualTo(5));
            flow.BeginLesson(lesson, source);
            Assert.That(flow.State, Is.EqualTo(LessonState.Practice)); Assert.That(flow.StepIndex, Is.Zero);
            Assert.That(flow.Results.steps, Is.Empty); Assert.That(flow.Progress.HeldMs, Is.Zero);
            Assert.That(flow.Results.completed, Is.False);
        }

        [Test]
        public void PanelRecentersAfterHeadMovesAndTurns()
        {
            var head = new GameObject("Test Head", typeof(Camera)); head.transform.SetParent(root.transform, false);
            source.Head = head.transform;
            var panel = root.AddComponent<LessonPanel>(); panel.RefreshView();
            head.transform.position = new Vector3(3, 2, -1); head.transform.rotation = Quaternion.Euler(20, 120, 0);
            panel.RefreshView();
            var viewport = head.GetComponent<Camera>().WorldToViewportPoint(panel.Panel.position);
            Assert.That(viewport.z, Is.GreaterThan(0)); Assert.That(viewport.x, Is.InRange(0.2f, 0.8f));
            Assert.That(viewport.y, Is.InRange(0.2f, 0.8f));
            Assert.That(Quaternion.Angle(panel.Panel.rotation, head.transform.rotation), Is.LessThan(0.001f));
        }
    }
}

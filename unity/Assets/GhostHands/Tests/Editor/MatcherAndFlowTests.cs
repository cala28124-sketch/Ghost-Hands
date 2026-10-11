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
            public Vector3? Fingertip;  // where the index fingertip is, when a test moves it
            public bool TryGetJoints(bool isRight, out Vector3[] worldJoints21)
            {
                worldJoints21 = null;
                if (!Tracked || (isRight && !RightTracked) || Flow.CurrentStep == null ||
                    !Flow.CurrentStep.hands.TryGetValue(isRight ? "right" : "left", out var hand)) return false;
                worldJoints21 = ReplayJointSource.WorldPose(hand, isRight, Head);
                if (Incorrect) BendIndex(worldJoints21, isRight, 80);
                if (Fingertip.HasValue) worldJoints21[8] = Fingertip.Value;
                return true;
            }
        }

        [SetUp]
        public void SetUp()
        {
            lesson = JsonConvert.DeserializeObject<Lesson>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Lessons/example.json")));
            root = new GameObject("U2 Test") { hideFlags = HideFlags.HideAndDontSave };
            flow = root.AddComponent<LessonFlow>(); flow.autoStart = false; flow.submitResults = false;
            flow.minTransitionSeconds = flow.maxTransitionSeconds = 0;  // these tests time the holds; transitions have their own
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
        public void FiftyDegreeIndexBendOnlyChangesIndex(bool right)
        {
            var hand = lesson.steps[0].hands[right ? "right" : "left"];
            var joints = hand.JointVectors();
            BendIndex(joints, right, 50);  // the knuckle alone: counts 0.75 x 50 = 37.5, past the index's 30
            var match = new FingerMatcher().Match(hand, joints, right);
            foreach (var finger in FingerMatcher.FingerNames)
                Assert.That(match.Fingers[finger], Is.EqualTo(finger == "index" ? FingerStatus.Close : FingerStatus.Matched), finger);
            Assert.That(match.Hint, Is.EqualTo("Straighten your index"));
        }

        [Test]
        public void FingertipJointCountsHalf()
        {
            var hand = lesson.steps[0].hands["right"];
            var joints = hand.JointVectors();
            hand.targetAngles = HandConventions.ComputeAngles(HandConventions.ToLocal(joints, HandConventions.HandFrame(joints, true)));
            hand.targetAngles.index[2] += 40;  // DIP: weighted 20, so 15 on its own and a curl of 8, inside 30
            Assert.That(new FingerMatcher().Match(hand, joints, true).Fingers["index"], Is.EqualTo(FingerStatus.Matched));
            hand.targetAngles.index[0] += 50;  // MCP: 37.5 on its own, past 30 but within Close (52.5)
            Assert.That(new FingerMatcher().Match(hand, joints, true).Fingers["index"], Is.EqualTo(FingerStatus.Close));
        }

        private LessonHand HandTargetingItself(out Vector3[] joints)
        {
            var hand = lesson.steps[0].hands["right"];
            joints = hand.JointVectors();
            hand.targetAngles = HandConventions.ComputeAngles(HandConventions.ToLocal(joints, HandConventions.HandFrame(joints, true)));
            return hand;
        }

        [Test]
        public void TheRightCurlSpreadDifferentlyStillMatches()
        {
            var hand = HandTargetingItself(out var joints);
            hand.targetAngles.index[0] += 35;  // more at the knuckle,
            hand.targetAngles.index[1] -= 35;  // less in the middle: same overall curl, each joint 26.25 on its own
            var match = new FingerMatcher().Match(hand, joints, true);
            Assert.That(match.Fingers["index"], Is.EqualTo(FingerStatus.Matched));
        }

        [Test]
        public void TheWholeFingerTooBentIsNotMatched()
        {
            var hand = HandTargetingItself(out var joints);
            for (int i = 0; i < 3; i++) hand.targetAngles.index[i] += 35;  // every joint 35 more: overall curl 35, past 30
            var match = new FingerMatcher().Match(hand, joints, true);
            Assert.That(match.Fingers["index"], Is.EqualTo(FingerStatus.Close));
            Assert.That(match.Hint, Is.EqualTo("Bend your index more"));
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
            progress.Tick(step, 150, true, false, false);
            Assert.That(progress.HeldMs, Is.EqualTo(600), "a break inside the grace period only pauses the hold");
            progress.Tick(step, 150, true, false, false);
            Assert.That(progress.HeldMs, Is.Zero); Assert.That(progress.Retries, Is.EqualTo(1));
            progress.Tick(step, 150, true, false, false);
            Assert.That(progress.Retries, Is.EqualTo(1));
            Assert.That(progress.Tick(step, 100, true, true, false), Is.False);
        }

        [Test]
        public void BriefBreakKeepsTheHold()
        {
            var progress = new StepProgress(); var step = lesson.steps[0];
            progress.Tick(step, 600, true, true, false);
            progress.Tick(step, StepProgress.BreakGraceMs, true, false, false);  // e.g. the pinky flickers out of tolerance
            Assert.That(progress.Tick(step, 100, true, true, false), Is.True);
            Assert.That(progress.Retries, Is.Zero);
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
            for (int i = 0; i < 20; i++) flow.Tick(20);  // 400 ms off: past smoothing and the break grace
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

        // One joint off by this much counts 0.75 of it: 30 is the index's tolerance, 52.5 the edge of Close.
        [TestCase(40, FingerStatus.Matched)]
        [TestCase(70, FingerStatus.Close)]
        [TestCase(70.2f, FingerStatus.Incorrect)]
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

        private void WithTransitions()
        {
            flow.minTransitionSeconds = GhostTransition.MinSeconds; flow.maxTransitionSeconds = GhostTransition.MaxSeconds;
            flow.BeginLesson(lesson, source);
        }

        [Test]
        public void TransitionTakesTheExpertsMoveTimeWithinLimits()
        {
            var previous = new LessonStep { mode = "hold" };
            Assert.That(GhostTransition.Seconds(previous, new LessonStep { mode = "hold", durationMs = 4417 }), Is.EqualTo(1.5f));
            Assert.That(GhostTransition.Seconds(previous, new LessonStep { mode = "hold", durationMs = 200 }), Is.EqualTo(0.6f));
            Assert.That(GhostTransition.Seconds(previous, new LessonStep { mode = "hold", durationMs = 1000 }), Is.EqualTo(1f));
            Assert.That(GhostTransition.Seconds(previous, new LessonStep { mode = "pass", durationMs = 3000 }), Is.EqualTo(3f), "a motion step");
            Assert.That(GhostTransition.Seconds(null, new LessonStep { mode = "hold", durationMs = 1000 }), Is.Zero, "first step");
        }

        [Test]
        public void TransitionEasesInAndOut()
        {
            Assert.That(GhostTransition.Ease(0), Is.Zero); Assert.That(GhostTransition.Ease(1), Is.EqualTo(1));
            Assert.That(GhostTransition.Ease(0.5f), Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(GhostTransition.Ease(0.05f), Is.LessThan(0.05f), "starts slowly");
            Assert.That(GhostTransition.Ease(0.95f), Is.GreaterThan(0.95f), "stops slowly");
        }

        [Test]
        public void TransitionSwingsBonesWithoutShrinkingThem()
        {
            var from = lesson.steps[0].hands["right"].JointVectors();
            var to = lesson.steps[1].hands["right"].JointVectors();
            BendIndex(to, true, 80);  // a big curl: sliding joints in straight lines would shrink this finger
            var halfway = GhostTransition.Pose(from, to, 0.5f, new Vector3[21]);
            for (int i = 1; i < 21; i++)
            {
                int parent = HandConventions.Parent(i);
                float expected = Mathf.Lerp((from[i] - from[parent]).magnitude, (to[i] - to[parent]).magnitude, 0.5f);
                Assert.That((halfway[i] - halfway[parent]).magnitude, Is.EqualTo(expected).Within(1e-5f), "bone to joint " + i);
            }
            var start = GhostTransition.Pose(from, to, 0, new Vector3[21]);
            var end = GhostTransition.Pose(from, to, 1, new Vector3[21]);
            for (int i = 0; i < 21; i++)
            {
                Assert.That(Vector3.Distance(start[i], from[i]), Is.LessThan(1e-5f));
                Assert.That(Vector3.Distance(end[i], to[i]), Is.LessThan(1e-5f));
            }
        }

        [Test]
        public void PracticeOnlyTracksOnceTheGhostLands()
        {
            WithTransitions();
            flow.Tick(700);
            Assert.That(flow.StepIndex, Is.EqualTo(1)); Assert.That(flow.TransitionMs, Is.EqualTo(600), "step 2 moves for 0.6 s");
            source.Tracked = false;  // a hand dropping out while watching doesn't count
            int statusUpdates = renderer.StatusUpdates;
            flow.Tick(300);
            Assert.That(flow.InTransition, Is.True);
            Assert.That(flow.Progress.ElapsedMs, Is.Zero); Assert.That(flow.Progress.TrackingLosses, Is.Zero);
            Assert.That(flow.HandMatches, Is.Empty);
            Assert.That(renderer.StatusUpdates, Is.EqualTo(statusUpdates), "fingers aren't colored while the ghost moves");
            source.Tracked = true;
            flow.Tick(300);
            Assert.That(flow.InTransition, Is.False);
            Assert.That(flow.Progress.HeldMs, Is.Zero, "the hold starts after the transition");
            flow.Tick(699); Assert.That(flow.StepIndex, Is.EqualTo(1));
            flow.Tick(1); Assert.That(flow.StepIndex, Is.EqualTo(2));
        }

        [Test]
        public void RecallDoesNotWaitForTransitions()
        {
            WithTransitions();
            for (int i = 0; i < 100 && flow.State == LessonState.Practice; i++) flow.Tick(100);
            Assert.That(flow.State, Is.EqualTo(LessonState.Recall));
            flow.Tick(700);
            Assert.That(flow.StepIndex, Is.EqualTo(1)); Assert.That(flow.InTransition, Is.False);
        }

        [Test]
        public void RatingCombinesAccuracyTriesAndTime()
        {
            var p = new LessonPerformance();
            p.AddPose(true, 0.9f, 0, 2000);
            p.AddPose(true, 0.8f, 1, 2000);  // held at the second try
            p.AddMotion(500);
            p.AddTime(5000);                 // par is 4500
            Assert.That(p.Accuracy, Is.EqualTo(0.85f).Within(1e-5f));
            Assert.That(p.TimesTried, Is.EqualTo(3)); Assert.That(p.TriesScore, Is.EqualTo(2 / 3f).Within(1e-5f));
            Assert.That(p.TimeScore, Is.EqualTo(0.9f).Within(1e-5f));
            Assert.That(p.Rating, Is.EqualTo(8.2f).Within(1e-5f), "10 x (0.5 x 0.85 + 0.25 x 0.667 + 0.25 x 0.9) = 8.17");
        }

        [Test]
        public void PerfectLessonScoresTenAndAFailedPoseScoresNoAccuracy()
        {
            var perfect = new LessonPerformance();
            perfect.AddPose(true, 1, 0, 2200); perfect.AddTime(1800);
            Assert.That(perfect.Rating, Is.EqualTo(10f));
            var failed = new LessonPerformance();
            failed.AddPose(false, 0.9f, 0, 2200); failed.AddTime(2200);
            Assert.That(failed.Accuracy, Is.Zero); Assert.That(failed.Rating, Is.EqualTo(5f));
        }

        [Test]
        public void ClosenessAndTimeFormat()
        {
            Assert.That(LessonPerformance.Closeness(0), Is.EqualTo(1f), "exactly on the pose");
            Assert.That(LessonPerformance.Closeness(1), Is.EqualTo(0.5f), "at the edge of tolerance");
            Assert.That(LessonPerformance.FormatTime(42300), Is.EqualTo("42.3 s"));
            Assert.That(LessonPerformance.FormatTime(102000), Is.EqualTo("1:42"));
        }

        [Test]
        public void FinishedLessonShowsItsPerformanceOnThePanel()
        {
            var panel = root.AddComponent<LessonPanel>();
            for (int i = 0; i < 10; i++) flow.Tick(1000);  // 5 steps, twice: 4 holds and a motion each round
            Assert.That(flow.State, Is.EqualTo(LessonState.Results));
            var p = flow.Performance;
            Assert.That(p.Poses, Is.EqualTo(8)); Assert.That(p.Retries, Is.Zero); Assert.That(p.TotalMs, Is.EqualTo(10000));
            Assert.That(p.Accuracy, Is.InRange(0.8f, 1f), "the example's poses are within a few degrees of their targets");
            Assert.That(p.TimeScore, Is.EqualTo(1f), "well under par");
            panel.RefreshView();
            Assert.That(panel.PerformanceView.gameObject.activeSelf, Is.True);
            Assert.That(panel.RatingText, Does.Contain(p.Rating.ToString("0.0")));
            Assert.That(panel.StatText(0), Is.EqualTo(Mathf.RoundToInt(100 * p.Accuracy) + "%"));
            Assert.That(panel.StatText(1), Is.EqualTo("8"));
            Assert.That(panel.StatText(2), Is.EqualTo("10.0 s"));
        }

        [Test]
        public void LessonPanelPromptsTheStepAndShowsEachFinger()
        {
            var panel = root.AddComponent<LessonPanel>();
            flow.Tick(100);
            panel.RefreshView();
            Assert.That(panel.StepText, Is.EqualTo("STEP 1 / 5"));
            Assert.That(panel.InstructionText, Is.EqualTo(lesson.steps[0].instruction));
            Assert.That(panel.StatusText, Is.EqualTo("Hold steady"));
            Assert.That(panel.HoldText, Is.EqualTo("0.1 / 0.7 S"));
            for (int i = 0; i < 5; i++) Assert.That(panel.FingerColor(i), Is.EqualTo(MenuStyle.Good), FingerMatcher.FingerNames[i]);
            source.Incorrect = true;  // index bent 80 degrees off
            for (int i = 0; i < 20; i++) flow.Tick(20);
            panel.RefreshView();
            Assert.That(panel.FingerColor(1), Is.EqualTo(MenuStyle.Bad), "index");
            Assert.That(panel.FingerColor(2), Is.EqualTo(MenuStyle.Good), "middle");
        }

        [Test]
        public void RetryButtonIsWithinReach()
        {
            var panel = root.AddComponent<LessonPanel>(); panel.RefreshView();
            Assert.That(Vector3.Distance(panel.RetryButton.position, source.Head.position), Is.LessThan(0.6f));
        }

        [Test]
        public void PokingRetryWithAFingertipPressesIt()
        {
            var panel = root.AddComponent<LessonPanel>(); panel.RefreshView();
            var button = panel.RetryButton.GetComponent<UnityEngine.UI.Button>();
            int presses = 0;
            button.onClick.RemoveAllListeners(); button.onClick.AddListener(() => presses++);
            PokeButton.ResetCooldown();
            foreach (float millimetres in new[] { -100f, -50f, -20f, 5f, 10f })  // in front of the button, then through it
            {
                source.Fingertip = panel.RetryButton.TransformPoint(new Vector3(0, 0, millimetres));
                panel.RefreshView();
                panel.RetryButton.GetComponent<PokeButton>().Poke();  // LateUpdate, outside play mode
            }
            Assert.That(presses, Is.EqualTo(1));
        }

        private static readonly Rect Button = new Rect(-55, -25, 110, 50);

        private static int Presses(PokeDetector poke, params Vector3[] path)
        {
            int presses = 0;
            foreach (var tip in path) if (poke.Update(tip, Button)) presses++;
            return presses;
        }

        [Test]
        public void PushingThroughFromTheFrontPressesOnceUntilTheFingerPullsBack()
        {
            var poke = new PokeDetector();
            Assert.That(Presses(poke, new Vector3(0, 0, -60), new Vector3(0, 0, -20), new Vector3(0, 0, 2), new Vector3(0, 0, 20)), Is.EqualTo(1));
            Assert.That(Presses(poke, new Vector3(0, 0, -2), new Vector3(0, 0, 3)), Is.Zero, "a small wobble at the surface");
            Assert.That(Presses(poke, new Vector3(0, 0, -30), new Vector3(0, 0, 1)), Is.EqualTo(1), "pulled back, pressed again");
        }

        [Test]
        public void SlidingInFromTheSideOrPassingBehindDoesNotPress()
        {
            var poke = new PokeDetector();
            Assert.That(Presses(poke, new Vector3(-200, 0, 10), new Vector3(-60, 0, 10), new Vector3(0, 0, 10)), Is.Zero);
            Assert.That(Presses(poke, new Vector3(0, 0, 200), new Vector3(0, 0, 30)), Is.Zero);
            Assert.That(Presses(poke, new Vector3(0, -200, -30), new Vector3(0, 0, 5)), Is.Zero, "the finger came from outside");
        }

        [Test]
        public void HoveringMeansOverTheButtonAndCloseInFront()
        {
            var poke = new PokeDetector();
            poke.Update(new Vector3(60, 0, -30), Button); Assert.That(poke.Hovering, Is.True, "within the 15 mm margin");
            poke.Update(new Vector3(0, 0, -150), Button); Assert.That(poke.Hovering, Is.False, "too far in front");
            poke.Update(new Vector3(100, 0, -30), Button); Assert.That(poke.Hovering, Is.False, "off to the side");
        }
    }
}

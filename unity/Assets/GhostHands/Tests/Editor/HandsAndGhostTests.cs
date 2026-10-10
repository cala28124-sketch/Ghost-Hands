using System.IO;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace GhostHands
{
    public sealed class HandsAndGhostTests
    {
        private Lesson lesson;
        private GameObject root;
        private LessonFlow flow;
        private ReplayJointSource replay;
        private GhostHandRenderer ghost;
        private Transform head;

        [SetUp]
        public void SetUp()
        {
            lesson = JsonConvert.DeserializeObject<Lesson>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Lessons/example.json")));
            root = new GameObject("Combined Hands Test") { hideFlags = HideFlags.HideAndDontSave };
            head = new GameObject("Test Head").transform; head.SetParent(root.transform, false);
            head.position = new Vector3(1, 2, 3); head.rotation = Quaternion.Euler(20, 60, 10);
            flow = root.AddComponent<LessonFlow>(); flow.autoStart = false; flow.submitResults = false;
            replay = root.AddComponent<ReplayJointSource>(); replay.flow = flow;
            ghost = root.AddComponent<GhostHandRenderer>(); ghost.head = head; ghost.handJointSource = replay;
            flow.handJointSource = replay; flow.ghostHandRenderer = ghost;
            flow.BeginLesson(lesson, replay);
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(root);

        [Test]
        public void EveryExampleFingerIsWithinEightDegreesOfArtifactTargets()
        {
            foreach (var step in lesson.steps)
                foreach (var pair in step.hands)
                {
                    var angles = HandConventions.ComputeAngles(pair.Value.JointVectors());
                    foreach (var finger in new[] { "index", "middle", "ring", "pinky" })
                        for (int joint = 0; joint < 3; joint++)
                            Assert.That(Mathf.Abs(angles.ForFinger(finger)[joint] - pair.Value.targetAngles.ForFinger(finger)[joint]),
                                Is.LessThanOrEqualTo(8), step.id + " " + pair.Key + " " + finger + " " + joint);
                }
        }

        [Test]
        public void LessonHandFramesAreCloseToIdentity()
        {
            foreach (var step in lesson.steps)
                foreach (var pair in step.hands)
                    Assert.That(Quaternion.Angle(HandConventions.HandFrame(pair.Value.JointVectors(), pair.Key == "right"), Quaternion.identity),
                        Is.LessThan(5), step.id + " " + pair.Key);
        }

        [Test]
        public void MirroredStepOneHasEqualAngles()
        {
            var left = HandConventions.ComputeAngles(lesson.steps[0].hands["left"].JointVectors());
            var right = HandConventions.ComputeAngles(lesson.steps[0].hands["right"].JointVectors());
            foreach (var finger in FingerMatcher.FingerNames)
                for (int joint = 0; joint < left.ForFinger(finger).Length; joint++)
                    Assert.That(left.ForFinger(finger)[joint], Is.EqualTo(right.ForFinger(finger)[joint]).Within(0.0001f));
        }

        [Test]
        public void XRSourceConvertsOriginThroughTrackingSpaceExactlyOnce()
        {
            var tracking = new GameObject("TrackingSpace").transform; tracking.SetParent(root.transform, false);
            tracking.position = new Vector3(2, 3, -4); tracking.rotation = Quaternion.Euler(15, 80, 20);
            tracking.localScale = Vector3.one * 1.2f;
            var source = root.AddComponent<XRHandsJointSource>(); source.trackingSpace = tracking; source.head = head;
            source.ResolveRig();
            var point = new Vector3(0.03f, 0.6f, 0.2f);
            Assert.That(Vector3.Distance(source.OriginToWorld(point), tracking.TransformPoint(point)), Is.LessThan(1e-6f));
            Assert.That(source.Head, Is.SameAs(head));
            Assert.That(InteractionHandJointSource.LessonJointOrder.Length, Is.EqualTo(21));
        }

        [Test]
        public void RendererCreatesTwoHandsWithTwentyOneJointsAndTwentyBonesEach()
        {
            Assert.That(ghost.HandVisible(false), Is.True); Assert.That(ghost.HandVisible(true), Is.True);
            Assert.That(ghost.GetComponentsInChildren<MeshRenderer>(true).Length, Is.EqualTo(82));
            Assert.That(ghost.GetComponentsInChildren<Collider>(true), Is.Empty);
            foreach (var renderer in ghost.GetComponentsInChildren<MeshRenderer>(true))
            {
                Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
                Assert.That(renderer.receiveShadows, Is.False);
                Assert.That(renderer.sharedMaterial.shader.name, Is.EqualTo("Universal Render Pipeline/Unlit"));
                Assert.That(renderer.sharedMaterial.GetFloat("_ZWrite"), Is.Zero);
                Assert.That(renderer.sharedMaterial.renderQueue, Is.EqualTo(3000));
            }
        }

        [Test]
        public void GhostUsesLiveWristHeadYawAndLessonRotation()
        {
            var target = lesson.steps[1].hands["right"];
            ghost.Show(target, true); ghost.RefreshPose(0);
            replay.TryGetJoints(true, out var live);
            var yaw = Quaternion.Euler(0, head.eulerAngles.y, 0);
            float scale = Mathf.Clamp(Vector3.Distance(live[9], live[0]) / target.JointVectors()[9].magnitude, 0.7f, 1.4f);
            var expected = live[0] + yaw * (target.Rotation() * target.JointVectors()[8] * scale);
            Assert.That(Vector3.Distance(ghost.JointPosition(true, 8), expected), Is.LessThan(1e-5f));
        }

        [Test]
        public void GhostScaleClampsToSupportedHandSizes()
        {
            var target = JsonConvert.DeserializeObject<LessonHand>(JsonConvert.SerializeObject(lesson.steps[0].hands["right"]));
            var liveHand = lesson.steps[0].hands["right"];
            foreach (var p in liveHand.joints) for (int axis = 0; axis < 3; axis++) p[axis] *= 2;
            ghost.Show(target, true); ghost.RefreshPose(0);
            Assert.That(Vector3.Distance(ghost.JointPosition(true, 8), ghost.JointPosition(true, 0)),
                Is.EqualTo(target.JointVectors()[8].magnitude * 1.4f).Within(1e-5f));
            foreach (var p in liveHand.joints) for (int axis = 0; axis < 3; axis++) p[axis] *= 0.25f;
            ghost.RefreshPose(0);
            Assert.That(Vector3.Distance(ghost.JointPosition(true, 8), ghost.JointPosition(true, 0)),
                Is.EqualTo(target.JointVectors()[8].magnitude * 0.7f).Within(1e-5f));
        }

        [Test]
        public void TrackingLossUsesFallbackAnchorAndGrayFeedback()
        {
            replay.tracked = false;
            ghost.RefreshPose(10);
            var expected = head.position + Quaternion.Euler(0, head.eulerAngles.y, 0) * new Vector3(0, -0.15f, 0.35f);
            Assert.That(Vector3.Distance(ghost.JointPosition(true, 0), expected), Is.LessThan(1e-5f));
            var bone = ghost.transform.Find("Right Ghost Hand/Bone 4").GetComponent<Renderer>();
            var properties = new MaterialPropertyBlock(); bone.GetPropertyBlock(properties);
            Assert.That(properties.GetColor("_BaseColor"), Is.EqualTo(GhostHandRenderer.StatusColor(FingerStatus.Unavailable)));
        }

        [Test]
        public void AnchorSmoothingApproachesMovedHeadWithoutJumping()
        {
            replay.tracked = false; ghost.RefreshPose(10);
            Vector3 old = ghost.JointPosition(true, 0);
            head.position += Vector3.right;
            ghost.RefreshPose(0.01f);
            Assert.That(Vector3.Distance(ghost.JointPosition(true, 0), old), Is.InRange(0.08f, 0.11f));
        }

        [Test]
        public void FingerStatusOnlyChangesThatFingerAndPreservesTransparency()
        {
            ghost.SetFingerStatus("index", FingerStatus.Incorrect); ghost.RefreshPose(0);
            Assert.That(ghost.FingerColor("index"), Is.EqualTo(GhostHandRenderer.StatusColor(FingerStatus.Incorrect)));
            Assert.That(ghost.FingerColor("middle").r, Is.EqualTo(0.55f));
            Assert.That(ghost.FingerColor("index").a, Is.EqualTo(0.35f));
            var properties = new MaterialPropertyBlock();
            ghost.transform.Find("Right Ghost Hand/Bone 4").GetComponent<Renderer>().GetPropertyBlock(properties);
            Assert.That(Vector4.Distance(properties.GetColor("_BaseColor"), ghost.FingerColor("index")), Is.LessThan(1e-5f));
        }

        [Test]
        public void PassTransitionInterpolatesAndHidesMissingHand()
        {
            ghost.ShowStep(lesson.steps[1], lesson.steps[2]); ghost.SetVisible(true);
            float half = lesson.steps[2].durationMs / 2000f;
            ghost.RefreshPose(half);
            Assert.That(ghost.HandVisible(false), Is.False); Assert.That(ghost.HandVisible(true), Is.True);
            replay.TryGetJoints(true, out var live);
            var a = lesson.steps[1].hands["right"]; var b = lesson.steps[2].hands["right"];
            var point = Vector3.Lerp(a.JointVectors()[8], b.JointVectors()[8], 0.5f);
            var rotation = Quaternion.Slerp(a.Rotation(), b.Rotation(), 0.5f);
            var expected = live[0] + Quaternion.Euler(0, head.eulerAngles.y, 0) * (rotation * point);
            Assert.That(Vector3.Distance(ghost.JointPosition(true, 8), expected), Is.LessThan(1e-5f));
        }

        [Test]
        public void CombinedFlowShowsPracticeGhostsThenHidesRecallAndResults()
        {
            for (int i = 0; i < 5; i++)
            {
                var step = flow.CurrentStep;
                flow.Tick(step.mode == "pass" ? step.durationMs : step.holdMs.Value);
                ghost.RefreshPose(1);
            }
            Assert.That(flow.State, Is.EqualTo(LessonState.Recall));
            Assert.That(ghost.HandVisible(false), Is.False); Assert.That(ghost.HandVisible(true), Is.False);
            for (int i = 0; i < 5; i++) flow.Tick(1000);
            Assert.That(flow.State, Is.EqualTo(LessonState.Results));
            Assert.That(flow.Results.steps.Count, Is.EqualTo(5)); Assert.That(flow.Results.recallPassed, Is.True);
            Assert.That(ghost.IsVisible, Is.False);
        }

        [Test]
        public void CombinedTrackingLossResetsHoldAndGraysBothGhosts()
        {
            flow.Tick(300); replay.tracked = false; flow.Tick(10); ghost.RefreshPose(0);
            Assert.That(flow.Progress.HeldMs, Is.Zero); Assert.That(flow.Progress.TrackingLosses, Is.EqualTo(1));
            Assert.That(flow.HandMatches["left"].Fingers["index"], Is.EqualTo(FingerStatus.Unavailable));
            Assert.That(flow.HandMatches["right"].Fingers["index"], Is.EqualTo(FingerStatus.Unavailable));
            var properties = new MaterialPropertyBlock();
            foreach (var side in new[] { "Left", "Right" })
            {
                ghost.transform.Find(side + " Ghost Hand/Bone 4").GetComponent<Renderer>().GetPropertyBlock(properties);
                Assert.That(Vector4.Distance(properties.GetColor("_BaseColor"), GhostHandRenderer.StatusColor(FingerStatus.Unavailable)), Is.LessThan(1e-5f));
            }
            replay.tracked = true; flow.Tick(699); Assert.That(flow.StepIndex, Is.Zero);
            flow.Tick(1); Assert.That(flow.StepIndex, Is.EqualTo(1));
        }

        [Test]
        public void StandaloneDemoStepsForwardAndBackWithoutAdvancingLessonFlow()
        {
            var demo = root.AddComponent<GhostDemo>(); demo.autoLoad = false; demo.handJointSource = replay;
            demo.BeginLesson(lesson); demo.NextStep();
            Assert.That(demo.StepIndex, Is.EqualTo(1)); Assert.That(flow.StepIndex, Is.Zero);
            demo.NextStep(); Assert.That(ghost.HandVisible(false), Is.False);
            demo.PreviousStep(); Assert.That(ghost.HandVisible(false), Is.True);
            demo.PreviousStep(); demo.PreviousStep(); Assert.That(demo.StepIndex, Is.EqualTo(4));
        }

        [Test]
        public void DebugJointsAppearOnlyForTrackedHandsAndCanBeDisabled()
        {
            var debug = root.AddComponent<HandTrackingDebug>(); debug.handJointSource = replay; debug.drawJoints = true;
            debug.Refresh(); Assert.That(root.transform.Find("Live Hand Debug Joints").GetComponentsInChildren<MeshRenderer>().Length, Is.EqualTo(42));
            replay.tracked = false; debug.Refresh();
            Assert.That(root.transform.Find("Live Hand Debug Joints").GetComponentsInChildren<MeshRenderer>().Length, Is.Zero);
            debug.drawJoints = false; debug.Refresh();
            Assert.That(root.transform.Find("Live Hand Debug Joints").gameObject.activeSelf, Is.False);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace GhostHands
{
    // Real hands end to end: a lesson made by cv/ from a recording, and that recording's tracked hands
    // (cv/export_replay.py writes both into Fixtures/). The replay stands in for the headset's hand tracking,
    // so the expert's own recording must pass the lesson made from it, through the real loader, matcher and flow.
    public sealed class EndToEndReplayTests
    {
        private const float SameAngleDegrees = 1;   // C# vs Python on the lesson's own joints: rounding only
        private const float SameWristDegrees = 5;   // one frame of a held pose vs the pose built from the pause

        private static string FixturesDir => Path.Combine(Application.dataPath, "GhostHands", "Tests", "Editor", "Fixtures");

        private sealed class ReplayStep
        {
            [JsonProperty("id")] public string id;
            [JsonProperty("frame")] public int frame;
            [JsonProperty("first")] public int first;
            [JsonProperty("last")] public int last;
        }

        private sealed class ReplayFrame
        {
            [JsonProperty("t_ms")] public int tMs;
            [JsonProperty("left")] public float[][] left;
            [JsonProperty("right")] public float[][] right;
            public Vector3[] Hand(bool isRight)
            {
                var points = isRight ? right : left;
                return points?.Select(p => new Vector3(p[0], p[1], p[2])).ToArray();
            }
        }

        private sealed class Replay
        {
            [JsonProperty("lessonId")] public string lessonId;
            [JsonProperty("fps")] public float fps;
            [JsonProperty("frames")] public List<ReplayFrame> frames;
            [JsonProperty("steps")] public List<ReplayStep> steps;
        }

        // Plays the recording as live hand tracking; the test moves the cursor.
        private sealed class ReplaySource : IHandJointSource
        {
            private readonly Replay replay;
            public int Cursor;
            public ReplaySource(Replay replay) => this.replay = replay;
            public Transform Head => null;
            public bool TryGetJoints(bool isRight, out Vector3[] worldJoints21)
            {
                worldJoints21 = replay.frames[Cursor].Hand(isRight);
                return worldJoints21 != null;
            }
        }

        public static IEnumerable<string> Fixtures()
        {
            var names = Directory.Exists(FixturesDir)
                ? Directory.GetFiles(FixturesDir, "*.replay.json").Select(p => Path.GetFileName(p).Replace(".replay.json", "")).ToList()
                : new List<string>();
            return names.Count > 0 ? names : new List<string> { "(none)" };
        }

        private static (Lesson, Replay) Load(string name)
        {
            if (name == "(none)") Assert.Ignore("No fixtures yet: run cv/export_replay.py on a recording.");
            var json = File.ReadAllText(Path.Combine(FixturesDir, name + ".lesson.json"));
            Assert.That(LessonLoader.TryParse(json, out var lesson, out var reason), Is.True, reason);
            var replay = JsonConvert.DeserializeObject<Replay>(File.ReadAllText(Path.Combine(FixturesDir, name + ".replay.json")));
            Assert.That(replay.steps.Select(s => s.id), Is.EqualTo(lesson.steps.Select(s => s.id)), "replay and lesson steps differ");
            return (lesson, replay);
        }

        [TestCaseSource(nameof(Fixtures))]
        public void UnityComputesTheSameAnglesAsCv(string name)
        {
            var (lesson, _) = Load(name);
            foreach (var step in lesson.steps)
                foreach (var pair in step.hands)
                {
                    // The lesson's joints are already in the hand frame, so both languages measure the same points.
                    var angles = HandConventions.ComputeAngles(pair.Value.JointVectors());
                    foreach (var finger in FingerMatcher.FingerNames)
                    {
                        var unity = angles.ForFinger(finger);
                        var cv = pair.Value.targetAngles.ForFinger(finger);
                        for (int i = 0; i < cv.Length; i++)
                            Assert.That(unity[i], Is.EqualTo(cv[i]).Within(SameAngleDegrees),
                                $"{step.id} {pair.Key} {finger}[{i}]");
                    }
                }
        }

        [TestCaseSource(nameof(Fixtures))]
        public void UnityHandFrameMatchesTheLessonWristRotation(string name)
        {
            var (lesson, replay) = Load(name);
            for (int s = 0; s < lesson.steps.Count; s++)
                foreach (var pair in lesson.steps[s].hands)
                {
                    bool isRight = pair.Key == "right";
                    var joints = replay.frames[replay.steps[s].frame].Hand(isRight);
                    if (!HandConventions.ValidJoints(joints)) continue;  // the pose came from the frames around it
                    float angle = Quaternion.Angle(HandConventions.HandFrame(joints, isRight), pair.Value.Rotation());
                    TestContext.WriteLine($"{lesson.steps[s].id} {pair.Key}: wrist {angle:0.0} deg from the lesson");
                    Assert.That(angle, Is.LessThan(SameWristDegrees), $"{lesson.steps[s].id} {pair.Key} wrist");
                }
        }

        [TestCaseSource(nameof(Fixtures))]
        public void TheExpertsRecordingPassesItsOwnLesson(string name)
        {
            var (lesson, replay) = Load(name);
            var root = new GameObject("E2E Test") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var flow = root.AddComponent<LessonFlow>();
                flow.autoStart = false;
                flow.submitResults = false;
                var source = new ReplaySource(replay);
                flow.BeginLesson(lesson, source);

                float frameMs = 1000f / replay.fps;
                var state = flow.State;
                int ticks = 0, stepTicks = 0, lastStep = -1, maxTicks = 4 * replay.frames.Count + lesson.steps.Count * (int)(25000 / frameMs);
                while (flow.State != LessonState.Results && ticks++ < maxTicks)
                {
                    if (flow.State != state) { state = flow.State; source.Cursor = 0; }  // recall replays from the start
                    var step = replay.steps[flow.StepIndex];
                    // Move through the recording; at a step's pause, hold there (loop its frames) like a learner would.
                    if (source.Cursor < step.first) source.Cursor++;
                    else source.Cursor = source.Cursor >= step.last ? step.first : source.Cursor + 1;
                    flow.Tick(frameMs);
                    // Every 5 seconds stuck on one step, log how far off each finger is: that's what to tune.
                    if (flow.StepIndex != lastStep) { lastStep = flow.StepIndex; stepTicks = 0; }
                    else if (++stepTicks % (int)(5000 / frameMs) == 0)
                        foreach (var pair in flow.HandMatches)
                            TestContext.WriteLine($"{flow.State} {flow.CurrentStep?.id} {pair.Key}: " +
                                string.Join(", ", pair.Value.Errors.Select(e => $"{e.Key} {e.Value:0}")));
                }

                Assert.That(flow.State, Is.EqualTo(LessonState.Results), $"stuck at {flow.CurrentStep?.id}: {flow.Message}");
                Assert.That(flow.Results.completed, Is.True);
                Assert.That(flow.Results.recallPassed, Is.True, flow.Message);
                Assert.That(flow.Results.steps.Select(s => s.stepId), Is.EqualTo(lesson.steps.Select(s => s.id)));
                foreach (var result in flow.Results.steps)
                    TestContext.WriteLine($"{result.stepId}: {result.timeToMatchMs} ms, {result.retries} retries, {result.trackingLosses} losses");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}

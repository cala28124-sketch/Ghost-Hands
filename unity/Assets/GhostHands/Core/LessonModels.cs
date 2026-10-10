using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace GhostHands
{
    [Serializable]
    public class Lesson
    {
        [JsonProperty("schemaVersion")] public int schemaVersion;
        [JsonProperty("lessonId")] public string lessonId;
        [JsonProperty("title")] public string title;
        [JsonProperty("hands")] public string[] hands;
        [JsonProperty("coordinateConvention")] public string coordinateConvention;
        [JsonProperty("angleUnit")] public string angleUnit;
        [JsonProperty("positionUnit")] public string positionUnit;
        [JsonProperty("steps")] public List<LessonStep> steps;
    }

    [Serializable]
    public class LessonStep
    {
        [JsonProperty("id")] public string id;
        [JsonProperty("label")] public string label;
        [JsonProperty("instruction")] public string instruction;
        [JsonProperty("mode")] public string mode;
        [JsonProperty("holdMs")] public int? holdMs;
        [JsonProperty("durationMs")] public int durationMs;
        [JsonProperty("hands")] public Dictionary<string, LessonHand> hands;
    }

    [Serializable]
    public class LessonHand
    {
        [JsonProperty("targetAngles")] public TargetAngles targetAngles;
        [JsonProperty("joints")] public float[][] joints;
        [JsonProperty("wristRotation")] public float[] wristRotation;

        public Vector3[] JointVectors()
        {
            var result = new Vector3[joints.Length];
            for (int i = 0; i < result.Length; i++)
                result[i] = new Vector3(joints[i][0], joints[i][1], joints[i][2]);
            return result;
        }

        public Quaternion Rotation() => new Quaternion(wristRotation[0], wristRotation[1], wristRotation[2], wristRotation[3]).normalized;
    }

    [Serializable]
    public class TargetAngles
    {
        [JsonProperty("thumb")] public float[] thumb;
        [JsonProperty("index")] public float[] index;
        [JsonProperty("middle")] public float[] middle;
        [JsonProperty("ring")] public float[] ring;
        [JsonProperty("pinky")] public float[] pinky;

        public float[] ForFinger(string name)
        {
            switch (name)
            {
                case "thumb": return thumb;
                case "index": return index;
                case "middle": return middle;
                case "ring": return ring;
                case "pinky": return pinky;
                default: throw new ArgumentException("Unknown finger: " + name);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace GhostHands
{
    [Serializable]
    public sealed class StepResult
    {
        [JsonProperty("stepId")] public string stepId;
        [JsonProperty("timeToMatchMs")] public int timeToMatchMs;
        [JsonProperty("retries")] public int retries;
        [JsonProperty("trackingLosses")] public int trackingLosses;
    }

    [Serializable]
    public sealed class SessionResults
    {
        [JsonProperty("lessonId")] public string lessonId;
        [JsonProperty("tester")] public string tester;
        [JsonProperty("device")] public string device = "Quest 3S";
        [JsonProperty("completed")] public bool completed;
        [JsonProperty("recallPassed")] public bool recallPassed;
        [JsonProperty("steps")] public List<StepResult> steps = new List<StepResult>();
    }
}

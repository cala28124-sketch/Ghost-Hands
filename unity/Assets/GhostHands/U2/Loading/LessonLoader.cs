using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace GhostHands
{
    [Serializable]
    public class LessonSummary
    {
        [JsonProperty("lessonId")] public string lessonId;
        [JsonProperty("title")] public string title;
        [JsonProperty("hands")] public string[] hands;
        [JsonProperty("stepCount")] public int stepCount;
    }

    public sealed class LessonLoader : MonoBehaviour
    {
        public const string ServerPreference = "GhostHands.ServerUrl";
        public string serverUrl = "http://10.32.217.238:3000";
        public string ServerUrl => PlayerPrefs.GetString(ServerPreference, serverUrl).TrimEnd('/');
        public bool UsedFallback { get; private set; }
        public string LastMessage { get; private set; }

        public void SetServerUrl(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
                throw new ArgumentException("Use a full http:// or https:// server URL.");
            PlayerPrefs.SetString(ServerPreference, value.TrimEnd('/'));
            PlayerPrefs.Save();
        }

        public IEnumerator LoadLesson(string id, Action<Lesson> loaded, Action<string> failed = null)
        {
            UsedFallback = false;
            Lesson lesson = null;
            string reason = null;
            yield return Read(ServerUrl + "/lessons/" + Uri.EscapeDataString(id ?? ""), text =>
            {
                if (!TryParse(text, out lesson, out reason)) lesson = null;
            }, error => reason = error);
            if (lesson != null)
            {
                LastMessage = "Loaded lesson from " + ServerUrl;
                loaded?.Invoke(lesson);
                yield break;
            }
            UsedFallback = true;
            LastMessage = "Server lesson unavailable: " + reason + ". Using bundled example.";
            Debug.LogWarning(LastMessage);
            yield return Read(BundledUrl, text =>
            {
                if (!TryParse(text, out lesson, out reason)) lesson = null;
            }, error => reason = error);
            if (lesson != null) loaded?.Invoke(lesson);
            else
            {
                LastMessage = "Bundled lesson failed: " + reason;
                Debug.LogError(LastMessage);
                failed?.Invoke(LastMessage);
            }
        }

        public IEnumerator ListLessons(Action<List<LessonSummary>> loaded, Action<string> failed = null)
        {
            List<LessonSummary> list = null;
            string reason = null;
            yield return Read(ServerUrl + "/lessons", text =>
            {
                try
                {
                    list = JsonConvert.DeserializeObject<List<LessonSummary>>(text);
                    if (list == null || list.Exists(x => x == null || string.IsNullOrEmpty(x.lessonId)))
                    { list = null; reason = "Invalid lesson list."; }
                }
                catch (JsonException ex) { reason = "Bad lesson list JSON: " + ex.Message; }
            }, error => reason = error);
            if (list != null) { loaded?.Invoke(list); yield break; }
            Debug.LogWarning("Lesson list unavailable: " + reason + ". Listing bundled example.");
            Lesson fallback = null;
            yield return Read(BundledUrl, text => TryParse(text, out fallback, out reason), error => reason = error);
            if (fallback != null)
                loaded?.Invoke(new List<LessonSummary> { new LessonSummary { lessonId = fallback.lessonId,
                    title = fallback.title, hands = fallback.hands, stepCount = fallback.steps.Count } });
            else failed?.Invoke("Cannot list bundled lesson: " + reason);
        }

        public static string BundledUrl
        {
            get
            {
                var path = Path.Combine(Application.streamingAssetsPath, "Lessons/example.json");
                return path.Contains("://") ? path : new Uri(path).AbsoluteUri;
            }
        }

        private static IEnumerator Read(string url, Action<string> success, Action<string> failed)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https" && uri.Scheme != "file" && uri.Scheme != "jar"))
            {
                failed("Invalid lesson URL: " + url);
                yield break;
            }
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = 3;
                var operation = request.SendWebRequest();
                while (!operation.isDone) yield return null;
                if (request.result == UnityWebRequest.Result.Success) success(request.downloadHandler.text);
                else failed("GET " + url + ": " + request.responseCode + " " + request.error + " " + request.downloadHandler.text);
            }
        }

        public static bool TryParse(string json, out Lesson lesson, out string reason)
        {
            lesson = null;
            try
            {
                var candidate = JsonConvert.DeserializeObject<Lesson>(json);
                reason = Validate(candidate);
                if (reason != null) return false;
                lesson = candidate;
                return true;
            }
            catch (Exception ex) when (ex is JsonException || ex is ArgumentException)
            { reason = "Bad lesson JSON: " + ex.Message; return false; }
        }

        public static string Validate(Lesson lesson)
        {
            if (lesson == null || lesson.schemaVersion != 2) return "Expected schemaVersion 2.";
            if (string.IsNullOrEmpty(lesson.lessonId) || string.IsNullOrEmpty(lesson.title)) return "Missing lesson id or title.";
            if (lesson.coordinateConvention != "wrist-relative" || lesson.angleUnit != "degrees" || lesson.positionUnit != "meters")
                return "Unsupported coordinates or units.";
            if (lesson.hands == null || lesson.hands.Length < 1 || lesson.hands.Length > 2) return "Expected one or two lesson hands.";
            var allowed = new HashSet<string>();
            foreach (var side in lesson.hands)
                if ((side != "left" && side != "right") || !allowed.Add(side)) return "Invalid or duplicate lesson hand.";
            if (lesson.steps == null || lesson.steps.Count == 0) return "Lesson has no steps.";
            var ids = new HashSet<string>();
            foreach (var step in lesson.steps)
            {
                if (step == null || string.IsNullOrEmpty(step.id) || !ids.Add(step.id)) return "Missing or duplicate step id.";
                if (string.IsNullOrEmpty(step.instruction)) return step.id + ": missing instruction.";
                if (step.mode != "hold" && step.mode != "pass") return step.id + ": unsupported mode.";
                if (step.durationMs < 0 || step.durationMs > 10000) return step.id + ": invalid durationMs.";
                if (step.mode == "hold" && (!step.holdMs.HasValue || step.holdMs < 100 || step.holdMs > 5000))
                    return step.id + ": hold step requires holdMs between 100 and 5000.";
                if (step.hands == null || step.hands.Count == 0) return step.id + ": missing hands.";
                foreach (var pair in step.hands)
                {
                    var hand = pair.Value;
                    if (!allowed.Contains(pair.Key)) return step.id + ": hand absent from lesson hands.";
                    if (hand == null || hand.joints == null || hand.joints.Length != 21) return step.id + ": expected 21 joints.";
                    foreach (var point in hand.joints)
                        if (!ValidArray(point, 3, -0.5f, 0.5f)) return step.id + ": invalid joint point.";
                    if (!ValidArray(hand.wristRotation, 4, -1, 1)) return step.id + ": invalid wrist quaternion.";
                    float norm = 0; foreach (float v in hand.wristRotation) norm += v * v;
                    if (norm < 0.0001f) return step.id + ": zero wrist quaternion.";
                    if (hand.targetAngles == null) return step.id + ": missing targetAngles.";
                    foreach (var finger in new[] { "thumb", "index", "middle", "ring", "pinky" })
                        if (!ValidArray(hand.targetAngles.ForFinger(finger), finger == "thumb" ? 2 : 3, -90, 180))
                            return step.id + ": invalid " + finger + " targetAngles.";
                }
            }
            return null;
        }

        private static bool ValidArray(float[] values, int count, float min, float max)
        {
            if (values == null || values.Length != count) return false;
            foreach (float v in values) if (float.IsNaN(v) || float.IsInfinity(v) || v < min || v > max) return false;
            return true;
        }
    }
}

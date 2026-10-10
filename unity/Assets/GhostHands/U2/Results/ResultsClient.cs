using System;
using System.Collections;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Scripting;

namespace GhostHands
{
    public static class ResultsClient
    {
        [Preserve]
        private sealed class Response
        {
            [JsonProperty("ok")] public bool ok;
            [JsonProperty("sessionId")] public int sessionId;
        }

        public static IEnumerator Post(string serverUrl, SessionResults results, Action<int> saved, Action<string> failed)
        {
            string json = JsonConvert.SerializeObject(results);
            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var endpoint) ||
                (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
            {
                ReportFailure("POST /results: invalid HTTP server URL.", json, failed);
                yield break;
            }
            using (var request = new UnityWebRequest(serverUrl.TrimEnd('/') + "/results", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 3;
                var operation = request.SendWebRequest();
                while (!operation.isDone) yield return null;
                string error = null;
                Response response = null;
                if (request.result != UnityWebRequest.Result.Success || request.responseCode != 201)
                    error = "POST /results: " + request.responseCode + " " + request.error + " " + request.downloadHandler.text;
                else
                {
                    try { response = JsonConvert.DeserializeObject<Response>(request.downloadHandler.text); }
                    catch (JsonException ex) { error = "Bad results response: " + ex.Message; }
                    if (error == null && (response == null || !response.ok || response.sessionId <= 0))
                        error = "Results response must be 201 with ok:true and a positive sessionId.";
                }
                if (error == null) saved?.Invoke(response.sessionId);
                else
                {
                    ReportFailure(error, json, failed);
                }
            }
        }

        private static void ReportFailure(string error, string json, Action<string> failed)
        {
            Debug.LogWarning(error);
            Debug.Log("Ghost Hands results retained: " + json);
            failed?.Invoke(error);
        }
    }
}

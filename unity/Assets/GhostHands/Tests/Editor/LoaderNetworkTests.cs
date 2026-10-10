using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GhostHands
{
    public sealed class LoaderNetworkTests
    {
        private GameObject root;
        private LessonLoader loader;
        private string oldPreference;
        private bool hadPreference;
        private UnityEditor.InsecureHttpOption oldHttp;
        private StubServer server;

        // Local HTTP contract fixture. Does not start or modify /web or use Neon.
        private sealed class StubServer : IDisposable
        {
            private readonly HttpListener listener;
            public readonly string Url;
            public int Status = 200;
            public string LessonJson;
            public string ResultsJson;
            public string LastPost;

            public StubServer()
            {
                var portProbe = new TcpListener(IPAddress.Loopback, 0);
                portProbe.Start(); int port = ((IPEndPoint)portProbe.LocalEndpoint).Port; portProbe.Stop();
                Url = "http://127.0.0.1:" + port;
                listener = new HttpListener(); listener.Prefixes.Add(Url + "/"); listener.Start();
                Task.Run(Serve);
            }

            private async Task Serve()
            {
                try
                {
                    while (listener.IsListening)
                    {
                        var context = await listener.GetContextAsync();
                        string body;
                        if (context.Request.HttpMethod == "POST")
                        {
                            using (var reader = new StreamReader(context.Request.InputStream)) LastPost = reader.ReadToEnd();
                            context.Response.StatusCode = Status == 200 ? 201 : Status;
                            body = ResultsJson ?? "{\"ok\":true,\"sessionId\":7}";
                        }
                        else
                        {
                            context.Response.StatusCode = Status;
                            body = LessonJson;
                        }
                        var bytes = Encoding.UTF8.GetBytes(body ?? "{\"errors\":[\"fixture unavailable\"]}");
                        context.Response.ContentType = "application/json";
                        context.Response.ContentLength64 = bytes.Length;
                        await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                        context.Response.Close();
                    }
                }
                catch (Exception ex) when (ex is HttpListenerException || ex is ObjectDisposedException || ex is IOException) { }
            }

            public void Dispose()
            {
                try { listener.Stop(); listener.Close(); }
                catch (ObjectDisposedException) { }
            }
        }

        [SetUp]
        public void SetUp()
        {
            hadPreference = PlayerPrefs.HasKey(LessonLoader.ServerPreference);
            oldPreference = PlayerPrefs.GetString(LessonLoader.ServerPreference);
            PlayerPrefs.DeleteKey(LessonLoader.ServerPreference);
            oldHttp = UnityEditor.PlayerSettings.insecureHttpOption;
            UnityEditor.PlayerSettings.insecureHttpOption = UnityEditor.InsecureHttpOption.AlwaysAllowed;
            root = new GameObject("Loader Test") { hideFlags = HideFlags.HideAndDontSave };
            loader = root.AddComponent<LessonLoader>();
            server = new StubServer(); loader.serverUrl = server.Url;
            server.LessonJson = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Lessons/example.json"));
        }

        [TearDown]
        public void TearDown()
        {
            server.Dispose(); UnityEngine.Object.DestroyImmediate(root);
            if (hadPreference) PlayerPrefs.SetString(LessonLoader.ServerPreference, oldPreference);
            else PlayerPrefs.DeleteKey(LessonLoader.ServerPreference);
            UnityEditor.PlayerSettings.insecureHttpOption = oldHttp;
        }

        [UnityTest]
        public IEnumerator HttpLessonLoadsWithoutFallback()
        {
            Lesson lesson = null;
            yield return loader.LoadLesson("example", value => lesson = value, Assert.Fail);
            Assert.That(lesson.lessonId, Is.EqualTo("example")); Assert.That(lesson.steps.Count, Is.EqualTo(5));
            Assert.That(loader.UsedFallback, Is.False);
        }

        [UnityTest]
        public IEnumerator ServerOffFallsBackToBundledLesson()
        {
            server.Dispose();
            LogAssert.Expect(LogType.Warning, new Regex("Server lesson unavailable:.*Using bundled example\\.", RegexOptions.Singleline));
            Lesson lesson = null;
            yield return loader.LoadLesson("example", value => lesson = value, Assert.Fail);
            Assert.That(lesson.steps.Count, Is.EqualTo(5)); Assert.That(loader.UsedFallback, Is.True);
        }

        [UnityTest]
        public IEnumerator InvalidServerUrlStillFallsBack()
        {
            loader.serverUrl = "not a server URL";
            LogAssert.Expect(LogType.Warning, new Regex("Server lesson unavailable: Invalid lesson URL:.*", RegexOptions.Singleline));
            Lesson lesson = null;
            yield return loader.LoadLesson("example", value => lesson = value, Assert.Fail);
            Assert.That(lesson.steps.Count, Is.EqualTo(5)); Assert.That(loader.UsedFallback, Is.True);
        }

        [UnityTest]
        public IEnumerator NotFoundFallsBackWithReason()
        {
            server.Status = 404; server.LessonJson = "{\"errors\":[\"Lesson not found\"]}";
            LogAssert.Expect(LogType.Warning, new Regex("Server lesson unavailable:.*404.*Using bundled example\\.", RegexOptions.Singleline));
            Lesson lesson = null;
            yield return loader.LoadLesson("missing", value => lesson = value, Assert.Fail);
            Assert.That(lesson.lessonId, Is.EqualTo("example"));
        }

        [UnityTest]
        public IEnumerator MalformedAndInvalidLessonFallBack()
        {
            server.LessonJson = "{bad";
            LogAssert.Expect(LogType.Warning, new Regex("Server lesson unavailable:.*Bad lesson JSON.*", RegexOptions.Singleline));
            Lesson lesson = null;
            yield return loader.LoadLesson("example", value => lesson = value, Assert.Fail);
            Assert.That(lesson.steps.Count, Is.EqualTo(5));
            server.LessonJson = "{\"schemaVersion\":1}";
            LogAssert.Expect(LogType.Warning, new Regex("Server lesson unavailable:.*schemaVersion 2.*", RegexOptions.Singleline));
            yield return loader.LoadLesson("example", value => lesson = value, Assert.Fail);
            Assert.That(loader.UsedFallback, Is.True);
        }

        [UnityTest]
        public IEnumerator LessonListReadsServerSummaries()
        {
            server.LessonJson = "[{\"lessonId\":\"example\",\"title\":\"Example\",\"hands\":[\"right\"],\"stepCount\":5}]";
            int count = 0;
            yield return loader.ListLessons(value => { count = value.Count; Assert.That(value[0].stepCount, Is.EqualTo(5)); }, Assert.Fail);
            Assert.That(count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ResultsPostUsesExactPayloadAndAccepts201()
        {
            var results = new SessionResults { lessonId = "example", tester = "test", completed = true, recallPassed = true };
            results.steps.Add(new StepResult { stepId = "step-01", timeToMatchMs = 700, retries = 1, trackingLosses = 2 });
            int saved = 0;
            yield return ResultsClient.Post(server.Url, results, id => saved = id, Assert.Fail);
            Assert.That(saved, Is.EqualTo(7));
            var body = JObject.Parse(server.LastPost);
            Assert.That(body.Count, Is.EqualTo(6)); Assert.That((string)body["device"], Is.EqualTo("Quest 3S"));
            Assert.That((int)body["steps"][0]["trackingLosses"], Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator InvalidResultsUrlRetainsPayload()
        {
            var results = new SessionResults { lessonId = "example" };
            LogAssert.Expect(LogType.Warning, "POST /results: invalid HTTP server URL.");
            LogAssert.Expect(LogType.Log, new Regex("Ghost Hands results retained:.*example.*", RegexOptions.Singleline));
            string failure = null;
            yield return ResultsClient.Post(null, results, _ => Assert.Fail("Unexpected success"), error => failure = error);
            Assert.That(failure, Is.Not.Null);
            Assert.That(results.lessonId, Is.EqualTo("example"));
        }

        [UnityTest]
        public IEnumerator FailedResultsPostRetainsPayload()
        {
            server.Status = 503;
            var results = new SessionResults { lessonId = "example", tester = "test" };
            LogAssert.Expect(LogType.Warning, new Regex("POST /results: 503.*", RegexOptions.Singleline));
            LogAssert.Expect(LogType.Log, new Regex("Ghost Hands results retained:.*example.*", RegexOptions.Singleline));
            string failure = null;
            yield return ResultsClient.Post(server.Url, results, _ => Assert.Fail("Unexpected success"), error => failure = error);
            Assert.That(failure, Is.Not.Null); Assert.That(results.lessonId, Is.EqualTo("example"));
        }
    }
}

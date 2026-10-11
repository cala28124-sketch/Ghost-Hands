using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GhostHands
{
    // Ghost Hands > Play Lesson: pick a server lesson (e.g. one cv/process.py just posted), then play it in the
    // editor or build it to the headset. The choice is saved as Resources/GhostHands/Launch.json (git-ignored),
    // which GhostHandsBootstrap reads at startup, in the editor and in the headset build alike.
    public sealed class PlayLessonWindow : EditorWindow
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string LaunchFile = "Assets/Resources/GhostHands/Launch.json";
        private const string ApkPath = "Builds/GhostHands.apk";
        private string serverUrl = "http://localhost:3000", lessonId = "example";
        private bool replay = true, menu = true;

        [MenuItem("Ghost Hands/Play Lesson...")]
        public static void Open() => GetWindow<PlayLessonWindow>(true, "Play Lesson");

        // Unity.exe -projectPath unity -executeMethod GhostHands.PlayLessonWindow.PlayFromCommandLine -lessonId count-14
        // Opens the editor straight into the lesson (optional: -serverUrl http://..., -live for real hand tracking).
        public static void PlayFromCommandLine()
        {
            var window = GetWindow<PlayLessonWindow>(true, "Play Lesson");
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string next = i + 1 < args.Length ? args[i + 1] : null;
                if (args[i] == "-lessonId" && next != null) window.lessonId = next;
                if (args[i] == "-serverUrl" && next != null) window.serverUrl = next;
                if (args[i] == "-live") window.replay = false;
            }
            window.menu = Array.IndexOf(args, "-menu") >= 0;  // a lesson id on the command line plays it straight away
            window.PlayInEditor();
        }

        private void OnEnable()
        {
            var saved = File.Exists(LaunchFile) ? JsonUtility.FromJson<LaunchSettings>(File.ReadAllText(LaunchFile)) : null;
            if (!string.IsNullOrEmpty(saved?.lessonId)) lessonId = saved.lessonId;
            if (!string.IsNullOrEmpty(saved?.serverUrl)) serverUrl = saved.serverUrl;
            if (saved != null) menu = !saved.skipMenu;
            replay = PlayerPrefs.GetInt(EditorReplayBootstrap.EnabledPreference, 1) != 0;
            minSize = new Vector2(400, 210);
        }

        private void OnGUI()
        {
            serverUrl = EditorGUILayout.TextField("Server URL", serverUrl);
            lessonId = EditorGUILayout.TextField("Lesson ID", lessonId);
            replay = EditorGUILayout.Toggle(new GUIContent("Replay hands (editor)",
                "On: fake hands replay each step's pose, no headset needed. Off: live hand tracking. Builds are always live."), replay);
            menu = EditorGUILayout.Toggle(new GUIContent("Start at main menu",
                "On: open at the main menu and pick a tutorial there. Off: play the Lesson ID straight away."), menu);
            EditorGUILayout.HelpBox("Lesson ID: printed by process.py (\"posted lesson <id>\"). If the server can't be reached, " +
                "the bundled example plays. On the headset, a localhost server is reached over the USB cable.", MessageType.None);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || string.IsNullOrWhiteSpace(lessonId)))
            {
                // Start after this window finishes drawing: a build or scene load in the middle of OnGUI
                // breaks its layout ("EndLayoutGroup: BeginLayoutGroup must be called first").
                if (GUILayout.Button("Play in editor", GUILayout.Height(26))) EditorApplication.delayCall += PlayInEditor;
                if (GUILayout.Button("Build and Run on Quest", GUILayout.Height(26))) EditorApplication.delayCall += BuildAndRun;
            }
        }

        private void SaveLaunchSettings()
        {
            var settings = new LaunchSettings { lessonId = lessonId.Trim(), serverUrl = serverUrl.Trim().TrimEnd('/'), skipMenu = !menu };
            Directory.CreateDirectory(Path.GetDirectoryName(LaunchFile));
            File.WriteAllText(LaunchFile, JsonUtility.ToJson(settings, true));
            AssetDatabase.ImportAsset(LaunchFile);
            // A saved PlayerPrefs URL (LessonLoader.SetServerUrl) would override the launch file in the editor.
            PlayerPrefs.DeleteKey(LessonLoader.ServerPreference);
            PlayerPrefs.Save();
        }

        private void PlayInEditor()
        {
            SaveLaunchSettings();
            if (replay) ReplayDemoMenu.Enable(); else ReplayDemoMenu.EnableLive();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (EditorSceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.isPlaying = true;
        }

        private void BuildAndRun()
        {
            if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed)
            {
                EditorUtility.DisplayDialog("Play Lesson", EditorApplication.isCompiling
                    ? "Unity is still compiling scripts. Wait for the spinner (bottom right) to stop, then build."
                    : "Scripts failed to compile. Fix the first red error in the Console, then build.", "OK");
                return;
            }
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                EditorUtility.DisplayDialog("Play Lesson", "Switch the build target to Android first (File > Build Profiles).", "OK");
                return;
            }
            SaveLaunchSettings();
            ForwardServerOverUsb();  // before the app starts and loads its lesson; again after, in case adb restarted
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettingsScene.GetActiveSceneList(EditorBuildSettings.scenes),
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.AutoRunPlayer,
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"Ghost Hands build {report.summary.result}: see the Console for the first error.");
                return;
            }
            ForwardServerOverUsb();
        }

        // The headset's "localhost" is the headset itself; adb reverse sends its localhost:<port> to this laptop.
        private void ForwardServerOverUsb()
        {
            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) || !uri.IsLoopback) return;
            string adb = Path.Combine(EditorApplication.applicationContentsPath,
                "PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe");
            var info = new ProcessStartInfo(adb, $"reverse tcp:{uri.Port} tcp:{uri.Port}")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            using (var process = Process.Start(info))
            {
                process.WaitForExit(10000);
                if (process.ExitCode == 0) Debug.Log($"Headset localhost:{uri.Port} now reaches this laptop's server over USB.");
                else Debug.LogError("adb reverse failed: " + process.StandardError.ReadToEnd());
            }
        }
    }
}

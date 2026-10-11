using System;
using UnityEngine;

namespace GhostHands
{
    // Which lesson to load and from where. Written by the editor's Ghost Hands > Play Lesson window into
    // Resources/GhostHands/Launch.json (git-ignored), so the editor and a headset build both use it.
    [Serializable]
    public sealed class LaunchSettings
    {
        public const string ResourcePath = "GhostHands/Launch";
        public string lessonId;
        public string serverUrl;
        public bool skipMenu;  // straight into lessonId instead of opening at the main menu

        public static LaunchSettings Load()
        {
            var file = Resources.Load<TextAsset>(ResourcePath);
            return file != null ? JsonUtility.FromJson<LaunchSettings>(file.text) : null;
        }
    }

    public static class GhostHandsBootstrap
    {
        // Runtime objects only: never writes SampleScene or Paul's rig/hand objects.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (UnityEngine.Object.FindAnyObjectByType<GhostDemo>() != null) return;
            if (UnityEngine.Object.FindAnyObjectByType<LessonFlow>() != null) return;
#if UNITY_EDITOR
            if (PlayerPrefs.GetInt("GhostHands.AlignmentDemo", 0) != 0)
            {
                var demoRoot = new GameObject("Ghost Hands Alignment Demo");
                var live = AddLiveSource(demoRoot);
                var demo = demoRoot.AddComponent<GhostDemo>();
                demo.handJointSource = live;
                demoRoot.GetComponent<GhostHandRenderer>().handJointSource = live;
                return;
            }
#endif
            var root = new GameObject("Ghost Hands Lesson");
            var flow = root.AddComponent<LessonFlow>();
            var launch = LaunchSettings.Load();
            if (!string.IsNullOrEmpty(launch?.lessonId)) flow.lessonId = launch.lessonId;
            if (!string.IsNullOrEmpty(launch?.serverUrl)) root.GetComponent<LessonLoader>().serverUrl = launch.serverUrl;
            bool menu = launch == null || !launch.skipMenu || string.IsNullOrEmpty(launch.lessonId);
            flow.autoStart = !menu;
            MonoBehaviour source = null;
#if UNITY_EDITOR
            if (PlayerPrefs.GetInt(EditorReplayBootstrap.EnabledPreference, 0) != 0)
            {
                var replay = root.AddComponent<ReplayJointSource>();
                replay.flow = flow;
                source = replay;
            }
#endif
            if (source == null) source = AddLiveSource(root);
            flow.handJointSource = source;
            var ghost = root.AddComponent<GhostHandRenderer>();
            ghost.handJointSource = source;
            flow.ghostHandRenderer = ghost;
            flow.RefreshBindings();
            var debug = root.AddComponent<HandTrackingDebug>(); debug.handJointSource = source;
            root.AddComponent<LessonPanel>();
            if (menu) root.AddComponent<MainMenu>();
        }

        private static XRHandsJointSource AddLiveSource(GameObject root)
        {
            var fallback = root.AddComponent<InteractionHandJointSource>();
            var live = root.AddComponent<XRHandsJointSource>();
            live.interactionFallback = fallback;
            live.ResolveRig();
            return live;
        }
    }
}

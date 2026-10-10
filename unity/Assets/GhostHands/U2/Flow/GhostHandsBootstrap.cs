using UnityEngine;

namespace GhostHands
{
    public static class GhostHandsBootstrap
    {
        // Runtime objects only: never writes SampleScene or Paul's rig/hand objects.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (Object.FindAnyObjectByType<GhostDemo>() != null) return;
            if (Object.FindAnyObjectByType<LessonFlow>() != null) return;
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

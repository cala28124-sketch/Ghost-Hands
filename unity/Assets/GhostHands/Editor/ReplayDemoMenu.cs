using UnityEditor;
using UnityEngine;

namespace GhostHands
{
    public static class ReplayDemoMenu
    {
        [MenuItem("Ghost Hands/Enable Editor Replay Demo")]
        public static void Enable()
        {
            PlayerPrefs.SetInt(EditorReplayBootstrap.EnabledPreference, 1);
            PlayerPrefs.DeleteKey("GhostHands.AlignmentDemo");
            PlayerPrefs.Save();
            Debug.Log("Combined replay enabled. Enter Play mode: two ghosts, practice, hidden recall, and results. Scene is unchanged.");
        }

        [MenuItem("Ghost Hands/Disable Editor Replay Demo")]
        public static void Disable()
        {
            PlayerPrefs.DeleteKey(EditorReplayBootstrap.EnabledPreference);
            PlayerPrefs.Save();
        }

        [MenuItem("Ghost Hands/Enable Standalone Alignment Demo")]
        public static void EnableAlignment()
        {
            PlayerPrefs.SetInt("GhostHands.AlignmentDemo", 1);
            PlayerPrefs.DeleteKey(EditorReplayBootstrap.EnabledPreference);
            PlayerPrefs.Save();
            Debug.Log("Alignment demo enabled. Enter Play mode; A/pinch/N next, B/P previous. Uses live tracking and bundled lesson.");
        }

        [MenuItem("Ghost Hands/Enable Integrated Live Lesson")]
        public static void EnableLive()
        {
            PlayerPrefs.DeleteKey("GhostHands.AlignmentDemo");
            PlayerPrefs.DeleteKey(EditorReplayBootstrap.EnabledPreference);
            PlayerPrefs.Save();
            Debug.Log("Integrated live lesson enabled. Enter Play mode to use XR Hands with Interaction SDK fallback.");
        }
    }
}

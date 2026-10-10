using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace GhostHands
{
    // Runs before Android builds; no scene or asset edits are needed.
    public sealed class AndroidNetworkSettings : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android) return;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            PlayerSettings.Android.forceInternetPermission = true;
        }

        [MenuItem("Ghost Hands/Allow Android HTTP")]
        public static void Configure()
        {
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            PlayerSettings.Android.forceInternetPermission = true;
        }
    }
}

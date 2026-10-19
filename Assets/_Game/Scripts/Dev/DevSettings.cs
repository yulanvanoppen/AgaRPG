#if UNITY_EDITOR
using ParrelSync;
#endif

namespace AgaRPG.Dev
{
    /// <summary>
    /// Development switches. The values are only ever true inside the Unity editor, so builds
    /// always run the full online flow.
    /// </summary>
    public static class DevSettings
    {
        /// <summary>EditorPrefs key toggled by the "AgaRPG/Quick Start" menu.</summary>
        public const string QuickStartPrefKey = "AgaRPG.QuickStart";

        /// <summary>
        /// Skip login, lobby and relay: host on localhost and load straight into the Game scene
        /// (ParrelSync clones join as clients).
        /// </summary>
        public static bool QuickStartEnabled
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.EditorPrefs.GetBool(QuickStartPrefKey, false);
#else
                return false;
#endif
            }
        }

        /// <summary>True in a ParrelSync clone of the project.</summary>
        public static bool IsEditorClone
        {
            get
            {
#if UNITY_EDITOR
                return ClonesManager.IsClone();
#else
                return false;
#endif
            }
        }
    }
}

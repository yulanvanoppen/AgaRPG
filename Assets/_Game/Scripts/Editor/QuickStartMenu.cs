using AgaRPG.Dev;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AgaRPG.EditorTools
{
    /// <summary>
    /// Editor toggle for the development quick start: when enabled, entering Play Mode from any
    /// scene starts in the Auth scene (which holds the NetworkManager and persistent managers),
    /// skips login and hosts a local game straight away. Replaces the old workflow of enabling
    /// test copies of the NetworkManager and player inside the Game scene.
    /// </summary>
    [InitializeOnLoad]
    public static class QuickStartMenu
    {
        private const string MenuPath = "AgaRPG/Quick Start (skip login)";
        private const string StartScenePath = "Assets/_Game/Scenes/Auth.unity";

        static QuickStartMenu() => EditorApplication.delayCall += ApplyStartScene;

        [MenuItem(MenuPath, priority = 0)]
        private static void Toggle()
        {
            EditorPrefs.SetBool(DevSettings.QuickStartPrefKey, !DevSettings.QuickStartEnabled);
            ApplyStartScene();
        }

        [MenuItem(MenuPath, validate = true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, DevSettings.QuickStartEnabled);
            return !EditorApplication.isPlaying;
        }

        private static void ApplyStartScene()
        {
            if (!DevSettings.QuickStartEnabled)
            {
                EditorSceneManager.playModeStartScene = null;
                return;
            }

            // Find the scene by name so it keeps working if the folder layout changes.
            SceneAsset startScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(StartScenePath);
            if (startScene == null)
            {
                foreach (string guid in AssetDatabase.FindAssets("Auth t:Scene"))
                {
                    startScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(AssetDatabase.GUIDToAssetPath(guid));
                    if (startScene != null && startScene.name == "Auth") break;
                }
            }

            if (startScene == null) Debug.LogWarning("Quick start: could not find the Auth scene.");
            EditorSceneManager.playModeStartScene = startScene;
        }
    }
}

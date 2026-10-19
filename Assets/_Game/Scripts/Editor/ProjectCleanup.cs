using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AgaRPG.Networking;
using FishNet.Object;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AgaRPG.EditorTools
{
    /// <summary>
    /// One-time migration that reorganises the project after the code refactor:
    /// moves scripts into feature folders (keeping GUIDs, so no references break), removes
    /// obsolete test assets, turns the arrow tracer into a local-only effect and fixes stale
    /// type names in UI event bindings. Safe to run more than once; offers to delete itself at the end.
    /// </summary>
    public static class ProjectCleanup
    {
        private const string Root = "Assets/_Game";
        private const string Scripts = Root + "/Scripts";
        private const string OldCharacterScripts = Root + "/Character/Scripts";
        private const string OldLobbyScripts = Root + "/Lobby/Scripts";

        private const string PlayerPrefabPath = Root + "/Character/NetworkPlayer.prefab";
        private const string GameManagerPrefabPath = Root + "/Lobby/Game Manager.prefab";
        private const string TracerPrefabPath = Root + "/Character/Prefabs/Weapons/ArrowTracer.prefab";

        /// <summary>(folder, file names) - every script that moves out of the old Scripts folders.</summary>
        private static readonly (string folder, string[] files)[] ScriptMoves =
        {
            ("Player", new[] { "PlayerMovement.cs", "PlayerCameraControl.cs", "PlayerAnimation.cs", "PlayerState.cs",
                               "PlayerAttributes.cs", "InputController.cs", "BasicRigidBodyPush.cs", "OnStrikeEnd.cs" }),
            ("Combat", new[] { "PlayerCombat.cs", "PlayerWeapons.cs", "AbstractWeapon.cs", "MeleeWeapon.cs", "RaycastWeapon.cs",
                               "WeaponRecoil.cs", "WeaponPickup.cs", "PositionClippingPlane.cs" }),
            ("Networking", new[] { "GameManager.cs", "AuthenticationManager.cs", "AuthenticationService.cs", "CloudSaveManager.cs" }),
            ("UI", new[] { "PauseMenu.cs", "ReticleControl.cs", "FloatingHealthBar.cs", "TagSync.cs", "CanvasUtilities.cs",
                           "ButtonClick.cs", "MyLog.cs" }),
            ("Audio", new[] { "DoubleAudioSource.cs", "SFXManager.cs", "SilenceOnServer.cs" }),
        };

        /// <summary>Files renamed after moving so the file name matches the type it contains.</summary>
        private static readonly (string path, string newName)[] Renames =
        {
            (Scripts + "/Networking/AuthenticationService.cs", "Authentication"),
            (Scripts + "/Audio/SFXManager.cs", "RandomAudioPlaylistLoop"),
        };

        private static readonly string[] ObsoleteAssets =
        {
            Root + "/Character/OLDNetworkPlayer.prefab",
            Root + "/Character/SimpleNetworkPlayer.prefab",
            Root + "/Character/Prefabs/UI_Canvas_StarterAssetsInputs_Joysticks.prefab",
            Root + "/Scenes/Test.unity",
            Root + "/Scenes/GameBackup.unity",
            OldCharacterScripts + "/IDSync.cs",
            OldCharacterScripts + "/IDamageable.cs",
            OldCharacterScripts + "/TracerDespawner.cs",
            OldLobbyScripts + "/TestCSManager.cs",
            "Assets/TestScript.cs",
        };

        /// <summary>UnityEvent bindings store the target type name; update them to the namespaced types.</summary>
        private static readonly (string oldType, string newType)[] EventTypeRenames =
        {
            ("PauzeMenu", "AgaRPG.UI.PauseMenu"),
            ("PauseMenu", "AgaRPG.UI.PauseMenu"),
            ("CloudSaveManager", "AgaRPG.Networking.CloudSaveManager"),
            ("AuthenticationManager", "AgaRPG.Networking.AuthenticationManager"),
        };

        [MenuItem("AgaRPG/Clean Up Project...", priority = 100)]
        private static void Run()
        {
            const string summary =
                "This will:\n\n" +
                "• Move the game scripts into Assets/_Game/Scripts/<Player|Combat|Networking|UI|Audio>\n" +
                "• Point the Game Manager prefab at NetworkPlayer\n" +
                "• Make ArrowTracer a local-only effect (remove its NetworkObject)\n" +
                "• Move obsolete test assets to the trash (OLD/Simple player prefabs, Test scene, test scripts)\n" +
                "• Update UI event type names in scenes and prefabs\n\n" +
                "Commit your work first. Continue?";

            if (!EditorUtility.DisplayDialog("Clean up project", summary, "Run", "Cancel")) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            SceneSetup[] openScenes = EditorSceneManager.GetSceneManagerSetup();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);   // release scene files

            var log = new StringBuilder();
            try
            {
                FixGameManagerPrefab(log);
                StripTracerNetworking(log);
                FixEventTypeNames(log);
                RemoveFromBuildSettings(log);
                TrashObsoleteAssets(log);
                MoveScripts(log);
                DeleteEmptyFolder(OldCharacterScripts, log);
                DeleteEmptyFolder(OldLobbyScripts, log);
            }
            finally
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                if (openScenes.Length > 0 && openScenes.All(s => File.Exists(s.path)))
                    EditorSceneManager.RestoreSceneManagerSetup(openScenes);
            }

            Debug.Log("[Project cleanup]\n" + log);

            if (EditorUtility.DisplayDialog("Clean up project",
                    "Done - see the Console for details.\n\nThis tool is only needed once. Move it to the trash?",
                    "Remove tool", "Keep"))
            {
                string self = AssetDatabase.FindAssets($"{nameof(ProjectCleanup)} t:MonoScript")
                                           .Select(AssetDatabase.GUIDToAssetPath)
                                           .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == nameof(ProjectCleanup));
                if (self != null) AssetDatabase.MoveAssetToTrash(self);
            }
        }

        private static void FixGameManagerPrefab(StringBuilder log)
        {
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (playerPrefab == null || AssetDatabase.LoadAssetAtPath<GameObject>(GameManagerPrefabPath) == null) return;

            EditPrefab(GameManagerPrefabPath, root =>
            {
                var manager = root.GetComponentInChildren<GameManager>(true);
                if (manager == null) return false;

                var serialized = new SerializedObject(manager);
                SerializedProperty property = serialized.FindProperty("_playerPrefab");
                if (property.objectReferenceValue == playerPrefab) return false;

                property.objectReferenceValue = playerPrefab;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                log.AppendLine($"Game Manager prefab now spawns {playerPrefab.name}.");
                return true;
            });
        }

        private static void StripTracerNetworking(StringBuilder log)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(TracerPrefabPath) == null) return;

            EditPrefab(TracerPrefabPath, root =>
            {
                int removed = 0;
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                foreach (NetworkBehaviour behaviour in root.GetComponentsInChildren<NetworkBehaviour>(true))
                {
                    Object.DestroyImmediate(behaviour, true);
                    removed++;
                }
                foreach (NetworkObject networkObject in root.GetComponentsInChildren<NetworkObject>(true))
                {
                    Object.DestroyImmediate(networkObject, true);
                    removed++;
                }

                if (removed > 0) log.AppendLine($"ArrowTracer: removed {removed} networking component(s).");
                return removed > 0;
            });
        }

        private static void FixEventTypeNames(StringBuilder log)
        {
            IEnumerable<string> files = Directory.GetFiles("Assets", "*.unity", SearchOption.AllDirectories)
                                                 .Concat(Directory.GetFiles("Assets", "*.prefab", SearchOption.AllDirectories));
            foreach (string file in files)
            {
                string text = File.ReadAllText(file);
                string updated = text;
                foreach ((string oldType, string newType) in EventTypeRenames)
                {
                    // Only bare (non-namespaced) names directly after "TypeName: " or "value: ".
                    updated = Regex.Replace(updated, $@"(?<=(TypeName|value): ){oldType}, Assembly-CSharp(?![\w-])",
                                            $"{newType}, Assembly-CSharp");
                }

                if (updated == text) continue;
                File.WriteAllText(file, updated);
                log.AppendLine($"Updated UI event type names in {file}.");
            }
        }

        private static void RemoveFromBuildSettings(StringBuilder log)
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            EditorBuildSettingsScene[] kept = scenes
                .Where(s => !ObsoleteAssets.Contains(s.path) && File.Exists(s.path))
                .GroupBy(s => s.path).Select(g => g.First())
                .ToArray();

            if (kept.Length == scenes.Length) return;
            EditorBuildSettings.scenes = kept;
            log.AppendLine($"Build settings: removed {scenes.Length - kept.Length} obsolete scene entr(ies).");
        }

        private static void TrashObsoleteAssets(StringBuilder log)
        {
            foreach (string path in ObsoleteAssets)
            {
                if (!Exists(path)) continue;
                if (AssetDatabase.MoveAssetToTrash(path)) log.AppendLine($"Moved to trash: {path}");
                else log.AppendLine($"Could not remove {path}");
            }
        }

        private static void MoveScripts(StringBuilder log)
        {
            EnsureFolder(Scripts);
            foreach ((string folder, string[] files) in ScriptMoves)
            {
                string target = $"{Scripts}/{folder}";
                EnsureFolder(target);

                foreach (string file in files)
                {
                    string from = Exists($"{OldCharacterScripts}/{file}") ? $"{OldCharacterScripts}/{file}" : $"{OldLobbyScripts}/{file}";
                    string to = $"{target}/{file}";
                    if (!Exists(from) || Exists(to)) continue;

                    string error = AssetDatabase.MoveAsset(from, to);
                    log.AppendLine(string.IsNullOrEmpty(error) ? $"Moved {from} -> {to}" : $"Could not move {from}: {error}");
                }
            }

            foreach ((string path, string newName) in Renames)
            {
                if (!Exists(path)) continue;
                string error = AssetDatabase.RenameAsset(path, newName);
                log.AppendLine(string.IsNullOrEmpty(error) ? $"Renamed {path} -> {newName}.cs" : $"Could not rename {path}: {error}");
            }
        }

        private static void EditPrefab(string path, System.Func<GameObject, bool> edit)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (edit(root)) PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static void DeleteEmptyFolder(string path, StringBuilder log)
        {
            if (!AssetDatabase.IsValidFolder(path)) return;
            if (Directory.EnumerateFileSystemEntries(path).Any(e => !e.EndsWith(".meta"))) return;
            AssetDatabase.DeleteAsset(path);
            log.AppendLine($"Deleted empty folder {path}");
        }

        private static bool Exists(string path) => !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets));
    }
}

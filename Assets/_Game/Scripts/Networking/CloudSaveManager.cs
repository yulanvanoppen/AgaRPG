using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace AgaRPG.Networking
{
    /// <summary>
    /// Persistent player profile (size and colour) stored with Unity Cloud Save, plus the lobby
    /// UI that shows the player's id and name. Survives scene loads. When the player is not
    /// signed in (e.g. the editor quick start) it keeps working with local defaults.
    /// </summary>
    public class CloudSaveManager : MonoBehaviour
    {
        [FormerlySerializedAs("playerText")]
        [SerializeField] private Text _playerText;

        [FormerlySerializedAs("tagField")]
        [SerializeField] private InputField _tagField;

        private const string SizeKey = "SIZE_STATE";
        private const string ColorKey = "COLOR_STATE";
        private const string ManagerTag = "CloudSaveManager";

        public string PlayerId { get; set; } = string.Empty;
        public string PlayerTag { get; private set; } = "Player";
        public float PlayerSize { get; private set; } = 1f;
        public Color PlayerColor { get; private set; } = Color.white;

        private static bool CanUseCloud =>
            UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn;

        private void Awake()
        {
            // Keep the instance from the first scene; drop duplicates when the lobby scene reloads.
            if (GameObject.FindGameObjectsWithTag(ManagerTag).Length > 1)
            {
                gameObject.SetActive(false);
                Destroy(gameObject);
                return;
            }

            DontDestroyOnLoad(gameObject);
        }

        /// <summary>Bound to the name input field's OnEndEdit.</summary>
        public async void EditTag()
        {
            await Authentication.SetPlayerTagAsync(_tagField.text);
            PlayerTag = Authentication.PlayerTag;
            _tagField.text = PlayerTag;
        }

        /// <summary>Refreshes the lobby UI from the signed-in player.</summary>
        public void UpdateText()
        {
            PlayerTag = Authentication.PlayerTag;
            _playerText.text = $"ID: {Authentication.PlayerId} (SIZE: {PlayerSize * 100f:0}%)";
            _tagField.text = PlayerTag;
        }

        /// <summary>Loads size and colour from Cloud Save, writing defaults for new players.</summary>
        public async Task FetchPlayerDataAsync()
        {
            if (!CanUseCloud) return;

            try
            {
                var data = await CloudSaveService.Instance.Data.Player.LoadAllAsync();

                if (data.TryGetValue(SizeKey, out var size))
                    PlayerSize = float.Parse(size.Value.GetAsString(), CultureInfo.InvariantCulture);
                else await SaveSize(1f, force: true);

                if (data.TryGetValue(ColorKey, out var color) &&
                    ColorUtility.TryParseHtmlString('#' + color.Value.GetAsString(), out Color parsed))
                    PlayerColor = parsed;
                else await SaveColor(Color.white, force: true);

                PlayerTag = Authentication.PlayerTag;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>Stores the player size. Skips the upload when unchanged (unless forced) or offline.</summary>
        public Task SaveSize(float size, bool force = false)
        {
            bool changed = !Mathf.Approximately(PlayerSize, size);
            PlayerSize = size;
            return changed || force ? SaveAsync(SizeKey, size) : Task.CompletedTask;
        }

        /// <summary>Stores the player colour. Skips the upload when unchanged (unless forced) or offline.</summary>
        public Task SaveColor(Color color, bool force = false)
        {
            bool changed = PlayerColor != color;
            PlayerColor = color;
            return changed || force ? SaveAsync(ColorKey, ColorUtility.ToHtmlStringRGBA(color)) : Task.CompletedTask;
        }

        private static async Task SaveAsync(string key, object value)
        {
            if (!CanUseCloud) return;

            try
            {
                await CloudSaveService.Instance.Data.Player.SaveAsync(new Dictionary<string, object> { { key, value } });
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}

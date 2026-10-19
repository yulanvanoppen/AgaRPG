using AgaRPG.Networking;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace AgaRPG.UI
{
    /// <summary>
    /// Name tag above a player. The owner sends its player name once; the server stores it in a
    /// <see cref="SyncVar{T}"/> so all current and future clients receive it automatically.
    /// </summary>
    public class TagSync : NetworkBehaviour
    {
        [FormerlySerializedAs("Tag")]
        [SerializeField] private Text _label;

        private const string CloudSaveManagerTag = "CloudSaveManager";
        private const int MaxTagLength = 32;

        private readonly SyncVar<string> _playerTag = new(string.Empty);

        private void Awake() => _playerTag.OnChange += (_, next, _) => _label.text = next;

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!base.IsOwner) return;

            string playerTag = GameObject.FindGameObjectWithTag(CloudSaveManagerTag)
                                         .GetComponent<CloudSaveManager>().PlayerTag;
            _label.text = playerTag;
            ServerRpcSetTag(playerTag);
        }

        [ServerRpc]
        private void ServerRpcSetTag(string playerTag)
        {
            if (playerTag != null && playerTag.Length > MaxTagLength) playerTag = playerTag.Substring(0, MaxTagLength);
            _playerTag.Value = playerTag ?? string.Empty;
        }

        private void LateUpdate() => Billboard.FaceMainCamera(transform);
    }
}

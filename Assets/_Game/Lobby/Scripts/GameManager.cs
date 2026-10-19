using FishNet;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace AgaRPG.Networking
{
    /// <summary>
    /// Scene object in the Game scene that spawns a player for every client once that client has
    /// the scene loaded, and provides spawn positions.
    /// </summary>
    public class GameManager : NetworkBehaviour
    {
        [SerializeField] private GameObject _playerPrefab;

        /// <summary>Half extent of the square spawn area around the world origin.</summary>
        private const float SpawnAreaHalfSize = 2f;

        /// <summary>Random position inside the spawn area.</summary>
        public static Vector3 GetRandomSpawnPosition() =>
            new Vector3(Random.Range(-SpawnAreaHalfSize, SpawnAreaHalfSize), 0f, Random.Range(-SpawnAreaHalfSize, SpawnAreaHalfSize));

        public override void OnStartClient()
        {
            base.OnStartClient();
            ServerRpcSpawnPlayer();
        }

        // RequireOwnership = false: every client calls this on the server-owned manager.
        // FishNet fills 'caller' with the sending connection, so a client can't spawn for someone else.
        [ServerRpc(RequireOwnership = false)]
        private void ServerRpcSpawnPlayer(NetworkConnection caller = null)
        {
            if (_playerPrefab == null)
            {
                Debug.LogError($"{nameof(GameManager)}: no player prefab assigned.", this);
                return;
            }

            GameObject player = Instantiate(_playerPrefab, GetRandomSpawnPosition(), Quaternion.identity);
            base.Spawn(player, caller);
        }

        private void OnApplicationQuit()
        {
            if (base.IsClientInitialized) InstanceFinder.ClientManager.StopConnection();
            if (base.IsServerInitialized) InstanceFinder.ServerManager.StopConnection(true);
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            if (InstanceFinder.NetworkManager != null && base.IsClientInitialized)
                InstanceFinder.ClientManager.StopConnection();
        }
    }
}

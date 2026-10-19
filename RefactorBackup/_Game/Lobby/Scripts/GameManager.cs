using FishNet;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

public class GameManager : NetworkBehaviour {
    [SerializeField] private GameObject _playerPrefab;

    // Temporary diagnostics for the "player doesn't spawn" bug. Remove once fixed.
    private const string Tag = "[GameManager]";

    private void Awake() => Debug.Log($"{Tag} Awake (scene: {gameObject.scene.name}, prefab set: {_playerPrefab != null})");

    public override void OnStartServer()
    {
        base.OnStartServer();
        Debug.Log($"{Tag} OnStartServer");
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        Debug.Log($"{Tag} OnStartClient (clientId: {base.LocalConnection?.ClientId}, isServer: {base.IsServerInitialized})");
        SpawnPlayerServerRpc();
    }

    // FishNet fills 'caller' with the connection that sent the RPC, so clients can't spawn for someone else.
    [ServerRpc(RequireOwnership = false)]
    private void SpawnPlayerServerRpc(NetworkConnection caller = null) {
        Debug.Log($"{Tag} SpawnPlayerServerRpc from client {caller?.ClientId}");

        if (_playerPrefab == null)
        {
            Debug.LogError($"{Tag} _playerPrefab is not assigned on the Game Manager in this scene.");
            return;
        }

        var spawn = Instantiate(_playerPrefab, new Vector3(Random.value * 4f - 2f, 0f, Random.value * 4f - 2f),
                                Quaternion.identity);
        base.Spawn(spawn, caller);
        Debug.Log($"{Tag} Spawned {spawn.name} for client {caller?.ClientId}");
    }

    public void OnApplicationQuit()
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

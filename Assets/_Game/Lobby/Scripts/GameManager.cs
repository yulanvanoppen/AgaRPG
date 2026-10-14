using FishNet;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

public class GameManager : NetworkBehaviour {
    [SerializeField] private GameObject _playerPrefab;

    public override void OnStartClient() => SpawnPlayerServerRpc(InstanceFinder.ClientManager.Connection);

    [ServerRpc(RequireOwnership = false)]
    private void SpawnPlayerServerRpc(NetworkConnection connection) {
        var spawn = Instantiate(_playerPrefab, new Vector3(Random.value * 4f - 2f, 0f, Random.value * 4f - 2f),
                                Quaternion.identity);
        base.Spawn(spawn, connection);
    }

    public void OnApplicationQuit()
    {
        if (base.IsClient) InstanceFinder.ClientManager.StopConnection();
        if (base.IsServer) InstanceFinder.ServerManager.StopConnection(true);
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        if (InstanceFinder.NetworkManager != null && base.IsClient)
            InstanceFinder.ClientManager.StopConnection();
    }
}
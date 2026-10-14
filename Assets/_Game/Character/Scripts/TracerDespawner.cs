using FishNet;
using FishNet.Connection;
using FishNet.Object;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class TracerDespawner : NetworkBehaviour
{
    private TrailRenderer _trailRenderer;

    private void Awake() => _trailRenderer = GetComponent<TrailRenderer>();

    public override void OnStartClient()
    {
        base.OnStartClient();
        StartCoroutine(DespawnCoroutine(.5f));
    }

    IEnumerator DespawnCoroutine(float time)
    {
        yield return new WaitForSeconds(time);
        if (base.IsOwner) RpcDespawn();
        if (base.IsServer) base.Despawn();
    }

    [ServerRpc]
    private void RpcDespawn() => base.Despawn();
}

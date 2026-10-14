using FishNet.Object;
using System.Collections.Generic;
using Unity.Services.Authentication;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

public class IDSync : NetworkBehaviour
{
    public Text ID;
    private GameObject _mainCameraGO;
    private bool _initialized;
    private CloudSaveManager _cloudSaveManager;

    private void Awake() => _cloudSaveManager = GameObject.FindGameObjectWithTag("CloudSaveManager")
                                                          .GetComponent<CloudSaveManager>();

    public override void OnStartClient()
    {
        Initialize();
        base.OnStartClient();
    }

    private void Initialize()
    {
        if (_initialized) return;

        if (_mainCameraGO == null)
            _mainCameraGO = GameObject.FindGameObjectWithTag("MainCamera");

        if (IsOwner)
        {
            ID.text = _cloudSaveManager.playerId;
            UpdateText();
            InvokeRepeating("UpdateText", 1f, 1f);
        }

        _initialized = true;
    }

    void Update()
    {
        if (_mainCameraGO == null)
            _mainCameraGO = GameObject.FindGameObjectWithTag("MainCamera");
        transform.rotation = _mainCameraGO.transform.rotation;
    }

    private void UpdateText() => UpdateTextServerRpc(_cloudSaveManager.playerId);

    [ServerRpc]
    private void UpdateTextServerRpc(string text) => UpdateTextObserversRpc(text);

    [ObserversRpc]
    private void UpdateTextObserversRpc(string text)
    {
        if (!IsOwner) ID.text = text;
    }
}

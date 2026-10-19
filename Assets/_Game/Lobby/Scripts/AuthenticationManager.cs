using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishNet;
using FishNet.Managing.Scened;
using FishNet.Transporting.UTP;
using Unity.Services.CloudSave;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Authentication.PlayerAccounts.Samples;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.UI;
using System.Threading;

public class AuthenticationManager : MonoBehaviour
{
    [HideInInspector] public string playerId = string.Empty;

    public Button signInButton;
    public Button signOutButton;

    private Lobby _connectedLobby;
    private UnityTransport _transport;
    private const string JoinCodeKey = "j";

    private Lobby _currentLobby = null;
    private static CancellationTokenSource _heartbeatSource;
    private const int HeartbeatInterval = 15;

    [SerializeField] private RandomAudioPlaylistLoop _sfxManager;
    [SerializeField] private CloudSaveManager _cloudSaveManager;


    protected virtual void Awake()
    {
        _transport = FindAnyObjectByType<UnityTransport>();

        GameObject[] objs = GameObject.FindGameObjectsWithTag("AuthenticationManager");
        if (objs.Length > 1) Destroy(this.gameObject); 

        DontDestroyOnLoad(this.gameObject);
    }

    private async void Start()
    {
        if (Application.isBatchMode) Play();
        Application.logMessageReceived += HandleLog;
        await Authentication.StartUp();

        LoginAnonymously();
    }

    public async void Play()
    {
        if (!Authentication.signedIn)
            await Authentication.LoginAnonymously();

        playerId = _cloudSaveManager.playerId = Authentication.playerId;
        Debug.Log(playerId);

        _connectedLobby = await QuickJoinLobby() ?? await CreateLobby();
        Heartbeat();

        if (_connectedLobby != null && InstanceFinder.NetworkManager.IsServerStarted)
        {
            _sfxManager.StartGame();

            var loadData = new SceneLoadData("Game");
            loadData.ReplaceScenes = ReplaceOption.All;
            InstanceFinder.SceneManager.LoadGlobalScenes(loadData);
            //Debug.Log("Scene Loaded");
        }
    }

    private async void Heartbeat()
    {
        _heartbeatSource = new CancellationTokenSource();
        while (!_heartbeatSource.IsCancellationRequested && _currentLobby != null)
        {
            await Lobbies.Instance.SendHeartbeatPingAsync(_currentLobby.Id);
            await Task.Delay(HeartbeatInterval * 1000);
        }
    }


    public async void LoginAnonymously()
    {
        using (new Load("Logging you in..."))
            await Authentication.LoginAnonymously();

        await _cloudSaveManager.FetchPlayerData();
        _cloudSaveManager.UpdateText();
    }


    public async void Login()
    {
        if (Authentication.signedIn) Authentication.SignOut();

        using (new Load("Logging you in..."))
            await Authentication.Login();

        signOutButton.gameObject.SetActive(true);
        signInButton.gameObject.SetActive(false);

        await _cloudSaveManager.FetchPlayerData();
        _cloudSaveManager.UpdateText();
    }


    public void SignOut()
    {
        Authentication.SignOut();
        LoginAnonymously();
        signOutButton.gameObject.SetActive(false);
        signInButton.gameObject.SetActive(true);
    }





    private async Task<Lobby> QuickJoinLobby()
    {
        try
        {
            // Attempt to join a lobby in progress
            var lobby = await Lobbies.Instance.QuickJoinLobbyAsync();

            // If we found one, grab the relay allocation details
            var a = await RelayService.Instance.JoinAllocationAsync(lobby.Data[JoinCodeKey].Value);

            // Set the details to the transform
            SetTransformAsClient(a);

            // Join the game room as a client
            InstanceFinder.ClientManager.StartConnection();
            return lobby;
        }
        catch (Exception e)
        {
            Debug.Log($"No lobbies available via quick join: {e.Message}");
            return null;
        }
    }

    private async Task<Lobby> CreateLobby()
    {
        try
        {
            const int maxPlayers = 100;

            // Create a relay allocation and generate a join code to share with the lobby
            var a = await RelayService.Instance.CreateAllocationAsync(maxPlayers);
            var joinCode = await RelayService.Instance.GetJoinCodeAsync(a.AllocationId);

            // Create a lobby, adding the relay join code to the lobby data
            var options = new CreateLobbyOptions
            {
                Data = new Dictionary<string, DataObject> { { JoinCodeKey, new DataObject(DataObject.VisibilityOptions.Public, joinCode) } }
            };
            var lobby = await Lobbies.Instance.CreateLobbyAsync("Useless Lobby Name", maxPlayers, options);

            // Send a heartbeat every 15 seconds to keep the room alive
            StartCoroutine(HeartbeatLobbyCoroutine(lobby.Id, 15));

            // Set the game room to use the relay allocation
            _transport.SetHostRelayData(a.RelayServer.IpV4, (ushort)a.RelayServer.Port, a.AllocationIdBytes, a.Key, a.ConnectionData);

            // Start the room. I'm doing this immediately, but maybe you want to wait for the lobby to fill up
            InstanceFinder.ServerManager.StartConnection();
            if (!Application.isBatchMode) InstanceFinder.ClientManager.StartConnection();
            return lobby;
        }
        catch (Exception e)
        {
            Debug.LogFormat($"Failed creating a lobby: {e.Message}");
            return null;
        }
    }

    private void SetTransformAsClient(JoinAllocation a)
            => _transport.SetClientRelayData(a.RelayServer.IpV4,
                                             (ushort)a.RelayServer.Port,
                                             a.AllocationIdBytes,
                                             a.Key,
                                             a.ConnectionData,
                                             a.HostConnectionData);

    private static IEnumerator HeartbeatLobbyCoroutine(string lobbyId, float waitTimeSeconds)
    {
        var delay = new WaitForSecondsRealtime(waitTimeSeconds);
        while (true)
        {
            Lobbies.Instance.SendHeartbeatPingAsync(lobbyId);
            yield return delay;
        }
    }

    private void HandleLog(string logString, string stackTrace, LogType type) => Console.WriteLine("--------------------------------------------------------");

    private void OnDestroy()
    {
        Application.logMessageReceived -= HandleLog;

        try
        {
            StopAllCoroutines();
            // todo: Add a check to see if you're host
            if (_connectedLobby != null)
            {
                if (_connectedLobby.HostId == playerId) Lobbies.Instance.DeleteLobbyAsync(_connectedLobby.Id);
                else Lobbies.Instance.RemovePlayerAsync(_connectedLobby.Id, playerId);
            }
        }
        catch (Exception e)
        {
            Debug.Log($"Error shutting down lobby: {e}");
        }
    }
}
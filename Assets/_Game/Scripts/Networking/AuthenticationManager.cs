using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using AgaRPG.Audio;
using AgaRPG.Dev;
using AgaRPG.UI;
using FishNet;
using FishNet.Managing.Scened;
using FishNet.Transporting.UTP;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace AgaRPG.Networking
{
    /// <summary>
    /// Entry point of the game (lives in the Auth scene and persists): signs the player in,
    /// then on "Play" quick-joins an existing lobby or creates a new one, connects through
    /// Unity Relay and loads the Game scene for everyone.
    /// </summary>
    /// <remarks>
    /// Flow: <c>Play</c> → quick join (client via the lobby's relay join code) or create
    /// (relay allocation + lobby + host) → host loads the Game scene with FishNet's scene manager.
    /// A headless (batch mode) build hosts a dedicated server immediately; the editor quick start
    /// (<see cref="DevSettings"/>) skips all online services and hosts on localhost.
    /// </remarks>
    public class AuthenticationManager : MonoBehaviour
    {
        [FormerlySerializedAs("signInButton")]
        [SerializeField] private Button _signInButton;

        [FormerlySerializedAs("signOutButton")]
        [SerializeField] private Button _signOutButton;

        [SerializeField] private RandomAudioPlaylistLoop _sfxManager;
        [SerializeField] private CloudSaveManager _cloudSaveManager;

        [Header("Session")]
        [SerializeField] private int _maxPlayers = 100;
        [SerializeField] private string _lobbyName = "AgaRPG";
        [SerializeField] private string _gameSceneName = "Game";
        [SerializeField] private float _heartbeatInterval = 15f;

        [Header("Editor quick start")]
        [SerializeField] private ushort _localPort = 7777;

        private const string ManagerTag = "AuthenticationManager";
        private const string JoinCodeKey = "j";

        private UnityTransport _transport;
        private Lobby _connectedLobby;
        private string _playerId = string.Empty;

        #region Lifecycle
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
            _transport = FindAnyObjectByType<UnityTransport>();
        }

        private async void Start()
        {
            if (DevSettings.QuickStartEnabled)
            {
                StartLocalSession();
                return;
            }

            await Authentication.InitializeAsync();

            // Headless server: skip the lobby UI and go straight to hosting.
            if (Application.isBatchMode) Play();
            else LoginAnonymously();
        }

        private void OnDestroy()
        {
            StopAllCoroutines();
            LeaveLobby();
        }
        #endregion

        #region UI callbacks (bound in the Auth scene)
        /// <summary>Bound to the Play button.</summary>
        public async void Play()
        {
            if (!Authentication.IsSignedIn) await Authentication.SignInAnonymouslyAsync();
            _playerId = _cloudSaveManager.PlayerId = Authentication.PlayerId;

            _connectedLobby = await QuickJoinLobbyAsync() ?? await CreateLobbyAsync();

            // Only the host loads the game scene; FishNet loads it on clients as they connect.
            if (_connectedLobby != null && InstanceFinder.NetworkManager.IsServerStarted) LoadGameScene();
        }

        /// <summary>Bound to the Sign In button.</summary>
        public async void Login()
        {
            if (Authentication.IsSignedIn) Authentication.SignOut();

            using (new Load("Logging you in..."))
                await Authentication.SignInWithUnityAsync();

            SetSignedInWithUnity(true);
            await RefreshProfileAsync();
        }

        /// <summary>Bound to the Sign Out button.</summary>
        public void SignOut()
        {
            Authentication.SignOut();
            SetSignedInWithUnity(false);
            LoginAnonymously();
        }

        public async void LoginAnonymously()
        {
            using (new Load("Logging you in..."))
                await Authentication.SignInAnonymouslyAsync();

            await RefreshProfileAsync();
        }
        #endregion

        private async Task RefreshProfileAsync()
        {
            await _cloudSaveManager.FetchPlayerDataAsync();
            _cloudSaveManager.UpdateText();
        }

        private void SetSignedInWithUnity(bool signedIn)
        {
            _signOutButton.gameObject.SetActive(signedIn);
            _signInButton.gameObject.SetActive(!signedIn);
        }

        private void LoadGameScene()
        {
            if (_sfxManager != null) _sfxManager.StartGame();
            var loadData = new SceneLoadData(_gameSceneName) { ReplaceScenes = ReplaceOption.All };
            InstanceFinder.SceneManager.LoadGlobalScenes(loadData);
        }

        #region Lobby and relay
        private async Task<Lobby> QuickJoinLobbyAsync()
        {
            try
            {
                Lobby lobby = await Lobbies.Instance.QuickJoinLobbyAsync();
                JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(lobby.Data[JoinCodeKey].Value);

                _transport.SetClientRelayData(allocation.RelayServer.IpV4, (ushort)allocation.RelayServer.Port,
                                              allocation.AllocationIdBytes, allocation.Key,
                                              allocation.ConnectionData, allocation.HostConnectionData);

                InstanceFinder.ClientManager.StartConnection();
                return lobby;
            }
            catch (Exception e)
            {
                Debug.Log($"No lobby to quick join, creating one instead ({e.Message}).");
                return null;
            }
        }

        private async Task<Lobby> CreateLobbyAsync()
        {
            try
            {
                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(_maxPlayers);
                string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

                var options = new CreateLobbyOptions
                {
                    Data = new Dictionary<string, DataObject>
                    {
                        { JoinCodeKey, new DataObject(DataObject.VisibilityOptions.Public, joinCode) }
                    }
                };
                Lobby lobby = await Lobbies.Instance.CreateLobbyAsync(_lobbyName, _maxPlayers, options);
                StartCoroutine(HeartbeatLobbyRoutine(lobby.Id));

                _transport.SetHostRelayData(allocation.RelayServer.IpV4, (ushort)allocation.RelayServer.Port,
                                            allocation.AllocationIdBytes, allocation.Key, allocation.ConnectionData);

                InstanceFinder.ServerManager.StartConnection();
                if (!Application.isBatchMode) InstanceFinder.ClientManager.StartConnection();
                return lobby;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to create a lobby: {e.Message}");
                return null;
            }
        }

        /// <summary>Lobbies expire without a heartbeat from the host.</summary>
        private IEnumerator HeartbeatLobbyRoutine(string lobbyId)
        {
            var wait = new WaitForSecondsRealtime(_heartbeatInterval);
            while (true)
            {
                Lobbies.Instance.SendHeartbeatPingAsync(lobbyId).ContinueWith(task =>
                {
                    if (task.IsFaulted) Debug.LogWarning($"Lobby heartbeat failed: {task.Exception?.GetBaseException().Message}");
                });
                yield return wait;
            }
        }

        private void LeaveLobby()
        {
            if (_connectedLobby == null) return;

            try
            {
                if (_connectedLobby.HostId == _playerId) _ = Lobbies.Instance.DeleteLobbyAsync(_connectedLobby.Id);
                else _ = Lobbies.Instance.RemovePlayerAsync(_connectedLobby.Id, _playerId);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Error leaving lobby: {e.Message}");
            }

            _connectedLobby = null;
        }
        #endregion

        #region Editor quick start
        /// <summary>
        /// Offline session for development: no sign-in, lobby or relay. The main editor hosts on
        /// localhost; ParrelSync clones join it as clients.
        /// </summary>
        private void StartLocalSession()
        {
            Debug.Log("[Quick start] Skipping login, starting a local session.");
            _transport.SetConnectionData("127.0.0.1", _localPort);

            if (DevSettings.IsEditorClone)
            {
                InstanceFinder.ClientManager.StartConnection();
                return;
            }

            InstanceFinder.ServerManager.StartConnection();
            InstanceFinder.ClientManager.StartConnection();
            LoadGameScene();
        }
        #endregion
    }
}

using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Authentication.PlayerAccounts;
using Unity.Services.Core;
using UnityEngine;
using System;


#if UNITY_EDITOR
using ParrelSync;
#endif

namespace Unity.Services.Authentication.PlayerAccounts.Samples
{
    public static class Authentication
    {
        public static string playerId { get; private set; } = "";
        public static string playerTag { get; private set; } = "";
        public static bool signedIn = false;
        public static bool initPlayerAccountService;


        // "Enter Play Mode Options" has Domain Reload disabled in this project, so static
        // fields keep their values from the previous Play session. Reset them on every entry.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            playerId = "";
            playerTag = "";
            signedIn = false;
            initPlayerAccountService = false;
        }


        public static async Task InitializeUnityServices()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                var options = new InitializationOptions();

#if UNITY_EDITOR
                if (ClonesManager.IsClone()) options.SetProfile(ClonesManager.GetArgument());
                else options.SetProfile("Main");
#else
                options.SetProfile("Standalone");
#endif

                if (Application.isBatchMode) options.SetProfile("Headless");

                await UnityServices.InitializeAsync(options);
            }

            if (!initPlayerAccountService)
            {
                // PlayerAccountService only exists when Player Accounts is configured in
                // Project Settings > Services > Player Accounts. If it isn't, don't let that
                // block anonymous sign-in: log a warning and carry on without Unity sign-in.
                try
                {
                    PlayerAccountService.Instance.SignedIn -= SignInWithUnity;
                    PlayerAccountService.Instance.SignedIn += SignInWithUnity;
                    initPlayerAccountService = true;
                }
                catch (ServicesInitializationException e)
                {
                    Debug.LogWarning($"Player Accounts not available, Unity sign-in disabled: {e.Message}");
                }
            }
        }


        public static async Task StartUp()
        {
            try
            {
                initPlayerAccountService = false;
                await InitializeUnityServices();
                SetupEvents();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            
        }


        private static void SetupEvents()
        {
            // Unsubscribe first so handlers are never added twice (StartUp can run more than
            // once per editor session when Domain Reload is disabled).
            var auth = AuthenticationService.Instance;
            auth.SignedIn -= OnSignedIn;         auth.SignedIn += OnSignedIn;
            auth.SignInFailed -= OnSignInFailed; auth.SignInFailed += OnSignInFailed;
            auth.SignedOut -= OnSignedOut;       auth.SignedOut += OnSignedOut;
            auth.Expired -= OnExpired;           auth.Expired += OnExpired;
        }

        private static void OnSignedIn()
        {
            Debug.Log($"PlayerID: {AuthenticationService.Instance.PlayerId}");
            Debug.Log($"Access Token: {AuthenticationService.Instance.AccessToken}");
        }

        private static void OnSignInFailed(RequestFailedException err) => Debug.LogError(err);
        private static void OnSignedOut() => Debug.Log("Player signed out.");
        private static void OnExpired() => Debug.Log("Player session could not be refreshed and expired.");


        public static async Task LoginAnonymously()
        {
            await InitializeUnityServices();

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                playerId = AuthenticationService.Instance.PlayerId;
                Debug.Log(playerId);

                playerTag = await AuthenticationService.Instance.GetPlayerNameAsync();
                if (playerTag == null) await SetPlayerTag("Anonymous");
            }

            signedIn = true;
        }


        public static async Task Login()
        {
            await InitializeUnityServices();

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await PlayerAccountService.Instance.StartSignInAsync();

                while (!AuthenticationService.Instance.IsSignedIn)
                    await Task.Delay(25);

                playerTag = await AuthenticationService.Instance.GetPlayerNameAsync();
                if (playerTag == null) await SetPlayerTag("Anonymous");
            }

            signedIn = true;
        }


        public static async Task SetPlayerTag(string tag)
        {
            try
            {
                await AuthenticationService.Instance.UpdatePlayerNameAsync(tag);
                playerTag = await AuthenticationService.Instance.GetPlayerNameAsync();
            }
            catch (AuthenticationException ex)
            {
                Debug.LogException(ex);
            }
            catch (RequestFailedException ex)
            {
                Debug.LogException(ex);
            }
        }


        public static void SignOut()
        {
            if (PlayerAccountService.Instance.IsSignedIn)
                PlayerAccountService.Instance.SignOut();
            AuthenticationService.Instance.SignOut();
            AuthenticationService.Instance.ClearSessionToken();

            playerId = "";
            playerTag = null;
            signedIn = false;
        }


        public static async void SignInWithUnity()
        {
            try
            {
                Debug.Log(PlayerAccountService.Instance.AccessToken);
                await AuthenticationService.Instance.SignInWithUnityAsync(PlayerAccountService.Instance.AccessToken);
                Debug.Log("SignIn is successful.");
                playerId = AuthenticationService.Instance.PlayerId;
                Debug.Log(playerId);
            }
            catch (AuthenticationException ex)
            {
                Debug.LogException(ex);
            }
            catch (RequestFailedException ex)
            {
                Debug.LogException(ex);
            }
        }
    }
}
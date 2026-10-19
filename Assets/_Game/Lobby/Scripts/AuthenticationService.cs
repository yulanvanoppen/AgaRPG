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
                PlayerAccountService.Instance.SignedIn += SignInWithUnity;
                initPlayerAccountService = true;
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
            AuthenticationService.Instance.SignedIn += () => {
                // Shows how to get a playerID
                Debug.Log($"PlayerID: {AuthenticationService.Instance.PlayerId}");

                // Shows how to get an access token
                Debug.Log($"Access Token: {AuthenticationService.Instance.AccessToken}");

            };

            AuthenticationService.Instance.SignInFailed += (err) => {
                Debug.LogError(err);
            };

            AuthenticationService.Instance.SignedOut += () => {
                Debug.Log("Player signed out.");
            };

            AuthenticationService.Instance.Expired += () =>
            {
                Debug.Log("Player session could not be refreshed and expired.");
            };
        }


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
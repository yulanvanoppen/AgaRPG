using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Authentication.PlayerAccounts;
using Unity.Services.Core;
using UnityEngine;
#if UNITY_EDITOR
using ParrelSync;
#endif

namespace AgaRPG.Networking
{
    /// <summary>
    /// Wrapper around Unity Gaming Services sign-in: anonymous sign-in, "Sign in with Unity"
    /// (Player Accounts) and player names. Each ParrelSync clone uses its own UGS profile so
    /// several editor instances can be signed in as different players.
    /// </summary>
    public static class Authentication
    {
        public static string PlayerId { get; private set; } = string.Empty;
        public static string PlayerTag { get; private set; } = string.Empty;
        public static bool IsSignedIn { get; private set; }

        private static bool _playerAccountsHooked;

        // Domain reload may be disabled in "Enter Play Mode Options", in which case static fields
        // survive between play sessions. Reset them on every entry into play mode.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            PlayerId = string.Empty;
            PlayerTag = string.Empty;
            IsSignedIn = false;
            _playerAccountsHooked = false;
        }

        /// <summary>Initialises Unity Services and hooks up sign-in events. Safe to call repeatedly.</summary>
        public static async Task InitializeAsync()
        {
            try
            {
                await InitializeUnityServicesAsync();
                HookAuthenticationEvents();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>Signs in anonymously, unless already signed in.</summary>
        public static async Task SignInAnonymouslyAsync()
        {
            await InitializeUnityServicesAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                await OnSignedInAsync();
            }

            IsSignedIn = true;
        }

        /// <summary>Signs in through the Unity Player Accounts browser flow.</summary>
        public static async Task SignInWithUnityAsync()
        {
            await InitializeUnityServicesAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await PlayerAccountService.Instance.StartSignInAsync();

                // PlayerAccountService.SignedIn completes the UGS sign-in (see OnPlayerAccountSignedIn).
                while (!AuthenticationService.Instance.IsSignedIn) await Task.Delay(25);
                await OnSignedInAsync();
            }

            IsSignedIn = true;
        }

        public static void SignOut()
        {
            if (_playerAccountsHooked && PlayerAccountService.Instance.IsSignedIn) PlayerAccountService.Instance.SignOut();
            AuthenticationService.Instance.SignOut();
            AuthenticationService.Instance.ClearSessionToken();

            PlayerId = string.Empty;
            PlayerTag = string.Empty;
            IsSignedIn = false;
        }

        /// <summary>Changes the player's UGS player name.</summary>
        public static async Task SetPlayerTagAsync(string playerTag)
        {
            try
            {
                await AuthenticationService.Instance.UpdatePlayerNameAsync(playerTag);
                PlayerTag = await AuthenticationService.Instance.GetPlayerNameAsync();
            }
            catch (Exception e) when (e is AuthenticationException || e is RequestFailedException)
            {
                Debug.LogException(e);
            }
        }

        private static async Task InitializeUnityServicesAsync()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                var options = new InitializationOptions();
                options.SetProfile(GetProfileName());
                await UnityServices.InitializeAsync(options);
            }

            HookPlayerAccounts();
        }

        private static string GetProfileName()
        {
            if (Application.isBatchMode) return "Headless";
#if UNITY_EDITOR
            return ClonesManager.IsClone() ? ClonesManager.GetArgument() : "Main";
#else
            return "Standalone";
#endif
        }

        private static void HookPlayerAccounts()
        {
            if (_playerAccountsHooked) return;

            // Player Accounts only exists when configured in Project Settings > Services > Player
            // Accounts. Without it, anonymous sign-in should still work.
            try
            {
                PlayerAccountService.Instance.SignedIn -= OnPlayerAccountSignedIn;
                PlayerAccountService.Instance.SignedIn += OnPlayerAccountSignedIn;
                _playerAccountsHooked = true;
            }
            catch (ServicesInitializationException e)
            {
                Debug.LogWarning($"Player Accounts not available, Unity sign-in disabled: {e.Message}");
            }
        }

        private static void HookAuthenticationEvents()
        {
            // Unsubscribe first: AuthenticationService.Instance can outlive a play session when
            // domain reload is disabled, and handlers must not pile up.
            IAuthenticationService auth = AuthenticationService.Instance;
            auth.SignedIn -= LogSignedIn;
            auth.SignedIn += LogSignedIn;
            auth.SignInFailed -= LogSignInFailed;
            auth.SignInFailed += LogSignInFailed;
            auth.SignedOut -= LogSignedOut;
            auth.SignedOut += LogSignedOut;
            auth.Expired -= LogExpired;
            auth.Expired += LogExpired;
        }

        private static void LogSignedIn() => Debug.Log($"Signed in as {AuthenticationService.Instance.PlayerId}");
        private static void LogSignInFailed(RequestFailedException error) => Debug.LogError(error);
        private static void LogSignedOut() => Debug.Log("Signed out.");
        private static void LogExpired() => Debug.LogWarning("Player session could not be refreshed and expired.");

        private static async void OnPlayerAccountSignedIn()
        {
            try
            {
                await AuthenticationService.Instance.SignInWithUnityAsync(PlayerAccountService.Instance.AccessToken);
            }
            catch (Exception e) when (e is AuthenticationException || e is RequestFailedException)
            {
                Debug.LogException(e);
            }
        }

        private static async Task OnSignedInAsync()
        {
            PlayerId = AuthenticationService.Instance.PlayerId;
            PlayerTag = await AuthenticationService.Instance.GetPlayerNameAsync();
            if (string.IsNullOrEmpty(PlayerTag)) await SetPlayerTagAsync("Anonymous");
        }
    }
}

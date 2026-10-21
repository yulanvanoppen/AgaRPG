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
    /// (Player Accounts) and player names. Each ParrelSync clone, and each copy of a standalone
    /// build running on the same PC, uses its own UGS profile so they sign in as different players.
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
            if (TryGetCommandLineProfile(out string profile)) return profile;
            return GetInstanceProfile();
#endif
        }

#if !UNITY_EDITOR
        private const string ProfileArgument = "-profile";
        private const int MaxLocalInstances = 8;

        // Held for the lifetime of the process; marks which instance slot this copy of the game uses.
#pragma warning disable CS0414 // only kept alive, never read
        private static System.Threading.Mutex _instanceMutex;
#pragma warning restore CS0414

        /// <summary>
        /// <c>AgaRPG.exe -profile Alice</c> signs in with a fixed profile (letters, digits, '-' and
        /// '_', at most 30 characters).
        /// </summary>
        private static bool TryGetCommandLineProfile(out string profile)
        {
            profile = null;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], ProfileArgument, StringComparison.OrdinalIgnoreCase)) continue;
                if (!IsValidProfileName(args[i + 1])) break;
                profile = args[i + 1];
                return true;
            }
            return false;
        }

        /// <summary>
        /// Every copy of the game running on the same PC gets its own profile, and with it its own
        /// anonymous player, so testers can play against themselves. The first instance always
        /// uses "Standalone", so its Cloud Save data (size, colour) is kept between launches.
        /// </summary>
        private static string GetInstanceProfile()
        {
            for (int slot = 0; slot < MaxLocalInstances; slot++)
            {
                var mutex = new System.Threading.Mutex(true, $"AgaRPG.Instance.{slot}", out bool createdNew);
                if (createdNew)
                {
                    _instanceMutex = mutex;
                    return slot == 0 ? "Standalone" : $"Standalone{slot + 1}";
                }
                mutex.Dispose();
            }
            return "Standalone" + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        private static bool IsValidProfileName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 30) return false;
            foreach (char c in name)
                if (!char.IsLetterOrDigit(c) && c != '-' && c != '_') return false;
            return true;
        }
#endif

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

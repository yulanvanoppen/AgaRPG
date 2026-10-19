using System.Collections;
using AgaRPG.Networking;
using AgaRPG.UI;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.Serialization;

namespace AgaRPG.Player
{
    /// <summary>
    /// Server-authoritative player attributes: health, death/respawn, spawn protection, size
    /// (which grows with kills) and colour. All gameplay values are <see cref="SyncVar{T}"/>s
    /// written by the server only; clients react to their change callbacks.
    /// </summary>
    public class PlayerAttributes : NetworkBehaviour
    {
        [Tooltip("Maximum number of hitpoints")]
        [FormerlySerializedAs("maxHealth")]
        [SerializeField] private float _maxHealth = 100f;

        [Tooltip("Floating health bar above the player")]
        [FormerlySerializedAs("HealthBar")]
        [SerializeField] private FloatingHealthBar _healthBar;

        [SerializeField] private CloudSaveManager _cloudSaveManager;

        [Header("Respawn")]
        [SerializeField] private float _respawnDelay = 5f;
        [SerializeField] private float _spawnProtectionDuration = 5f;
        [SerializeField] private float _spawnProtectionBlinkInterval = 0.5f;

        [Header("Size")]
        [SerializeField] private float _minSize = 1f;
        [SerializeField] private float _maxSize = 2f;
        [SerializeField] private float _sizeGainPerKill = 0.05f;

        private const string MenuManagerTag = "MenuManager";
        private const string CloudSaveManagerTag = "CloudSaveManager";
        private static readonly Color SpawnProtectionColor = Color.gray;

        private readonly SyncVar<float> _health = new(100f);
        private readonly SyncVar<float> _size = new(1f);
        private readonly SyncVar<bool> _dead = new();
        private readonly SyncVar<bool> _spawnProtection = new();

        // The owner applies its colour locally and tells the server, which forwards it to everyone else.
        private readonly SyncVar<Color> _syncedColor = new(Color.white,
            new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.ExcludeOwner, 0.1f, Channel.Unreliable));

        private Color _ownerColor = Color.white;
        private Renderer _renderer;
        private PlayerAnimation _animation;
        private PauseMenu _pauseMenu;
        private Coroutine _blinkRoutine;

        public float Health => _health.Value;
        public float MaxHealth => _maxHealth;
        public bool IsDead => _dead.Value;
        public bool HasSpawnProtection => _spawnProtection.Value;

        /// <summary>Uniform scale of the player. Scales speed, jump height and damage.</summary>
        public float Size => _size.Value;

        private Color CurrentColor => base.IsOwner ? _ownerColor : _syncedColor.Value;

        #region Lifecycle
        private void Awake()
        {
            _health.OnChange += OnHealthChanged;
            _size.OnChange += OnSizeChanged;
            _dead.OnChange += OnDeadChanged;
            _spawnProtection.OnChange += OnSpawnProtectionChanged;
            _syncedColor.OnChange += OnSyncedColorChanged;

            _animation = GetComponent<PlayerAnimation>();
            _renderer = transform.Find("Graphical/Geometry/Armature_Mesh").GetComponent<Renderer>();

            if (_cloudSaveManager == null)
                _cloudSaveManager = GameObject.FindGameObjectWithTag(CloudSaveManagerTag).GetComponent<CloudSaveManager>();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _health.Value = _maxHealth;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!base.IsOwner) return;

            _pauseMenu = GameObject.FindGameObjectWithTag(MenuManagerTag).GetComponent<PauseMenu>();
            _pauseMenu.SetColor(_cloudSaveManager.PlayerColor);
            _pauseMenu.ColorChanged += SetOwnerColor;

            SetOwnerColor(_cloudSaveManager.PlayerColor);
            RequestScale(_cloudSaveManager.PlayerSize);
            ServerRpcStartSpawnProtection();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (_pauseMenu != null) _pauseMenu.ColorChanged -= SetOwnerColor;
        }
        #endregion

        #region Colour
        private void SetOwnerColor(Color color)
        {
            _ownerColor = color;
            ApplyColor(color);
            ServerRpcSetColor(color);
            _ = _cloudSaveManager.SaveColor(color);
        }

        [ServerRpc]
        private void ServerRpcSetColor(Color color) => _syncedColor.Value = color;

        private void OnSyncedColorChanged(Color previous, Color next, bool asServer)
        {
            if (!asServer && !HasSpawnProtection) ApplyColor(next);
        }

        private void ApplyColor(Color color)
        {
            foreach (Material material in _renderer.materials) material.color = color;
        }
        #endregion

        #region Size
        /// <summary>Requests a new size. Applied locally right away and made authoritative by the server.</summary>
        public void RequestScale(float value)
        {
            value = Mathf.Clamp(value, _minSize, _maxSize);
            transform.localScale = Vector3.one * value;
            if (Mathf.Approximately(_size.Value, value)) return;

            if (base.IsServerInitialized) _size.Value = value;
            else ServerRpcSetSize(value);
        }

        [ServerRpc]
        private void ServerRpcSetSize(float value) => _size.Value = Mathf.Clamp(value, _minSize, _maxSize);

        private void OnSizeChanged(float previous, float next, bool asServer)
        {
            transform.localScale = Vector3.one * next;
            if (!asServer && base.IsOwner) _ = _cloudSaveManager.SaveSize(next);
        }
        #endregion

        #region Health and death (server)
        /// <summary>Applies damage. Server only; ignored while spawn protected or dead.</summary>
        /// <param name="amount">Hitpoints to remove.</param>
        /// <param name="attacker">Connection of the attacking player, rewarded on a kill.</param>
        [Server]
        public void TakeDamage(float amount, NetworkConnection attacker = null)
        {
            if (HasSpawnProtection || IsDead) return;

            _health.Value = Mathf.Clamp(_health.Value - amount, 0f, _maxHealth);
            if (_health.Value > 0.001f) return;

            if (attacker != null && attacker != base.Owner && TryGetPlayer(attacker, out PlayerAttributes killer))
                killer.RequestScale(killer.Size + _sizeGainPerKill);

            StartCoroutine(DeathRoutine());
        }

        private IEnumerator DeathRoutine()
        {
            _dead.Value = true;
            _health.Value = 0f;

            yield return new WaitForSeconds(_respawnDelay);

            _health.Value = _maxHealth;
            transform.position = GameManager.GetRandomSpawnPosition();

            // Lose a quarter of the size gained, but at least one eighth.
            float size = Size;
            RequestScale(Mathf.Min(size - (size - 1f) / 4f, size - 0.125f));

            yield return new WaitForSeconds(0.1f);
            _dead.Value = false;
            StartCoroutine(SpawnProtectionRoutine());
        }

        [ServerRpc]
        private void ServerRpcStartSpawnProtection() => StartCoroutine(SpawnProtectionRoutine());

        private IEnumerator SpawnProtectionRoutine()
        {
            _spawnProtection.Value = true;
            yield return new WaitForSeconds(_spawnProtectionDuration);
            _spawnProtection.Value = false;
        }

        private static bool TryGetPlayer(NetworkConnection connection, out PlayerAttributes player)
        {
            player = null;
            if (connection.FirstObject != null && connection.FirstObject.TryGetComponent(out player)) return true;

            foreach (NetworkObject owned in connection.Objects)
                if (owned != null && owned.TryGetComponent(out player)) return true;
            return false;
        }
        #endregion

        #region Client reactions
        private void OnHealthChanged(float previous, float next, bool asServer) => _healthBar.UpdateHealthBar(next, _maxHealth);

        private void OnDeadChanged(bool previous, bool next, bool asServer)
        {
            if (asServer) return;   // animation is client-side only

            if (next)
            {
                _animation.SetRespawn(false);
                _animation.SetDeath();
            }
            else _animation.SetRespawn(true);
        }

        private void OnSpawnProtectionChanged(bool previous, bool next, bool asServer)
        {
            if (asServer) return;   // blinking is purely visual and runs locally on each client

            if (_blinkRoutine != null) StopCoroutine(_blinkRoutine);
            _blinkRoutine = next ? StartCoroutine(SpawnProtectionBlinkRoutine()) : null;
            if (!next) ApplyColor(CurrentColor);
        }

        private IEnumerator SpawnProtectionBlinkRoutine()
        {
            var wait = new WaitForSeconds(_spawnProtectionBlinkInterval);
            while (true)
            {
                ApplyColor(SpawnProtectionColor);
                yield return wait;
                ApplyColor(CurrentColor);
                yield return wait;
            }
        }
        #endregion
    }
}

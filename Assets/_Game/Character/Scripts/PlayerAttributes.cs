using UnityEngine;
using System.Threading.Tasks;
using FishNet.Object;
using System.Collections.Generic;
using System.Linq;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using System.Collections;
using FishNet.Connection;
using FishNet;

/* Note: animations are called via the controller for both the character and capsule using animator null checks
 */

public class PlayerAttributes : NetworkBehaviour
{
    [Tooltip("(Maximum) number of hitpoints")]
    public float maxHealth = 100f;

    // health: written by the server only
    private readonly SyncVar<float> _health = new(100f);
    public float health
    {
        get => _health.Value;
        set => _health.Value = value;      // server only
    }

    [Tooltip("Floating healthbar UI element")]
    public FloatingHealthBar HealthBar;

    public AudioClip DeathAudioClip;

    // managers
    [SerializeField] private CloudSaveManager _cloudSaveManager;
    private PauseMenu _menuManager;

    // animation
    private PlayerAnimation _animation;

    // controllers

    // player
    private Transform _mesh;

    // color: the owner keeps a local copy and sends it to the server (ServerRpcSetColor),
    // the server pushes it to everyone except the owner through the SyncVar
    private Color _color = Color.white;
    private readonly SyncVar<Color> _colorSync = new(
        Color.white,
        new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.ExcludeOwner, 0.1f, Channel.Unreliable));

    // server-written flags
    private readonly SyncVar<bool> _dead = new();
    public bool dead
    {
        get => _dead.Value;
        set => _dead.Value = value;        // server only
    }

    private readonly SyncVar<bool> _spawnProtection = new();
    public bool spawnProtection
    {
        get => _spawnProtection.Value;
        set => _spawnProtection.Value = value;   // server only
    }

    // size: read-only here, change it through Scale()
    private readonly SyncVar<float> _size = new(1f);
    public float size => _size.Value;


    private void OnHealth(float prev, float next, bool asServer)
    {
        HealthBar.UpdateHealthBar(next, maxHealth);
    }

    private void OnScaled(float prev, float next, bool asServer)
    {
        transform.localScale = Vector3.one * next;
        if (base.IsOwner)
        {
            Debug.Log($"Scaling to {next}");
            _ = _cloudSaveManager.UpdateSize(next);
        }
    }

    private void OnColored(Color prev, Color next, bool asServer)
    {
        if (!base.IsOwner) ChangeColor(next);
    }


    private async void Awake()
    {
        _health.OnChange += OnHealth;
        _size.OnChange += OnScaled;
        _colorSync.OnChange += OnColored;

        if (_cloudSaveManager == null)
            _cloudSaveManager = GameObject.FindGameObjectWithTag("CloudSaveManager")
                                          .GetComponent<CloudSaveManager>();

        _animation = GetComponent<PlayerAnimation>();
        _mesh = transform.Find("Graphical").transform.Find("Geometry")
                                           .transform.Find("Armature_Mesh");

        if (_cloudSaveManager.name == "TestCSManager")
        {
            Debug.Log("----------------- TESTING -----------------\n----------------- TESTING -----------------\n----------------- TESTING -----------------");
            InstanceFinder.ServerManager.StartConnection();
            InstanceFinder.ClientManager.StartConnection();
            await Task.Delay(1000);
            GetComponent<NetworkObject>().GiveOwnership(InstanceFinder.ClientManager.Connection);
            Initialize();
        }
    }


    public override void OnStartServer()
    {
        base.OnStartServer();
        _health.Value = maxHealth;
    }


    public override void OnStartClient()
    {
        base.OnStartClient();
        Initialize();
    }


    private void Initialize()
    {
        _menuManager = GameObject.FindGameObjectWithTag("MenuManager").GetComponent<PauseMenu>();
        _menuManager.color = _cloudSaveManager.playerColor;

        if (base.IsOwner)
        {
            _color = _cloudSaveManager.playerColor;
            ChangeColor(_color);
            ServerRpcSetColor(_color);
            Scale(_cloudSaveManager.playerSize);
            RpcSpawnWithProtection();
        }
    }


    private void Update()
    {
        if (!base.IsOwner) return;

        CheckColorChanged();
    }


    public void Scale(float value)
    {
        value = Mathf.Clamp(value, 1f, 2f);
        transform.localScale = Vector3.one * value;     // apply locally right away
        if (_size.Value == value) return;

        if (base.IsServer) _size.Value = value;
        else ServerRpcSetSize(value);
    }


    [ServerRpc(RequireOwnership = false)]
    private void ServerRpcSetSize(float value) => _size.Value = Mathf.Clamp(value, 1f, 2f);


    [ServerRpc]
    private void ServerRpcSetColor(Color color)
    {
        _color = color;              // server's copy, used by SpawnProtectionCoroutine
        _colorSync.Value = color;    // pushes to non-owner clients
    }


    public void CheckColorChanged()
    {
        if (!_menuManager.color.Equals(_color))
            UpdateColor();
    }


    public async void UpdateColor()
    {
        _color = _menuManager.color;
        ChangeColor(_color);
        ServerRpcSetColor(_color);
        if (!_cloudSaveManager.playerColor.Equals(_color))
            await _cloudSaveManager.UpdateColor(_color);
    }


    public void ChangeColor(Color color)
    {
        List<Material> materials = _mesh.GetComponent<Renderer>().materials.ToList();
        foreach (Material material in materials) material.color = color;
    }


    private void UpdateHealth(float value)
    {
        health = Mathf.Clamp(value, 0f, maxHealth);
        HealthBar.UpdateHealthBar(health, maxHealth);
    }


    public void TakeDamage(float damageAmount, NetworkConnection attackerConnection = null)
    {
        if (!base.IsServer || spawnProtection) return;

        UpdateHealth(health - damageAmount);
        if (health < 0.001f)
        {
            if (attackerConnection != null & !dead)
            {
                PlayerAttributes otherPlayer = attackerConnection.FirstObject.GetComponent<PlayerAttributes>();
                otherPlayer.Scale(otherPlayer.size + .05f);
            }

            Die();
        }
    }


    private void Die()
    {
        if (!base.IsServer) return;

        dead = true;
        UpdateHealth(0f);
        ObserversRpcAnimationSetDeath();

        StartCoroutine(DeathCoroutine());
    }


    [ObserversRpc]
    private void ObserversRpcAnimationSetDeath()
    {
        _animation.SetRespawn(false);
        _animation.SetDeath();
    }

    [ObserversRpc]
    private void ObserversRpcAnimationSetRespawn()
    {
        _animation.SetRespawn(true);
    }


    IEnumerator DeathCoroutine()
    {
        yield return new WaitForSeconds(5);

        UpdateHealth(maxHealth);
        ObserversRpcAnimationSetRespawn();

        transform.position = new Vector3(Random.value * 4f - 2f, 0f, Random.value * 4f - 2f);
        float newSize = Mathf.Min(size - (size - 1f) / 4f, size - 0.125f);
        Scale(newSize);

        yield return new WaitForSeconds(.1f);

        dead = false;

        StartCoroutine(SpawnProtectionCoroutine());
    }

    [ServerRpc]
    private void RpcSpawnWithProtection() => StartCoroutine(SpawnProtectionCoroutine());

    IEnumerator SpawnProtectionCoroutine()
    {
        spawnProtection = true;

        float time = 0f;
        while (time < 5f)
        {
            ObserversRpcChangeColor(Color.gray);
            time += .5f;
            yield return new WaitForSeconds(.5f);
            ObserversRpcChangeColor(_color);
            time += .5f;
            yield return new WaitForSeconds(.5f);
        }

        spawnProtection = false;
    }

    [ObserversRpc]
    private void ObserversRpcChangeColor(Color color) => ChangeColor(color);
}

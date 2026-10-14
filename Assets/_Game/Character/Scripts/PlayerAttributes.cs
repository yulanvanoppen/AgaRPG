using UnityEngine;
using System.Threading.Tasks;
using FishNet.Object;
using System.Collections.Generic;
using System.Linq;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using System.Collections;
using FishNet.Connection;
using Unity.VisualScripting;
using FishNet;
using UnityEngine.Rendering.Universal;
using static UnityEngine.Rendering.DebugUI;

/* Note: animations are called via the controller for both the character and capsule using animator null checks
 */

public class PlayerAttributes : NetworkBehaviour
{
    [Tooltip("(Maximum) number of hitpoints")]
    public float maxHealth = 100f;

    [field: SyncVar(Channel = Channel.Reliable, OnChange = nameof(OnHealth))]
    public float health = 100f;

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

    [field: SyncVar(Channel = Channel.Unreliable, ReadPermissions = ReadPermission.ExcludeOwner, OnChange = nameof(OnColored))]
    private Color _color { get; [ServerRpc(RunLocally = true)] set; } = Color.white;

    [field: SyncVar(Channel = Channel.Reliable)]
    [HideInInspector] public bool dead = false;

    [field: SyncVar(Channel = Channel.Reliable, OnChange = nameof(OnScaled))]
    [HideInInspector] public float size { get; [ServerRpc(RequireOwnership = false, RunLocally = true)] set; } = 1f;

    [field: SyncVar(Channel = Channel.Reliable)]
    [HideInInspector] public bool spawnProtection = false;


    private void OnHealth(float prev, float next, bool asServer)
    {
        HealthBar.UpdateHealthBar(next, maxHealth);
    }

    private void OnScaled(float prev, float next, bool asServer)
    {
        transform.localScale = Vector3.one * next;
        if (base.IsOwner) 
        {
            Debug.Log($"Scaling to {size}");
            _ = _cloudSaveManager.UpdateSize(size);
        }
    }

    private void OnColored(Color prev, Color next, bool asServer)
    {
        if (!base.IsOwner) ChangeColor(next);
    }


    private async void Awake()
    {
        if (_cloudSaveManager == null)
            _cloudSaveManager = GameObject.FindGameObjectWithTag("CloudSaveManager")
                                          .GetComponent<CloudSaveManager>();

        _animation = GetComponent<PlayerAnimation>();
        _mesh = transform.Find("Graphical").transform.Find("Geometry")
                                           .transform.Find("Armature_Mesh");

        health = maxHealth;

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
            size = _cloudSaveManager.playerSize;
            Scale(size);
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
        if (size != value)
        {
            size = value;
            if (base.IsOwner)
            {
                Debug.Log($"Scaling to {size}");
                _ = _cloudSaveManager.UpdateSize(size);
            }
                
        }
        transform.localScale = Vector3.one * size;
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
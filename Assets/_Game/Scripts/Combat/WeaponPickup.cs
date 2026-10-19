using UnityEngine;
using UnityEngine.Serialization;

namespace AgaRPG.Combat
{
    /// <summary>
    /// Floating weapon pickup placed in the scene (not networked). Every machine animates it and
    /// shows the cooldown locally, but only the server acts on a pickup: it equips the weapon
    /// through <see cref="PlayerWeapons.ServerEquip"/>. A pickup without a weapon prefab
    /// unequips the player.
    /// </summary>
    public class WeaponPickup : MonoBehaviour
    {
        [Tooltip("Weapon to equip; leave empty to make this an 'unequip' pad")]
        [SerializeField] private AbstractWeapon _weaponPrefab;

        [FormerlySerializedAs("floatSpeed")]
        [SerializeField] private float _floatSpeed = 1f;

        [SerializeField] private float _bobHeight = 0.04f;
        [SerializeField] private float _cooldownDuration = 1f;

        private static readonly Color ReadyColor = new Color(1f, 1f, 1f, 0.05f);
        private static readonly Color CooldownColor = new Color(1f, 0f, 0f, 0.05f);

        private float _baseHeight;
        private float _cooldown;
        private Material _material;

        private bool IsReady => _cooldown <= 0f;

        private void Awake() => _material = GetComponent<Renderer>().material;

        private void Start() => _baseHeight = transform.position.y;

        private void Update()
        {
            Vector3 position = transform.position;
            position.y = _baseHeight + Mathf.Sin(_floatSpeed * Time.time) * _bobHeight;
            transform.position = position;
            transform.Rotate(0f, _floatSpeed * 5f * Time.deltaTime, 0f);

            if (_cooldown > 0f) _cooldown -= Time.deltaTime;
            _material.color = IsReady ? ReadyColor : CooldownColor;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsReady || !other.TryGetComponent(out PlayerWeapons player)) return;

            _cooldown = _cooldownDuration;
            if (player.IsServerInitialized) player.ServerEquip(_weaponPrefab);
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}

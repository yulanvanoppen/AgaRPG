using FishNet;
using FishNet.Object;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class WeaponPickup : MonoBehaviour
{
    [SerializeField] private AbstractWeapon _weaponPrefab;

    private float _originalY;
    public float floatSpeed;

    private bool active = true;
    private float cooldown = 0f;
    private Material _material;

    private void Awake() => _material = GetComponent<Renderer>().material;

    private void Start() => _originalY = transform.position.y;

    private void Update()
    {
        transform.position = new Vector3(transform.position.x,
                                         _originalY + (Mathf.Sin(floatSpeed * Time.time) * .04f),
                                         transform.position.z);
        transform.Rotate(0f, floatSpeed * 5f * Time.deltaTime, 0f);

        if (cooldown > 0) cooldown -= Time.deltaTime;
        else active = true;

        _material.color = (active ? Color.white : Color.red) - new Color(0f, 0f, 0f, .95f);
    }

    private async void OnTriggerEnter(Collider other)
    {
        if (!active) return;

        PlayerWeapons player = other.gameObject.GetComponent<PlayerWeapons>();
        if (player != null && player.IsClient && player.IsOwner && active)
        {
            player.RpcUnequip();
            if (_weaponPrefab != null)
            {
                //player.RequestWeapon(_weaponPrefab);
                Debug.Log($"SPAWNING WEAPON FOR {player.Owner}");
                AbstractWeapon newWeapon = Instantiate(_weaponPrefab);
                InstanceFinder.ServerManager.Spawn(newWeapon.gameObject, player.Owner);

                active = false;
                cooldown = 1f;
                await Task.Delay(50);
                player.RpcEquip(newWeapon);
            }
        }

        cooldown = 1f;
    }
}




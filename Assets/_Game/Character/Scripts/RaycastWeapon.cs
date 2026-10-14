using FishNet.Component.Animating;
using FishNet.Object;
using UnityEngine;

public class RaycastWeapon : AbstractWeapon
{
    public int fireRate = 7;
    public bool automatic;
    public ParticleSystem[] muzzleFlash;
    public ParticleSystem hitEffect;
    public TrailRenderer tracerEffect;
    public Transform raycastOrigin;
    public AudioClip shootAudioClip;
    public AudioClip reloadAudioClip;

    private bool _isFiring = false;
    [HideInInspector] public float accumulatedTime;

    private int _animIDShoot;
    public AnimationClip handPositionClip;
    [SerializeField] private Animator _animator;

    public WeaponRecoil weaponRecoil;
    [SerializeField] private Transform _weaponClip;

    private int _arrowIndex = 0;
    public float offset = 0.00975f;
    public int clipSize;


    public void Initialize()
    {
        _animIDShoot = Animator.StringToHash("Shoot");
        weaponRecoil.Initialize();
    }


    public void RecoverTime(float delta)
    {
        if (!_isFiring && accumulatedTime < 0) accumulatedTime += delta;

        if (accumulatedTime > -.1f && _arrowIndex == -1) MoveClip();
    }


    public void UpdateFiring(RaycastHit raycastHit, float size, float delta)
    {
        _isFiring = true;
        accumulatedTime += delta;
        float fireInterval = 1f / fireRate;
        while(accumulatedTime >= 0f && _arrowIndex >= 0)
        {
            FireArrow(raycastHit, size);
            accumulatedTime -= fireInterval;
        }

        if (_arrowIndex >= clipSize)
        {
            accumulatedTime = -1f;
            _arrowIndex = -1;
            weaponRecoil.ResetPattern();
        }

    }


    private void MoveClip()
    {
        //Debug.Log("MOVING CLIP");
        _arrowIndex += 1;
        int multiplier = _arrowIndex == 0 ? -clipSize : _arrowIndex > 0 ? 1 : 0;
        _weaponClip.position += multiplier * offset * _weaponClip.up;

        if (_arrowIndex == 0) ReloadAudio();
    }


    private void FireArrow(RaycastHit raycastHit, float size)
    {
        //Debug.Log($"ANIMATOR {_networkAnimator}, ID {_animIDShoot}, RECOIL {_weaponRecoil}");
        ShotAudio();
        //_networkAnimator.SetTrigger(_animIDShoot);
        _animator.SetTrigger(_animIDShoot);
        weaponRecoil.Recoil();
        MoveClip();



        //Debug.Log($"OWNER {IsServer} {IsOwner}");

        foreach (var particle in muzzleFlash)
            particle.Emit(1);

        var tracer = Instantiate(tracerEffect, raycastOrigin.position, Quaternion.identity);
        base.Spawn(tracer.gameObject, base.Owner);
        tracer.AddPosition(raycastOrigin.position);

        if (raycastHit.distance > 0f)
        {
            ShotImpact(raycastHit.point, raycastHit.normal);

            tracer.transform.position = raycastHit.point;
        }

        if (base.IsServer && accumulatedTime >= 0f)
        {
            Transform hitTransform = raycastHit.transform;

            if (hitTransform != null && hitTransform.root.CompareTag("Player"))
            {
                PlayerAttributes otherPlayer = hitTransform.root.GetComponent<PlayerAttributes>();
                float damageAmount = baseDamage * size;
                if (hitTransform.gameObject.layer == LayerMask.NameToLayer("PlayerHead")) damageAmount *= 4f;

                otherPlayer.TakeDamage(damageAmount / otherPlayer.size, base.Owner);
            }
        }
    }


    public void StopFiring() => _isFiring = false;


    private void HitEffect(Vector3 position, Vector3 normal)
    {
        hitEffect.transform.position = position;
        hitEffect.transform.forward = normal;
        hitEffect.Emit(1);
    }


    private void ShotImpact(Vector3 position, Vector3 normal)
    {
        if (base.IsOwner) HitEffect(position, normal);
        if (base.IsServer) ObserversRpcShotImpact(position, normal);
    }

    [ObserversRpc(ExcludeOwner = true)]
    private void ObserversRpcShotImpact(Vector3 position, Vector3 normal) => HitEffect(position, normal);


    private void ShotAudio ()
    {
        if (base.IsOwner) AudioSource.PlayClipAtPoint(shootAudioClip, raycastOrigin.position, .3f);
        if (base.IsServer) ObserversRpcShotAudio();
    }

    [ObserversRpc(ExcludeOwner = true)]
    private void ObserversRpcShotAudio() => AudioSource.PlayClipAtPoint(shootAudioClip, raycastOrigin.position, .3f);


    private void ReloadAudio()
    {
        if (base.IsOwner) AudioSource.PlayClipAtPoint(reloadAudioClip, raycastOrigin.position, .6f);
        if (base.IsServer) ObserversRpcReloadAudio();
    }

    [ObserversRpc(ExcludeOwner = true)]
    private void ObserversRpcReloadAudio() => AudioSource.PlayClipAtPoint(reloadAudioClip, raycastOrigin.position, .6f);
}

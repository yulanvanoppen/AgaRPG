using AgaRPG.Player;
using FishNet.Object;
using UnityEngine;
using UnityEngine.Serialization;

namespace AgaRPG.Combat
{
    /// <summary>
    /// Hitscan weapon (the crossbows). Firing runs inside the predicted combat tick on every
    /// machine that simulates the shooter; the server applies damage and relays cosmetic shot
    /// effects to the other clients with a single RPC per shot.
    /// </summary>
    public class RaycastWeapon : AbstractWeapon
    {
        [Header("Firing")]
        [Tooltip("Shots per second")]
        [FormerlySerializedAs("fireRate")]
        [SerializeField] private int _fireRate = 7;

        [Tooltip("Keep firing while the trigger is held")]
        [FormerlySerializedAs("automatic")]
        [SerializeField] private bool _automatic;

        [Tooltip("Arrows per clip before reloading")]
        [FormerlySerializedAs("clipSize")]
        [SerializeField] private int _clipSize;

        [Tooltip("Reload time in seconds")]
        [SerializeField] private float _reloadTime = 1f;

        [Tooltip("Where shots and tracers originate")]
        [FormerlySerializedAs("raycastOrigin")]
        [SerializeField] private Transform _raycastOrigin;

        [Header("Effects")]
        [FormerlySerializedAs("muzzleFlash")]
        [SerializeField] private ParticleSystem[] _muzzleFlash;

        [FormerlySerializedAs("hitEffect")]
        [SerializeField] private ParticleSystem _hitEffect;

        [Tooltip("Purely cosmetic tracer; instantiated locally, never network spawned")]
        [FormerlySerializedAs("tracerEffect")]
        [SerializeField] private TrailRenderer _tracerEffect;

        [SerializeField] private float _tracerLifetime = 0.5f;
        [SerializeField] private float _missTracerLength = 100f;

        [FormerlySerializedAs("shootAudioClip")]
        [SerializeField] private AudioClip _shootAudioClip;

        [FormerlySerializedAs("reloadAudioClip")]
        [SerializeField] private AudioClip _reloadAudioClip;

        [Header("Animation")]
        [Tooltip("Hand pose clip applied to the player's grip override")]
        [FormerlySerializedAs("handPositionClip")]
        [SerializeField] private AnimationClip _handPositionClip;

        [SerializeField] private Animator _animator;

        [FormerlySerializedAs("weaponRecoil")]
        [SerializeField] private WeaponRecoil _weaponRecoil;

        [Header("Visible clip")]
        [Tooltip("Arrow clip mesh that slides as arrows are fired")]
        [SerializeField] private Transform _weaponClip;

        [Tooltip("Distance the clip moves per arrow")]
        [FormerlySerializedAs("offset")]
        [SerializeField] private float _clipOffset = 0.00975f;

        private static readonly int ShootId = Animator.StringToHash("Shoot");

        /// <summary>Time budget for firing: negative while reloading or between shots.</summary>
        private float _accumulatedTime;

        /// <summary>Index of the next arrow, or -1 while the clip is empty and reloading.</summary>
        private int _arrowIndex;
        private bool _isFiring;

        public bool IsAutomatic => _automatic;
        public AnimationClip HandPositionClip => _handPositionClip;
        public WeaponRecoil Recoil => _weaponRecoil;

        public void Initialize() => _weaponRecoil.Initialize();

        /// <summary>Advances the reload / fire-rate timer while not firing.</summary>
        public void RecoverTime(float delta)
        {
            if (!_isFiring && _accumulatedTime < 0f) _accumulatedTime += delta;

            // Slide the full clip back in shortly before the reload finishes.
            if (_accumulatedTime > -0.1f && _arrowIndex == -1) AdvanceClip();
        }

        /// <summary>Fires as many arrows as the fire rate allows during this tick.</summary>
        /// <param name="hit">Result of the aim raycast (distance 0 when nothing was hit).</param>
        /// <param name="shooterSize">Size of the shooter, scales damage.</param>
        /// <param name="delta">Tick duration.</param>
        public void UpdateFiring(RaycastHit hit, float shooterSize, float delta)
        {
            _isFiring = true;
            _accumulatedTime += delta;

            float fireInterval = 1f / _fireRate;
            while (_accumulatedTime >= 0f && _arrowIndex >= 0)
            {
                FireArrow(hit, shooterSize);
                _accumulatedTime -= fireInterval;
            }

            if (_arrowIndex >= _clipSize)
            {
                _accumulatedTime = -_reloadTime;
                _arrowIndex = -1;
                _weaponRecoil.ResetPattern();
            }
        }

        public void StopFiring() => _isFiring = false;

        private void FireArrow(RaycastHit hit, float shooterSize)
        {
            _animator.SetTrigger(ShootId);
            _weaponRecoil.Recoil();
            AdvanceClip();
            foreach (ParticleSystem particle in _muzzleFlash) particle.Emit(1);

            bool hasHit = hit.distance > 0f;
            Vector3 start = _raycastOrigin.position;
            Vector3 end = hasHit ? hit.point : start + _raycastOrigin.forward * _missTracerLength;

            // Cosmetics: the owner sees them immediately, the server relays them to everyone else.
            if (base.IsOwner) PlayShotEffects(start, end, hit.normal, hasHit);
            if (base.IsServerInitialized)
            {
                ObserversRpcShotFired(start, end, hit.normal, hasHit);
                ApplyDamage(hit, shooterSize);
            }
        }

        private void ApplyDamage(RaycastHit hit, float shooterSize)
        {
            Transform hitTransform = hit.transform;
            if (hitTransform == null || !hitTransform.root.CompareTag("Player")) return;
            if (!hitTransform.root.TryGetComponent(out PlayerAttributes target)) return;

            float damage = BaseDamage * shooterSize;
            if (hitTransform.gameObject.layer == LayerMask.NameToLayer("PlayerHead")) damage *= HeadshotMultiplier;
            target.TakeDamage(damage / target.Size, base.Owner);
        }

        [ObserversRpc(ExcludeOwner = true)]
        private void ObserversRpcShotFired(Vector3 start, Vector3 end, Vector3 normal, bool hasHit) =>
            PlayShotEffects(start, end, normal, hasHit);

        private void PlayShotEffects(Vector3 start, Vector3 end, Vector3 normal, bool hasHit)
        {
            AudioSource.PlayClipAtPoint(_shootAudioClip, start, 0.3f);

            TrailRenderer tracer = Instantiate(_tracerEffect, start, Quaternion.identity);
            tracer.AddPosition(start);
            tracer.transform.position = end;
            Destroy(tracer.gameObject, _tracerLifetime);

            if (!hasHit) return;
            _hitEffect.transform.SetPositionAndRotation(end, Quaternion.LookRotation(normal));
            _hitEffect.Emit(1);
        }

        /// <summary>Moves the visible arrow clip by one arrow, or resets it after a reload.</summary>
        private void AdvanceClip()
        {
            _arrowIndex++;
            int multiplier = _arrowIndex == 0 ? -_clipSize : _arrowIndex > 0 ? 1 : 0;
            _weaponClip.position += multiplier * _clipOffset * _weaponClip.up;

            if (_arrowIndex == 0) PlayReloadSound();
        }

        private void PlayReloadSound()
        {
            if (base.IsOwner) PlayReloadClip();
            if (base.IsServerInitialized) ObserversRpcReload();
        }

        [ObserversRpc(ExcludeOwner = true)]
        private void ObserversRpcReload() => PlayReloadClip();

        private void PlayReloadClip() => AudioSource.PlayClipAtPoint(_reloadAudioClip, _raycastOrigin.position, 0.6f);
    }
}

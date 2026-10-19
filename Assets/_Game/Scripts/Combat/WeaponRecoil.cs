using AgaRPG.Player;
using FishNet.Object;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Serialization;

namespace AgaRPG.Combat
{
    /// <summary>
    /// Recoil of a ranged weapon: plays the recoil animation for everyone and, for the owner,
    /// kicks the camera along a fixed pattern (scaled by movement), shakes it and pushes the
    /// aim camera back briefly.
    /// </summary>
    public class WeaponRecoil : NetworkBehaviour
    {
        [SerializeField] private float _recoilIntensity = 1f;
        [SerializeField] private float _recoilNoise = 0.25f;
        [SerializeField] private float _recoilDuration = 0.05f;

        [Tooltip("Per-shot recoil: x = vertical, y = horizontal. Cycles while firing.")]
        [SerializeField] private Vector2[] _recoilPattern;

        [Tooltip("Extra aim camera distance per shot")]
        [FormerlySerializedAs("kickback")]
        [SerializeField] private float _kickback = 0.1f;

        [Tooltip("How fast the aim camera returns after a kick")]
        [FormerlySerializedAs("recoverySpeed")]
        [SerializeField] private float _recoverySpeed = 10f;

        private const string AimCameraTag = "PlayerAimCamera";
        private const float AimCameraDistance = 2f;
        private const float VerticalPatternBias = 0.75f;
        private const float AirborneRecoilMultiplier = 2.5f;
        private const float MovingRecoilMultiplier = 1.5f;

        private static readonly int RecoilId = Animator.StringToHash("Recoil");

        private float _horizontalRecoil;
        private float _verticalRecoil;
        private float _horizontalMean;
        private float _remainingTime;
        private int _patternIndex;
        private bool _initialized;

        private CinemachineImpulseSource _impulseSource;
        private CinemachineThirdPersonFollow _aimCameraFollow;
        private PlayerMovement _playerMovement;
        private PlayerCameraControl _cameraControl;
        private InputController _input;

        /// <summary>The wielder's rig animator; assigned when the weapon is equipped.</summary>
        public Animator RigController { get; set; }

        // Looked up lazily: on a host the weapon is equipped server-side before its client side is
        // initialised, so IsOwner is still false during Initialize().
        private CinemachineThirdPersonFollow AimCameraFollow
        {
            get
            {
                if (_aimCameraFollow == null)
                {
                    GameObject aimCamera = GameObject.FindGameObjectWithTag(AimCameraTag);
                    if (aimCamera != null) _aimCameraFollow = aimCamera.GetComponent<CinemachineThirdPersonFollow>();
                }
                return _aimCameraFollow;
            }
        }

        /// <summary>Binds the recoil to the player that currently holds the weapon.</summary>
        public void Initialize()
        {
            Transform player = transform.root;
            _impulseSource = GetComponentInChildren<CinemachineImpulseSource>();
            _playerMovement = player.GetComponent<PlayerMovement>();
            _cameraControl = player.GetComponent<PlayerCameraControl>();
            _input = player.GetComponent<InputController>();

            _horizontalMean = 0f;
            foreach (Vector2 kick in _recoilPattern) _horizontalMean += kick.y;
            if (_recoilPattern.Length > 0) _horizontalMean /= _recoilPattern.Length;

            _initialized = true;
        }

        public void Recoil()
        {
            if (RigController != null) RigController.SetTrigger(RecoilId);
            if (!base.IsOwner || !_initialized) return;

            if (AimCameraFollow != null) AimCameraFollow.CameraDistance = AimCameraDistance + _kickback;
            _impulseSource.GenerateImpulse(_cameraControl.CameraForward);
            NextPatternStep();
        }

        public void ResetPattern() => _patternIndex = 0;

        private void NextPatternStep()
        {
            if (_recoilPattern.Length == 0) return;

            _remainingTime = _recoilDuration;
            _verticalRecoil = _recoilPattern[_patternIndex].x - VerticalPatternBias;
            _horizontalRecoil = _recoilPattern[_patternIndex].y - _horizontalMean;
            _patternIndex = (_patternIndex + 1) % _recoilPattern.Length;
        }

        private void LateUpdate()
        {
            if (!_initialized || !base.IsOwner) return;

            if (!_input.Shoot) ResetPattern();

            if (_remainingTime > 0f)
            {
                // More recoil while moving or airborne.
                float multiplier = !_playerMovement.IsGrounded
                    ? AirborneRecoilMultiplier
                    : 1f + MovingRecoilMultiplier * _playerMovement.CurrentSpeed / _playerMovement.WalkSpeed;
                float amplitude = (_recoilIntensity + Random.Range(-_recoilNoise, _recoilNoise)) * multiplier;
                float step = amplitude * Time.deltaTime / _recoilDuration;

                _cameraControl.AddRotationOffset(-step * _verticalRecoil, -step * _horizontalRecoil);
                _remainingTime -= Time.deltaTime;
            }

            CinemachineThirdPersonFollow follow = AimCameraFollow;
            if (follow != null && follow.CameraDistance > AimCameraDistance)
                follow.CameraDistance = Mathf.Lerp(follow.CameraDistance, AimCameraDistance, _recoverySpeed * Time.deltaTime);
        }
    }
}

using AgaRPG.Player;
using FishNet.Object;
using UnityEngine;
using UnityEngine.Serialization;

namespace AgaRPG.UI
{
    /// <summary>Dynamic crosshair for the owning player: spreads while moving or looking, tightens when aiming.</summary>
    public class ReticleControl : NetworkBehaviour
    {
        [FormerlySerializedAs("aimSize")]
        [SerializeField] private float _aimSize;

        [FormerlySerializedAs("restingSize")]
        [SerializeField] private float _restingSize;

        [FormerlySerializedAs("lookSize")]
        [SerializeField] private float _lookSize;

        [FormerlySerializedAs("maxSize")]
        [SerializeField] private float _maxSize;

        [FormerlySerializedAs("speed")]
        [SerializeField] private float _speed;

        private const string ReticleObjectName = "Reticle";
        private const float MovingVelocitySqrThreshold = 1.9f;

        private RectTransform _reticle;
        private CanvasGroup _reticleGroup;
        private CharacterController _characterController;
        private PlayerState _state;
        private PlayerAttributes _attributes;
        private InputController _input;
        private float _currentSize;

        private bool IsMoving => _input.Move != Vector2.zero || _characterController.velocity.sqrMagnitude > MovingVelocitySqrThreshold;
        private bool IsLooking => _input.Look != Vector2.zero;
        private bool IsAiming => _input.Aim;

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _state = GetComponent<PlayerState>();
            _attributes = GetComponent<PlayerAttributes>();
            _input = GetComponent<InputController>();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!base.IsOwner) return;

            GameObject reticle = GameObject.Find(ReticleObjectName);
            _reticle = reticle.GetComponent<RectTransform>();
            _reticleGroup = reticle.GetComponent<CanvasGroup>();
        }

        private void Update()
        {
            if (!base.IsOwner || _reticle == null) return;

            _reticleGroup.alpha = _attributes.IsDead ? 0f : 1f;

            float targetSize;
            if ((IsMoving && IsAiming) || !_state.Is(MeleeState.Inactive)) targetSize = _restingSize;
            else if (IsMoving) targetSize = _maxSize;
            else if (IsAiming) targetSize = _aimSize;
            else if (IsLooking) targetSize = _lookSize;
            else targetSize = _restingSize;

            _currentSize = Mathf.Lerp(_currentSize, targetSize, Time.deltaTime * _speed);
            _reticle.sizeDelta = new Vector2(_currentSize, _currentSize);
        }
    }
}

using AgaRPG.Combat;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.Serialization;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AgaRPG.Player
{
    /// <summary>
    /// Client-side predicted, server-authoritative character movement built on FishNet's
    /// replicate/reconcile prediction. The owner sends <see cref="MoveData"/> every tick; the
    /// server runs the same simulation and sends back <see cref="ReconcileData"/> so the owner
    /// can correct mispredictions.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM
    [RequireComponent(typeof(PlayerInput))]
#endif
    public class PlayerMovement : NetworkBehaviour
    {
        #region Inspector
        [Header("Player")]
        [Tooltip("Move speed of the character in m/s")]
        [FormerlySerializedAs("MoveSpeed")]
        [SerializeField] private float _moveSpeed = 2f;

        [Tooltip("Sprint speed of the character in m/s")]
        [FormerlySerializedAs("SprintSpeed")]
        [SerializeField] private float _sprintSpeed = 5.335f;

        [Tooltip("How fast the character turns to face the movement direction")]
        [FormerlySerializedAs("RotationSmoothTime")]
        [Range(0f, 0.3f)]
        [SerializeField] private float _rotationSmoothTime = 0.12f;

        [Tooltip("Acceleration and deceleration")]
        [FormerlySerializedAs("SpeedChangeRate")]
        [SerializeField] private float _speedChangeRate = 10f;

        [Space(10)]
        [Tooltip("The height the player can jump")]
        [FormerlySerializedAs("JumpHeight")]
        [SerializeField] private float _jumpHeight = 1.2f;

        [Tooltip("The character uses its own gravity value. The engine default is -9.81")]
        [FormerlySerializedAs("Gravity")]
        [SerializeField] private float _gravity = -15f;

        [Space(10)]
        [Tooltip("Time required to pass before being able to jump again")]
        [FormerlySerializedAs("JumpTimeout")]
        [SerializeField] private float _jumpTimeout = 0.3f;

        [Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
        [FormerlySerializedAs("FallTimeout")]
        [SerializeField] private float _fallTimeout = 0.15f;

        [Header("Grounded check")]
        [Tooltip("Useful for rough ground")]
        [FormerlySerializedAs("GroundedOffset")]
        [SerializeField] private float _groundedOffset = -0.14f;

        [Tooltip("Radius of the grounded check. Should match the radius of the CharacterController")]
        [FormerlySerializedAs("GroundedRadius")]
        [SerializeField] private float _groundedRadius = 0.28f;

        [Tooltip("Layers the character treats as ground")]
        [FormerlySerializedAs("GroundLayers")]
        [SerializeField] private LayerMask _groundLayers;
        #endregion

        private const float TerminalVelocity = 53f;
        private const float CombatSpeedBonus = 1.2f;
        private const float BackpedalSpeedFactor = 0.7f;

        private float _targetRotation;
        private float _rotationVelocity;
        private float _verticalVelocity;
        private float _jumpTimeoutDelta;
        private float _fallTimeoutDelta;
        private float _animationBlend;
        private Vector2 _directionalSpeed;
        private bool _jumpQueued;

        private CharacterController _controller;
        private InputController _input;
        private PlayerAnimation _animation;
        private PlayerState _state;
        private PlayerAttributes _attributes;
        private PlayerCameraControl _cameraControl;
        private PlayerWeapons _weapons;

        /// <summary>Whether the character currently stands on ground.</summary>
        public bool IsGrounded { get; private set; } = true;

        /// <summary>Target horizontal speed of the last simulated tick (m/s).</summary>
        public float CurrentSpeed { get; private set; }

        /// <summary>Base walking speed (m/s), before size and combat modifiers.</summary>
        public float WalkSpeed => _moveSpeed;

        #region Prediction data
        /// <summary>Input for one tick, sent from the owner to the server.</summary>
        public struct MoveData : IReplicateData
        {
            public bool AnalogMovement;
            public Vector2 Move;
            public bool Jump;
            public float CameraYaw;
            public bool Sprint;
            public bool Aim;

            public MoveData(bool analogMovement, Vector2 move, bool jump, float cameraYaw, bool sprint, bool aim)
            {
                AnalogMovement = analogMovement;
                Move = move;
                Jump = jump;
                CameraYaw = cameraYaw;
                Sprint = sprint;
                Aim = aim;
                _tick = 0;
            }

            private uint _tick;
            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }

        /// <summary>Authoritative movement state the server sends back for reconciliation.</summary>
        public struct ReconcileData : IReconcileData
        {
            public Vector3 Position;
            public float TargetRotation;
            public float VerticalVelocity;
            public float FallTimeout;
            public float JumpTimeout;
            public bool Grounded;

            public ReconcileData(Vector3 position, float targetRotation, float verticalVelocity,
                                 float fallTimeout, float jumpTimeout, bool grounded)
            {
                Position = position;
                TargetRotation = targetRotation;
                VerticalVelocity = verticalVelocity;
                FallTimeout = fallTimeout;
                JumpTimeout = jumpTimeout;
                Grounded = grounded;
                _tick = 0;
            }

            private uint _tick;
            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }
        #endregion

        #region Lifecycle
        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _input = GetComponent<InputController>();
            _animation = GetComponent<PlayerAnimation>();
            _state = GetComponent<PlayerState>();
            _attributes = GetComponent<PlayerAttributes>();
            _cameraControl = GetComponent<PlayerCameraControl>();
            _weapons = GetComponent<PlayerWeapons>();

            _jumpTimeoutDelta = _jumpTimeout;
            _fallTimeoutDelta = _fallTimeout;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            base.TimeManager.OnTick += TimeManager_OnTick;
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            base.TimeManager.OnTick -= TimeManager_OnTick;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
#if ENABLE_INPUT_SYSTEM
            // Only the owner reads local input; the component is disabled on the prefab.
            if (base.IsOwner) GetComponent<PlayerInput>().enabled = true;
#endif
        }

        private void Update()
        {
            // Latch frame-based button presses until the next network tick consumes them.
            if (base.IsOwner && _input.ConsumeJump()) _jumpQueued = true;
        }
        #endregion

        #region Prediction
        private void TimeManager_OnTick()
        {
            Move(BuildMoveData());
            CreateReconcile();   // called on every instance; only the server actually sends it
        }

        private MoveData BuildMoveData()
        {
            if (!base.IsOwner) return default;

            MoveData data = new MoveData(_input.AnalogMovement, _input.Move, _jumpQueued,
                                         _cameraControl.CameraYaw, _input.Sprint, _input.Aim);
            _jumpQueued = false;
            return data;
        }

        public override void CreateReconcile()
        {
            Reconciliation(new ReconcileData(transform.position, _targetRotation, _verticalVelocity,
                                             _fallTimeoutDelta, _jumpTimeoutDelta, IsGrounded));
        }

        [Reconcile]
        private void Reconciliation(ReconcileData data, Channel channel = Channel.Unreliable)
        {
            transform.position = data.Position;
            _targetRotation = data.TargetRotation;
            _verticalVelocity = data.VerticalVelocity;
            _fallTimeoutDelta = data.FallTimeout;
            _jumpTimeoutDelta = data.JumpTimeout;
            IsGrounded = data.Grounded;
        }

        [Replicate]
        private void Move(MoveData data, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
        {
            float delta = (float)base.TimeManager.TickDelta;
            bool replaying = (state & ReplicateState.Replayed) != 0;

            JumpAndGravity(data, delta, replaying);
            GroundedCheck();
            HorizontalMovement(data, delta, replaying);
        }
        #endregion

        #region Simulation
        private void HorizontalMovement(MoveData data, float delta, bool replaying)
        {
            bool dead = _attributes.IsDead;
            bool inCombatStance = (data.Aim && _weapons.RangedWeapon != null)
                                  || _state.Is(MeleeState.Readied, MeleeState.Striking, MeleeState.Blocking);
            bool meleeEquipped = !_state.Is(MeleeState.Inactive);

            // Walking backwards while in combat stance is slower.
            bool backpedaling = inCombatStance && data.Move.y < -0.9f && Mathf.Abs(data.Move.x) < 0.5f;
            float targetSpeed = data.Sprint && !inCombatStance ? _sprintSpeed
                                                               : _moveSpeed * (backpedaling ? BackpedalSpeedFactor : 1f);
            targetSpeed *= _attributes.Size;
            if (meleeEquipped) targetSpeed *= CombatSpeedBonus;
            if (data.Move == Vector2.zero || dead) targetSpeed = 0f;
            CurrentSpeed = targetSpeed;

            if (data.Move != Vector2.zero && !dead)
            {
                Vector3 inputDirection = new Vector3(data.Move.x, 0f, data.Move.y).normalized;
                _targetRotation = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg + data.CameraYaw;
                float rotation = Mathf.SmoothDampAngle(transform.eulerAngles.y, _targetRotation, ref _rotationVelocity,
                                                       _rotationSmoothTime, Mathf.Infinity, delta);

                // In combat stance the character faces the aim direction instead (see PlayerCombat).
                if (!inCombatStance) transform.rotation = Quaternion.Euler(0f, rotation, 0f);
            }

            Vector3 targetDirection = Quaternion.Euler(0f, _targetRotation, 0f) * Vector3.forward;
            if (!dead && _controller.enabled)
                _controller.Move(targetDirection.normalized * (targetSpeed * delta) + new Vector3(0f, _verticalVelocity, 0f) * delta);

            if (!replaying) UpdateLocomotionAnimation(data, delta, targetSpeed, meleeEquipped);
        }

        private void UpdateLocomotionAnimation(MoveData data, float delta, float targetSpeed, bool meleeEquipped)
        {
            float size = _attributes.Size;

            _animationBlend = Mathf.Lerp(_animationBlend, targetSpeed / size, delta * _speedChangeRate);
            if (_animationBlend < 0.01f) _animationBlend = 0f;

            _directionalSpeed = Vector2.Lerp(_directionalSpeed, data.Move, delta * _speedChangeRate / _moveSpeed);

            float inputMagnitude = data.AnalogMovement ? data.Move.magnitude : 1f;
            _animation.SetSpeed(_animationBlend);
            _animation.SetMotionSpeed(inputMagnitude * (meleeEquipped ? CombatSpeedBonus : 1f));
            _animation.SetForward(_directionalSpeed.x * targetSpeed / size);
            _animation.SetStrafe(_directionalSpeed.y * targetSpeed / size);
        }

        private void JumpAndGravity(MoveData data, float delta, bool replaying)
        {
            if (IsGrounded)
            {
                _fallTimeoutDelta = _fallTimeout;

                if (!replaying)
                {
                    _animation.SetJump(false);
                    _animation.SetFreeFall(false);
                }

                // Keep a small downward velocity so the controller stays grounded.
                if (_verticalVelocity < 0f) _verticalVelocity = -2f;

                if (data.Jump && !_attributes.IsDead && _jumpTimeoutDelta <= 0f)
                {
                    _verticalVelocity = Mathf.Sqrt(_jumpHeight * _attributes.Size * -2f * _gravity);
                    if (!replaying) _animation.SetJump(true);
                }

                if (_jumpTimeoutDelta >= 0f) _jumpTimeoutDelta -= delta;
            }
            else
            {
                _jumpTimeoutDelta = _jumpTimeout;

                if (_fallTimeoutDelta >= 0f) _fallTimeoutDelta -= delta;
                else if (!replaying) _animation.SetFreeFall(true);
            }

            if (_verticalVelocity < TerminalVelocity) _verticalVelocity += _gravity * delta;
        }

        private void GroundedCheck()
        {
            Vector3 position = transform.position;
            Vector3 spherePosition = new Vector3(position.x, position.y - _groundedOffset, position.z);
            IsGrounded = Physics.CheckSphere(spherePosition, _groundedRadius, _groundLayers, QueryTriggerInteraction.Ignore);
            _animation.SetGrounded(IsGrounded);
        }
        #endregion

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = IsGrounded ? new Color(0f, 1f, 0f, 0.35f) : new Color(1f, 0f, 0f, 0.35f);
            Vector3 position = transform.position;
            Gizmos.DrawSphere(new Vector3(position.x, position.y - _groundedOffset, position.z), _groundedRadius);
        }
    }
}

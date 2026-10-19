using AgaRPG.Player;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Serialization;

namespace AgaRPG.Combat
{
    /// <summary>
    /// Predicted combat for ranged and directional melee weapons.
    /// </summary>
    /// <remarks>
    /// The owner samples input every frame, packs it into <see cref="CombatData"/> on each network
    /// tick and runs it through FishNet's <c>[Replicate]</c>; the server (and spectators, via state
    /// forwarding) run the same tick logic. Visual-only work is skipped during reconciliation
    /// replays. Melee strikes on the owner are latency compensated with
    /// <see cref="StrikeTimingCompensator"/>.
    /// </remarks>
    public class PlayerCombat : NetworkBehaviour
    {
        #region Inspector
        [Header("Aiming")]
        [Tooltip("Normal look sensitivity")]
        [SerializeField] private float _normalSensitivity;

        [Tooltip("Look sensitivity while aiming")]
        [SerializeField] private float _aimSensitivity;

        [Tooltip("Aim target, moved to the point under the crosshair")]
        [SerializeField] private Transform _aimTarget;

        [Tooltip("Start the aim ray this far beyond the camera distance, so it starts in front of the player")]
        [FormerlySerializedAs("rayOffset")]
        [SerializeField] private float _rayOffset = 1f;

        [Header("Animation rigging")]
        [Tooltip("Rig for head rotation")]
        [SerializeField] private Rig _headRig;

        [Tooltip("Rig for gun pointing")]
        [SerializeField] private Rig _aimRig;

        [Tooltip("Rig for gun holding")]
        [SerializeField] private Rig _handRig;

        [Tooltip("Rig for melee weapon position")]
        [SerializeField] private Rig _meleeRig;

        [Tooltip("Weapon to pivot constraint when readying")]
        [SerializeField] private TwoBoneIKConstraint _meleeReadyConstraint;

        [Tooltip("Offhand to pivot constraint when readying")]
        [SerializeField] private TwoBoneIKConstraint _offhandReadyConstraint;

        [Tooltip("Weapon to pivot constraint when striking")]
        [SerializeField] private TwoBoneIKConstraint _meleeStrikeConstraint;

        [Tooltip("Offhand to pivot constraint when striking")]
        [SerializeField] private TwoBoneIKConstraint _offhandStrikeConstraint;

        [Tooltip("Melee striking pivots container, follows the camera rotation")]
        [SerializeField] private GameObject _meleeStrikePivot;

        [Header("Melee timing")]
        [Tooltip("Seconds a strike stays active (can hit)")]
        [SerializeField] private float _strikeDuration = 0.5f;

        [Tooltip("Minimum time between strike direction changes")]
        [SerializeField] private float _directionChangeCooldown = 1f / 3f;

        [Tooltip("Ticks the server buffers input before simulating it; used for strike compensation")]
        [SerializeField] private int _serverInputBufferTicks = 2;

        [Header("Debug")]
        [FormerlySerializedAs("alwaysAim")]
        [SerializeField] private bool _debugAlwaysAim;

        [FormerlySerializedAs("alwaysReady")]
        [SerializeField] private bool _debugAlwaysReady;

        [FormerlySerializedAs("alwaysStrike")]
        [SerializeField] private bool _debugAlwaysStrike;

        [FormerlySerializedAs("controlChest")]
        [SerializeField] private bool _debugControlChest;
        #endregion

        #region Constants
        private const string AimCameraTag = "PlayerAimCamera";
        private const string MeleeCameraTag = "PlayerMeleeCamera";
        private const string MeleeAnimatorLayer = "Melee Weapon";
        private const int AimAnimatorLayer = 2;
        private const int ActiveCameraPriority = 20;
        private const int InactiveCameraPriority = 5;
        private const float RigBlendSpeed = 25f;
        private const float ConstraintBlendSpeed = 20f;
        private const float ReadyTransitionSpeed = 15f;
        private const float BodyTurnSpeed = 20f;
        private const float AimRayLength = 999f;
        #endregion

        #region State
        private LayerMask _aimLayerMask;
        private TwoBoneIKConstraint _chestConstraint;
        private CinemachineCamera _aimCamera;
        private CinemachineCamera _meleeCamera;
        private CinemachineThirdPersonFollow _aimCameraFollow;
        private CinemachineThirdPersonFollow _meleeCameraFollow;

        private InputController _input;
        private PlayerState _state;
        private PlayerAnimation _animation;
        private PlayerAttributes _attributes;
        private PlayerCameraControl _cameraControl;
        private PlayerWeapons _weapons;

        // Rig weights the rigs blend towards.
        private float _headRigTarget = 1f;
        private float _aimRigTarget;
        private float _aimLayerTarget;
        private float _meleeRigTarget;

        // Trigger input sampled per frame and consumed per tick (owner only).
        private bool _triggerHeld;
        private bool _shootQueued;
        private bool _releaseQueued;

        // Melee simulation.
        private Vector2 _strikeDirection;
        private Vector2 _previousStrikeDirection;
        private float _readyTransition;
        private float _strikeTimer;
        private float _directionCooldown;
        private bool _firstDirectionChange = true;
        private readonly StrikeTimingCompensator _strikeCompensator = new StrikeTimingCompensator();

        private CombatData _lastCombatData;

        // Strafing is decided by the owner and relayed by the server to all other clients.
        private readonly SyncVar<bool> _strafeSync = new(new SyncTypeSettings(
            WritePermission.ServerOnly, ReadPermission.ExcludeOwner, 0.05f, Channel.Unreliable));

        /// <summary>True while the character strafes (aiming, or melee stance) instead of turning to move.</summary>
        public bool IsStrafing { get; private set; }
        #endregion

        #region Prediction data
        /// <summary>Combat input for one tick.</summary>
        public struct CombatData : IReplicateData
        {
            public Vector2 Look;
            public Quaternion CameraRotation;
            public bool Aim;
            public bool Shoot;
            public bool Released;
            public Ray ScreenCenterRay;

            public CombatData(Vector2 look, Quaternion cameraRotation, bool aim, bool shoot, bool released, Ray screenCenterRay)
            {
                Look = look;
                CameraRotation = cameraRotation;
                Aim = aim;
                Shoot = shoot;
                Released = released;
                ScreenCenterRay = screenCenterRay;
                _tick = 0;
            }

            private uint _tick;
            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }

        /// <summary>Authoritative combat state sent by the server.</summary>
        public struct ReconcileData : IReconcileData
        {
            public Vector3 AimTargetPosition;

            public ReconcileData(Vector3 aimTargetPosition)
            {
                AimTargetPosition = aimTargetPosition;
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
            _strafeSync.OnChange += OnStrafeSynced;

            _input = GetComponent<InputController>();
            _state = GetComponent<PlayerState>();
            _animation = GetComponent<PlayerAnimation>();
            _attributes = GetComponent<PlayerAttributes>();
            _cameraControl = GetComponent<PlayerCameraControl>();
            _weapons = GetComponent<PlayerWeapons>();

            _chestConstraint = _meleeRig.transform.Find("ChestPosition").GetComponent<TwoBoneIKConstraint>();
            _aimLayerMask = LayerMask.GetMask("Default", "PlayerBody", "PlayerHead");
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
            if (_aimCamera != null) return;

            _aimCamera = GameObject.FindGameObjectWithTag(AimCameraTag).GetComponent<CinemachineCamera>();
            _meleeCamera = GameObject.FindGameObjectWithTag(MeleeCameraTag).GetComponent<CinemachineCamera>();
            _aimCameraFollow = _aimCamera.GetComponent<CinemachineThirdPersonFollow>();
            _meleeCameraFollow = _meleeCamera.GetComponent<CinemachineThirdPersonFollow>();

            if (base.IsOwner)
            {
                _aimCamera.Follow = _cameraControl.CameraTarget;
                _meleeCamera.Follow = _cameraControl.CameraTarget;
            }

            // The aim target lives in world space so it is not dragged along by the body.
            _aimTarget.parent.SetParent(null, true);
        }

        private void OnDestroy()
        {
            // The aim container was detached in OnStartClient, so it isn't destroyed with the player.
            if (_aimTarget != null && _aimTarget.parent != null && !_aimTarget.parent.IsChildOf(transform))
                Destroy(_aimTarget.parent.gameObject);
        }

        private void Update()
        {
            if (!base.IsOwner) return;

            // Latch trigger edges between ticks so short clicks are never missed.
            bool held = _input.Shoot;
            if (!held && _triggerHeld) _releaseQueued = true;

            RaycastWeapon ranged = _weapons.RangedWeapon;
            if (ranged != null && !ranged.IsAutomatic)
            {
                if (held && !_triggerHeld) _shootQueued = true;   // one shot per press
            }
            else _shootQueued = held;

            _triggerHeld = held;
        }
        #endregion

        #region Prediction
        private void TimeManager_OnTick()
        {
            Attack(BuildCombatData());
            CreateReconcile();   // called on every instance; only the server actually sends it
        }

        private CombatData BuildCombatData()
        {
            if (!base.IsOwner) return default;

            Ray ray = Camera.main.ScreenPointToRay(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            ray.origin += ray.direction * (ActiveCameraDistance() + _rayOffset);

            bool aim = _input.Aim || _debugAlwaysAim;
            bool hasMelee = _weapons.MeleeWeapon != null;
            bool shoot = (_shootQueued && (aim || hasMelee)) || _debugAlwaysReady;

            var data = new CombatData(_input.Look, _cameraControl.CameraTarget.rotation, aim, shoot, _releaseQueued, ray);

            if (_weapons.RangedWeapon != null && !_weapons.RangedWeapon.IsAutomatic) _shootQueued = false;
            _releaseQueued = false;
            return data;
        }

        public override void CreateReconcile() => Reconciliation(new ReconcileData(_aimTarget.position));

        [Reconcile]
        private void Reconciliation(ReconcileData data, Channel channel = Channel.Unreliable) =>
            _aimTarget.position = data.AimTargetPosition;

        [Replicate]
        private void Attack(CombatData data, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
        {
            if (!base.IsOwner)
            {
                // Without fresh input from the owner, predict that it keeps doing the same thing.
                if ((state & ReplicateState.Created) != 0) _lastCombatData = data;
                else
                {
                    uint tick = data.GetTick();
                    data = _lastCombatData;
                    data.SetTick(tick);
                }
            }

            Simulate(data, (float)base.TimeManager.TickDelta, (state & ReplicateState.Replayed) != 0);
        }

        private float ActiveCameraDistance()
        {
            if (_aimCamera.Priority == ActiveCameraPriority) return _aimCameraFollow.CameraDistance;
            if (_meleeCamera.Priority == ActiveCameraPriority) return _meleeCameraFollow.CameraDistance;
            return _cameraControl.FollowDistance;
        }
        #endregion

        #region Shared simulation
        private void Simulate(CombatData data, float delta, bool replaying)
        {
            bool hasAimPoint = Physics.Raycast(data.ScreenCenterRay, out RaycastHit hit, AimRayLength, _aimLayerMask);
            if (hasAimPoint) _aimTarget.position = hit.point;

            // Everything below is either visual or must only happen once per tick.
            if (replaying) return;

            if (_state.Is(RangedState.Aiming, RangedState.Shooting)
                || _state.Is(MeleeState.Readied, MeleeState.Striking, MeleeState.Blocking))
                FaceAimPoint(hasAimPoint ? hit.point : Vector3.zero, delta);

            _meleeStrikePivot.transform.rotation = data.CameraRotation;

            BlendRigs(delta);
            _animation.TransitionLayerWeight(AimAnimatorLayer, _aimLayerTarget, delta);

            if (_state.Is(RangedState.Inactive, RangedState.Holstered)) ExitRangedCombat(delta);
            else RangedCombat(data, delta, hit);

            if (_state.Is(MeleeState.Inactive, MeleeState.Sheathed)) ExitMeleeCombat();
            else MeleeCombat(data, delta);
        }

        private void FaceAimPoint(Vector3 aimPoint, float delta)
        {
            Vector3 flatTarget = new Vector3(aimPoint.x, transform.position.y, aimPoint.z);
            Vector3 direction = (flatTarget - transform.position).normalized;
            transform.forward = Vector3.Lerp(transform.forward, direction, delta * BodyTurnSpeed);
        }

        private void BlendRigs(float delta)
        {
            float t = RigBlendSpeed * delta;
            _headRig.weight = Mathf.Lerp(_headRig.weight, _headRigTarget, t);
            _aimRig.weight = Mathf.Lerp(_aimRig.weight, _aimRigTarget, t);
            _meleeRig.weight = Mathf.Lerp(_meleeRig.weight, _meleeRigTarget, t);
        }

        private void SetStrafing(bool value)
        {
            if (IsStrafing == value) return;
            IsStrafing = value;
            if (base.IsServerInitialized) _strafeSync.Value = value;
            else if (base.IsOwner) ServerRpcSetStrafing(value);
        }

        [ServerRpc]
        private void ServerRpcSetStrafing(bool value)
        {
            IsStrafing = value;
            _strafeSync.Value = value;
        }

        private void OnStrafeSynced(bool previous, bool next, bool asServer)
        {
            IsStrafing = next;
            if (!base.IsOwner) SetRangedRigs(next);
        }

        private void SetCameraPriority(CinemachineCamera camera, bool active)
        {
            if (base.IsOwner) camera.Priority = active ? ActiveCameraPriority : InactiveCameraPriority;
        }
        #endregion

        #region Ranged
        private void RangedCombat(CombatData data, float delta, RaycastHit hit)
        {
            RaycastWeapon weapon = _weapons.RangedWeapon;
            _handRig.weight = Mathf.Lerp(_handRig.weight, 1f, RigBlendSpeed * delta);

            if (base.IsOwner) SetStrafing(data.Aim);
            weapon.RecoverTime(delta);

            _state.Set(!data.Aim ? RangedState.Drawn : data.Shoot ? RangedState.Shooting : RangedState.Aiming);

            bool aiming = _state.Is(RangedState.Aiming, RangedState.Shooting);
            SetCameraPriority(_aimCamera, aiming);
            _aimLayerTarget = aiming ? 1f : 0f;
            SetRangedRigs(aiming);
            if (base.IsOwner) _cameraControl.Sensitivity = aiming ? _aimSensitivity : _normalSensitivity;

            if (_state.Is(RangedState.Shooting)) weapon.UpdateFiring(hit, _attributes.Size, delta);
            else weapon.StopFiring();
        }

        private void ExitRangedCombat(float delta)
        {
            _handRig.weight = Mathf.Lerp(_handRig.weight, 0f, RigBlendSpeed * delta);
            if (base.IsOwner && _aimCamera.Priority > InactiveCameraPriority) _aimCamera.Priority = InactiveCameraPriority;
        }

        private void SetRangedRigs(bool aiming)
        {
            _headRigTarget = aiming ? 0f : 1f;
            _aimRigTarget = aiming ? 1f : 0f;
            _weapons.SetPoseRig(!aiming);
        }
        #endregion

        #region Melee
        private void MeleeCombat(CombatData data, float delta)
        {
            MeleeWeapon weapon = _weapons.MeleeWeapon;
            SetCameraPriority(_meleeCamera, true);

            _strikeTimer = Mathf.Max(0f, _strikeTimer - delta);
            _directionCooldown = Mathf.Max(0f, _directionCooldown - delta);
            weapon.RecoverTime(delta);

            if (_debugAlwaysStrike) _state.Set(MeleeState.Striking);
            else if (_state.Is(MeleeState.Striking) && _strikeTimer <= 0f) _state.Set(MeleeState.Drawn);

            if (base.IsOwner) SetStrafing(!_state.Is(MeleeState.Drawn));
            if (_state.Is(MeleeState.Drawn)) _strikeDirection = Vector2.zero;

            UpdateMeleeState(data, weapon);

            if (!_state.Is(MeleeState.Striking)) weapon.CanHit = weapon.CanDamage = false;

            UpdateMeleeRigs(delta);
            UpdateMeleeAnimation(data, delta);
        }

        private void UpdateMeleeState(CombatData data, MeleeWeapon weapon)
        {
            if (data.Aim && _state.Is(MeleeState.Drawn, MeleeState.Readied))
            {
                if (base.IsServerInitialized) weapon.CanBlock = true;
                _state.Set(MeleeState.Blocking);
                _firstDirectionChange = true;
            }

            if (!data.Aim && _state.Is(MeleeState.Blocking))
            {
                if (base.IsServerInitialized) weapon.CanBlock = false;
                _state.Set(MeleeState.Drawn);
                _firstDirectionChange = true;
            }

            if (data.Shoot && _state.Is(MeleeState.Drawn) && weapon.IsReady)
            {
                _state.Set(MeleeState.Readied);
                _firstDirectionChange = true;
            }

            if (data.Released && _state.Is(MeleeState.Readied)) BeginStrike(weapon);
        }

        private void BeginStrike(MeleeWeapon weapon)
        {
            _state.Set(MeleeState.Striking);
            _strikeTimer = _strikeDuration;

            weapon.StartCooldown();
            weapon.CanHit = true;
            if (base.IsServerInitialized) weapon.CanDamage = true;
            weapon.PlaySwingSound();

            // Only the owner runs ahead of the server, so only the owner slows its swing down.
            if (base.IsOwner && !base.IsServerInitialized)
            {
                float delay = StrikeTimingCompensator.EstimateServerDelay(base.TimeManager.RoundTripTime,
                                                                          base.TimeManager.TickDelta,
                                                                          _serverInputBufferTicks);
                _strikeCompensator.Begin(delay);
            }
            else _strikeCompensator.Reset();

            _animation.SetStrikeSpeed(_strikeCompensator.IsCompensating ? 0f : 1f);
            _animation.SetStrike(_strikeDirection);
            _strikeDirection = Vector2.zero;
            _firstDirectionChange = true;
        }

        private void UpdateMeleeRigs(float delta)
        {
            bool striking = _state.Is(MeleeState.Striking);
            bool stance = !_state.Is(MeleeState.Drawn);

            _chestConstraint.weight = Mathf.Lerp(_chestConstraint.weight, _debugControlChest || striking ? 1f : 0f,
                                                 ConstraintBlendSpeed * delta);

            _aimLayerTarget = stance ? 1f : 0f;
            _headRigTarget = stance ? 0f : 1f;
            _meleeRigTarget = stance ? 1f : 0f;

            // Hand the weapon over from the "ready" pivots to the "strike" pivots.
            float t = ConstraintBlendSpeed * delta;
            if (striking)
            {
                _offhandReadyConstraint.weight = Mathf.Lerp(_offhandReadyConstraint.weight, 0f, t);
                _meleeReadyConstraint.weight = Mathf.Lerp(_meleeReadyConstraint.weight, 0f, t);
                _offhandStrikeConstraint.weight = Mathf.Lerp(_offhandStrikeConstraint.weight, 1f, t);
                _meleeStrikeConstraint.weight = Mathf.Lerp(_meleeStrikeConstraint.weight, 1f, t);
            }
            else
            {
                _offhandReadyConstraint.weight = 1f;
                _meleeReadyConstraint.weight = 1f;
                _offhandStrikeConstraint.weight = Mathf.Lerp(_offhandStrikeConstraint.weight, 0f, t);
                _meleeStrikeConstraint.weight = Mathf.Lerp(_meleeStrikeConstraint.weight, 0f, t);
            }
        }

        private void UpdateMeleeAnimation(CombatData data, float delta)
        {
            AnimatorStateInfo animatorState = _animation.GetStateInfo(MeleeAnimatorLayer);

            switch (_state.Melee)
            {
                case MeleeState.Readied:
                    if (!animatorState.IsName("Ready")) _animation.SetReady();
                    UpdateStrikeDirection(data.Look, delta);
                    break;

                case MeleeState.Blocking:
                    if (!animatorState.IsName("Block")) _animation.SetBlock();
                    UpdateStrikeDirection(data.Look, delta);
                    break;

                case MeleeState.Striking:
                    _animation.SetStrikeSpeed(_strikeCompensator.Step(delta));
                    break;
            }
        }

        /// <summary>
        /// Picks the strike/block direction from mouse movement: a decisive flick left/right/up/down
        /// switches direction. The first change after readying needs less movement.
        /// </summary>
        private void UpdateStrikeDirection(Vector2 look, float delta)
        {
            _previousStrikeDirection = _strikeDirection;

            if (!_debugAlwaysReady)
            {
                // Strike from the right by default.
                if (look == Vector2.zero && _strikeDirection == Vector2.zero) _strikeDirection = Vector2.right;

                float threshold = _firstDirectionChange ? 1f : 3f;
                if (look != Vector2.zero && look.magnitude > threshold && _directionCooldown <= 0f)
                {
                    Vector2 normalized = look.normalized;
                    float horizontalThreshold = threshold + (_firstDirectionChange ? 0f : 1f);
                    if (Mathf.Abs(normalized.x) > 0.9f && look.magnitude > horizontalThreshold)
                        _strikeDirection = Mathf.Sign(look.x) * Vector2.right;
                    else if (Mathf.Abs(normalized.y) > 0.9f)
                        _strikeDirection = Mathf.Sign(look.y) * Vector2.up;

                    _directionCooldown = _directionChangeCooldown;
                    _firstDirectionChange = false;
                }

                if (_strikeDirection != _previousStrikeDirection)
                    _readyTransition = _previousStrikeDirection == Vector2.zero ? 0f : -1f;
            }

            // Blend through neutral when switching sides so the weapon swings across the body.
            _readyTransition = Mathf.Lerp(_readyTransition, 1f, ReadyTransitionSpeed * delta);
            Vector2 shown = _readyTransition <= 0f ? -_previousStrikeDirection : _strikeDirection;
            _animation.SetStrikeDirection(_readyTransition * shown);
        }

        private void ExitMeleeCombat()
        {
            _headRigTarget = 1f;
            _meleeRigTarget = 0f;
            if (base.IsOwner && _meleeCamera.Priority > InactiveCameraPriority) _meleeCamera.Priority = InactiveCameraPriority;
        }
        #endregion
    }
}

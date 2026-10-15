
using FishNet;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Animations.Rigging;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif



public class PlayerCombat : NetworkBehaviour
{
    #region Properties
    // aiming
    [Tooltip("Normal sensitivity")]
    [SerializeField] private float _normalSensitivity;

    [Tooltip("Sensitivity while aiming")]
    [SerializeField] private float _aimSensitivity;

    [SyncVar][Tooltip("Aim target at mouse world position")]
    [SerializeField] private Transform _aimTarget;

    [Tooltip("Capsule hitmask on own body (to exclude from aim target collisions)")]
    [SerializeField] private GameObject _bodyHitMask;

    [Tooltip("Capsule hitmask on own head (to exclude from aim target collisions)")]
    [SerializeField] private GameObject _headHitMask;

    private LayerMask _aimColliderLayerMask = new LayerMask();

    //animation rigging
    [Tooltip("Animation rig for head rotation")]
    [SerializeField] private Rig _headRig;

    [Tooltip("Animation rig for gun pointing")]
    [SerializeField] private Rig _aimRig;

    [Tooltip("Animation rig for gun holding")]
    [SerializeField] private Rig _handRig;

    [Tooltip("Animation rig for melee weapon position")]
    [SerializeField] private Rig _meleeRig;

    [Tooltip("Weapon to pivot constraint when readying")]
    [SerializeField] private TwoBoneIKConstraint _meleeReadyConstraint;

    [Tooltip("Offhand to pivot constraint when readying")]
    [SerializeField] private TwoBoneIKConstraint _offhandReadyConstraint;

    [Tooltip("Weapon to pivot constraint when striking")]
    [SerializeField] private TwoBoneIKConstraint _meleeStrikeConstraint;

    [Tooltip("Offhand to pivot constraint when striking")]
    [SerializeField] private TwoBoneIKConstraint _offhandStrikeConstraint;

    [Tooltip("Melee striking pivots container")]
    [SerializeField] private GameObject _meleeStrikePivot;

    

    private float _headRigTargetWeight = 1f;
    private float _aimRigTargetWeight = 0f;
    private float _aimLayerTargetWeight = 0f;
    private float _meleeRigTargetWeight = 0f;

    public bool alwaysAim = false;
    public bool alwaysReady = false;
    public bool alwaysStrike = false;
    public bool controlChest = false;
    public float rayOffset = 1f;

    // cinemachine
    private Unity.Cinemachine.CinemachineVirtualCamera _aimVirtualCamera;
    private Unity.Cinemachine.CinemachineVirtualCamera _meleeVirtualCamera;

    // input
#if ENABLE_INPUT_SYSTEM
    private PlayerInput _playerInput;
#endif
    private InputController _input;
    public bool _shoot = false;
    public bool _pressed = false;
    public bool _released = false;
    public float strikeCooldown = 0f;
    public float readyCooldown = 0f;
    public bool firstReady = true;

    // controllers
    private PlayerState _state;
    private PlayerAnimation _animation;
    private PlayerAttributes _attributes;
    private PlayerMovement _movement;
    private PlayerCameraControl _cameraControl;
    private PlayerWeapons _weapons;

    // player
    [field: SyncVar(Channel = Channel.Unreliable, ReadPermissions = ReadPermission.ExcludeOwner, OnChange = nameof(OnStrafe))]
    [HideInInspector] public bool strafe { get; [ServerRpc(RunLocally = true)] set; } = false;
    private void OnStrafe(bool prev, bool next, bool asServer)
    {
        if (!base.IsOwner) SwapRangedRigs(next);
    }

    public Vector2 strikeDir;
    private Vector2 _prevStrikeDir;
    private float _readyTransition;
    private float _strikeTime;
    private float _latencyEstimate;
    #endregion

    #region ClientSidePredicitonProperties

    private CombatData _lastCombatData = default;

    public struct CombatData : IReplicateData
    {
        public Vector2 Look;
        public Quaternion CameraRotation;
        public bool Aim;
        public bool Shoot;
        public bool Pressed;
        public bool Released;
        public Ray ScreenCenterRay;
        public CombatData(Vector2 look, Quaternion cameraRotation, bool aim, bool shoot,
                          bool pressed, bool released, Ray screenCenterRay)
        {
            Look = look;
            CameraRotation = cameraRotation;
            Aim = aim;
            Shoot = shoot;
            Pressed = pressed;
            Released = released;
            ScreenCenterRay = screenCenterRay;
            _tick = 0;
        }

        private uint _tick;
        public void Dispose() { }
        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
    };

    public struct ReconcileData : IReconcileData
    {
        public Vector3 AimTargetPosition;
        //public Vector3 Forward;
        public RangedState RangedState;
        public MeleeState MeleeState;

        public ReconcileData(Vector3 aimTargetPosition, Vector3 forward, RangedState rangedState, MeleeState meleeState)
        {
            AimTargetPosition = aimTargetPosition;
            //Forward = forward;
            RangedState = rangedState;
            MeleeState = meleeState;
            _tick = 0;
        }

        private uint _tick;
        public void Dispose() { }
        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
    };
    #endregion

    #region Initialization
    private void Awake()
    {
        _input = GetComponent<InputController>();
        _state = GetComponent<PlayerState>();
        _animation = GetComponent<PlayerAnimation>();
        _attributes = GetComponent<PlayerAttributes>();
        _movement = GetComponent<PlayerMovement>();
        _cameraControl = GetComponent<PlayerCameraControl>();
        _weapons = GetComponent<PlayerWeapons>();

        _aimColliderLayerMask = LayerMask.GetMask("Default") | LayerMask.GetMask("PlayerBody")
                                                             | LayerMask.GetMask("PlayerHead");
    }


    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        base.TimeManager.OnTick += TimeManager_OnTick;
        //base.TimeManager.OnUpdate += TimeManager_OnUpdate;
    }


    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        base.TimeManager.OnTick -= TimeManager_OnTick;
        //base.TimeManager.OnUpdate -= TimeManager_OnUpdate;
    }


    public override void OnStartClient()
    {
        base.OnStartClient();
        Initialize();
    }


    private void Initialize()
    {
        if (_aimVirtualCamera != null) return;

        _aimVirtualCamera = GameObject.FindGameObjectWithTag("PlayerAimCamera").GetComponent<Unity.Cinemachine.CinemachineVirtualCamera>();
        if (base.IsOwner) _aimVirtualCamera.Follow = transform.Find("Graphical").transform.Find("PlayerCameraRoot").transform;

        _meleeVirtualCamera = GameObject.FindGameObjectWithTag("PlayerMeleeCamera").GetComponent<Unity.Cinemachine.CinemachineVirtualCamera>();
        if (base.IsOwner) _meleeVirtualCamera.Follow = transform.Find("Graphical").transform.Find("PlayerCameraRoot").transform;

        _aimTarget.parent.SetParent(null, true);

        //if (base.IsOwner)
        //{
        //    _bodyHitMask.layer = LayerMask.NameToLayer("MyPlayerBody");
        //    _headHitMask.layer = LayerMask.NameToLayer("MyPlayerHead");
        //}
    }
    #endregion

    #region ClientSidePredictionLogic
    private void Update()
    {
        // gather trigger input between network ticks
        if (!_shoot && _input.shoot && !_released) _pressed = true;
        if (_shoot && !_input.shoot && !_pressed) _released = true;

        if (_weapons.rangedWeapon != null)
        {
            if (_weapons.rangedWeapon.automatic)
                _shoot = _input.shoot;
            else
            {
                if (_input.shoot) _shoot = true;
                _input.shoot = false;
            }
        }

        if (_weapons.meleeWeapon != null) _shoot = _input.shoot;    }

    private void TimeManager_OnTick()
    {
        Attack(BuildCombatData());

        if (base.IsServer)
        {
            ReconcileData rd = new ReconcileData(_aimTarget.position, transform.forward,
                                                 _state.GetRangedState(), _state.GetMeleeState());
            Reconciliation(rd);
        }
    }


    [ReconcileV2]
    private void Reconciliation(ReconcileData rd, Channel channel = Channel.Unreliable)
    {
        _aimTarget.position = rd.AimTargetPosition;
        //transform.forward = rd.Forward;
    }


    private CombatData BuildCombatData()
    {
        if (!base.IsOwner) return default;

        Vector2 screenCenterPoint = new Vector2(Screen.width / 2f, Screen.height / 2f);
        Ray screenCenterRay = Camera.main.ScreenPointToRay(screenCenterPoint);
        screenCenterRay.origin += screenCenterRay.direction * (CameraDistance() + rayOffset);

        CombatData cd  = new CombatData(_input.look, _cameraControl.CinemachineCameraTarget.transform.rotation, _input.aim || alwaysAim,
                                       _shoot && _input.aim || _shoot && _weapons.meleeWeapon != null || alwaysReady,
                                       _pressed, _released, screenCenterRay);

        if (_weapons.rangedWeapon != null && !_weapons.rangedWeapon.automatic) _shoot = false;
        if (_pressed) Debug.Log("PRESSED");
        if (_released) Debug.Log("RELEASED");
        _pressed = _released = false;

        return cd;
    }


    private float CameraDistance()
    {
        if (_aimVirtualCamera.m_Priority == 20)
            return _aimVirtualCamera.GetCinemachineComponent<Unity.Cinemachine.Cinemachine3rdPersonFollow>().CameraDistance;
        else if (_meleeVirtualCamera.m_Priority == 20)
            return _meleeVirtualCamera.GetCinemachineComponent<Unity.Cinemachine.Cinemachine3rdPersonFollow>().CameraDistance;
        else
            return _cameraControl.GetCameraDistance();
    }


    [ReplicateV2]
    private void Attack(CombatData cd, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
    {
        if (!base.IsOwner)
        {
            if (state == ReplicateState.UserCreated || state == ReplicateState.ReplayedUserCreated)
                _lastCombatData = cd;
            else
            {
                uint tick = cd.GetTick();
                cd = _lastCombatData;
                cd.SetTick(tick);
            }
        }
        AttackWithData(cd, (float)base.TimeManager.TickDelta, state);
    }
    #endregion

    #region CombatLogic
    private void AttackWithData(CombatData cd, float delta, ReplicateState state = ReplicateState.Invalid)
    {
        bool replaying = (state == ReplicateState.ReplayedUserCreated
                           || state == ReplicateState.ReplayedPredicted);

        // update mouse aim target in world
        Vector3 mouseWorldPosition = Vector3.zero;
        RaycastHit raycastHit = new RaycastHit();
        if (Physics.Raycast(cd.ScreenCenterRay, out raycastHit, 999f, _aimColliderLayerMask))
        {
            _aimTarget.position = raycastHit.point;
            mouseWorldPosition = raycastHit.point;
        }
        
        if (replaying) return;

        // align player rotation to camera
        if (_state.Is(RangedState.Aiming, RangedState.Shooting)
            || _state.Is(MeleeState.Readied, MeleeState.Striking, MeleeState.Blocking))
        {
            Vector3 worldAimTarget = mouseWorldPosition;
            worldAimTarget.y = transform.position.y;
            Vector3 aimDirection = (worldAimTarget - transform.position).normalized;
            transform.forward = Vector3.Lerp(transform.forward, aimDirection, delta * 20f);
        }

        //Debug.Log($"{ad.CameraRotation} {_meleeStrikePivot.rotation}");
        _meleeStrikePivot.transform.rotation = cd.CameraRotation;

        // transition animations
        TransitionRigs(delta);
        _animation.TransitionLayerWeight(2, _aimLayerTargetWeight, delta);

        // ranged logic
        if (_state.Is(RangedState.Inactive, RangedState.Holstered)) ExitRangedCombat(delta);
        else RangedCombat(cd, delta, raycastHit);

        // melee logic
        if (_state.Is(MeleeState.Inactive, MeleeState.Sheathed)) ExitMeleeCombat(delta);
        else MeleeCombat(cd, delta);
    }

    private void TransitionRigs(float delta)
    {
        _headRig.weight = Mathf.Lerp(_headRig.weight, _headRigTargetWeight, 25f * delta);
        _aimRig.weight = Mathf.Lerp(_aimRig.weight, _aimRigTargetWeight, 25f * delta);
        _meleeRig.weight = Mathf.Lerp(_meleeRig.weight, _meleeRigTargetWeight, 25f * delta);
    }
    #endregion

    #region RangedLogic
    private void RangedCombat(CombatData cd, float delta, RaycastHit raycastHit)
    {
        EnterRangedCombat(delta);
        //if (_handRig.weight < 1f) _handRig.weight = Mathf.Lerp(_handRig.weight, 1f, 25f * delta);

        if (base.IsOwner) strafe = cd.Aim;
        _weapons.rangedWeapon.RecoverTime(delta);

        _state.Set(!cd.Aim ? RangedState.Drawn : cd.Shoot ? RangedState.Shooting : RangedState.Aiming);

        if (_state.Is(RangedState.Aiming, RangedState.Shooting))
        {
            if (base.IsOwner) _aimVirtualCamera.m_Priority = 20;
            _aimLayerTargetWeight = 1f;
            SwapRangedRigs(true);
            SetSensitivity(_aimSensitivity);
        }
        else
        {
            if (base.IsOwner) _aimVirtualCamera.m_Priority = 5;
            _aimLayerTargetWeight = 0f;
            SwapRangedRigs(false);
            SetSensitivity(_normalSensitivity);
        }

        if (_state.Is(RangedState.Shooting))
            _weapons.rangedWeapon.UpdateFiring(raycastHit, _attributes.size, delta);
        else _weapons.rangedWeapon.StopFiring();
    }

    private void EnterRangedCombat(float delta)
    {
        if (_handRig.weight < 1f) _handRig.weight = Mathf.Lerp(_handRig.weight, 1f, 25f * delta);
    }

    private void ExitRangedCombat(float delta)
    {
        if (_handRig.weight > 0f) _handRig.weight = Mathf.Lerp(_handRig.weight, 0f, 25f * delta);
        if (base.IsOwner && _aimVirtualCamera.m_Priority > 5) _aimVirtualCamera.m_Priority = 5;
    }

    public void SetSensitivity(float newSensitivity) => _cameraControl.Sensitivity = newSensitivity;

    private void SwapRangedRigs(bool aim)
    {
        _headRigTargetWeight = aim ? 0f : 1f;
        _aimRigTargetWeight = aim ? 1f : 0f;
        _weapons.SetPoseRig(!aim);
    }
    #endregion

    #region MeleeLogic
    private void MeleeCombat(CombatData cd, float delta)
    {
         EnterMeleeCombat(delta);
        //if (base.IsOwner && _meleeVirtualCamera.m_Priority < 20) _meleeVirtualCamera.m_Priority = 20;

        strikeCooldown = Mathf.Clamp(strikeCooldown - delta, 0f, .5f);
        readyCooldown = Mathf.Clamp(readyCooldown - delta, 0f, 1f/3f);
        _weapons.meleeWeapon.RecoverTime(delta);
        if (!alwaysStrike && _state.Is(MeleeState.Striking) && strikeCooldown == 0) _state.Set(MeleeState.Drawn);

        if (base.IsOwner) strafe = !_state.Is(MeleeState.Drawn);

        if (alwaysStrike) _state.Set(MeleeState.Striking);

        if (_state.Is(MeleeState.Drawn))
            strikeDir = Vector2.zero;

        // state transitioning
        if (cd.Aim && _state.Is(MeleeState.Drawn, MeleeState.Readied))
        {
            if (base.IsServer) _weapons.meleeWeapon.canBlock = true;
            _state.Set(MeleeState.Blocking);
            firstReady = true;
        }

        if (!cd.Aim && _state.Is(MeleeState.Blocking))
        {
            if (base.IsServer) _weapons.meleeWeapon.canBlock = false;
            _state.Set(MeleeState.Drawn);
            firstReady = true;
        }

        if (cd.Shoot && _state.Is(MeleeState.Drawn) && _weapons.meleeWeapon.ready)
        {
            _state.Set(MeleeState.Readied);
            firstReady = true;
        }

        if (cd.Released && _state.Is(MeleeState.Readied))
        {
            _state.Set(MeleeState.Striking);
            _strikeTime = 0f;
            _latencyEstimate = base.TimeManager.RoundTripTime;
            _animation.SetStrikeSpeed(base.IsOwner ? 0f : 1f);
            Debug.Log($"LATENCY ESTIMATE {_latencyEstimate}");

            _weapons.meleeWeapon.SetCooldown();
            strikeCooldown = .5f;
            _weapons.meleeWeapon.canHit = true;
            if (base.IsServer) _weapons.meleeWeapon.canDamage = true;

            _weapons.meleeWeapon.SwingAudioEffect();
            _animation.SetStrike(strikeDir);
            strikeDir = Vector2.zero;
            firstReady = true;
        }
            

        if (!_state.Is(MeleeState.Striking))
            _weapons.meleeWeapon.canHit = _weapons.meleeWeapon.canDamage = false;

        // animator and rigging
        var chestPositionRigComponent = _meleeRig.transform.Find("ChestPosition").GetComponent<TwoBoneIKConstraint>();
        chestPositionRigComponent.weight = Mathf.Lerp(chestPositionRigComponent.weight,
                                                      controlChest ? 1f : _state.Is(MeleeState.Striking) ? 1f : 0f,
                                                      20 * delta);

        _aimLayerTargetWeight = _state.Is(MeleeState.Drawn) ? 0f : 1f;
        SwapMeleeRigs(!_state.Is(MeleeState.Drawn));
        StrikingRigConstraints();

        var animatorState = _animation.GetStateInfo("Melee Weapon");

        // state-specific logic
        switch (_state.GetMeleeState())
        {
            case MeleeState.Readied:
                if (!animatorState.IsName("Ready")) _animation.SetReady();
                UpdateStrikeDir(cd.Look);
                _readyTransition = Mathf.Lerp(_readyTransition, 1f, 15f * delta);
                _animation.SetStrikeDirection(_readyTransition * (_readyTransition <= 0f ? -_prevStrikeDir : strikeDir));
                break;

            case MeleeState.Striking:
                if (base.IsOwner)
                {
                    _strikeTime += delta;
                    float strikeSpeed = Mathf.Min(1f, Mathf.Pow(1000 * _strikeTime / (_latencyEstimate + 25), 2f));
                    _animation.SetStrikeSpeed(strikeSpeed);
                }
                else _animation.SetStrikeSpeed(1f);
                //if (alwaysStrike || strikeDir == Vector2.zero) break;

                //if (base.IsServer) _weapons.meleeWeapon.canHit = true;
                //_weapons.meleeWeapon.SwingAudioEffect();
                //_animation.SetStrike(strikeDir);
                //strikeDir = Vector2.zero;

                break;

            case MeleeState.Blocking:
                if (!animatorState.IsName("Block")) _animation.SetBlock();
                UpdateStrikeDir(cd.Look);
                _readyTransition = Mathf.Lerp(_readyTransition, 1f, 15f * delta);
                _animation.SetStrikeDirection(_readyTransition * (_readyTransition <= 0f ? -_prevStrikeDir : strikeDir));
                break;
        }
    }

    private void EnterMeleeCombat(float delta)
    {
        if (base.IsOwner && _meleeVirtualCamera.m_Priority < 20) _meleeVirtualCamera.m_Priority = 20;
    }

    private void ExitMeleeCombat(float delta)
    {
        SwapMeleeRigs(false);
        if (base.IsOwner && _meleeVirtualCamera.m_Priority > 5) _meleeVirtualCamera.m_Priority = 5;
    }

    private void UpdateStrikeDir(Vector2 lookDirection)
    {
        _prevStrikeDir = strikeDir;
        if (alwaysReady) return;

        // strike from the right be default
        if (lookDirection == Vector2.zero && strikeDir == Vector2.zero)
            strikeDir = Vector2.right;
        float threshold = firstReady ? 1f : 3f;

        // change direction threshold
        if (lookDirection != Vector2.zero && lookDirection.magnitude > threshold && readyCooldown == 0)
        {
            Vector2 lookNormalized = lookDirection.normalized;
            if (Mathf.Abs(lookNormalized.x) > .9 && lookDirection.magnitude > threshold + (firstReady ? 0f : 1f))
                strikeDir = Mathf.Sign(lookDirection.x) * Vector2.right;
            else if (Mathf.Abs(lookNormalized.y) > .9)
                strikeDir = Mathf.Sign(lookDirection.y) * Vector2.up;
            readyCooldown = 1f / 3f;
            firstReady = false;
        }

        // update animator variable
        if (strikeDir != _prevStrikeDir)
            _readyTransition = _prevStrikeDir == Vector2.zero ? 0f : -1f;
    }

    private void SwapMeleeRigs(bool shoot)
    {
        _headRigTargetWeight = shoot ? 0f : 1f;
        _meleeRigTargetWeight = shoot ? 1f : 0f;
    }

    private void StrikingRigConstraints()
    {
        bool strike = _state.Is(MeleeState.Striking);
        //_offhandReadyConstraint.weight = Mathf.Lerp(_offhandReadyConstraint.weight,
        //                                            strike ? 0f : 1f, 20*(float)base.TimeManager.TickDelta);
        //_meleeReadyConstraint.weight = Mathf.Lerp(_meleeReadyConstraint.weight,
        //                                          strike ? 0f : 1f, 20 * (float)base.TimeManager.TickDelta);

        //_offhandStrikeConstraint.weight = Mathf.Lerp(_offhandStrikeConstraint.weight,
        //                                             strike ? 1f : 0f, 20 * (float)base.TimeManager.TickDelta);
        //_meleeStrikeConstraint.weight = Mathf.Lerp(_meleeStrikeConstraint.weight,
        //                                           strike ? 1f : 0f, 20 * (float)base.TimeManager.TickDelta);
        if (strike)
        {
            _offhandReadyConstraint.weight = Mathf.Lerp(_offhandReadyConstraint.weight,
                                                        0f, 20 * (float)base.TimeManager.TickDelta);
            _meleeReadyConstraint.weight = Mathf.Lerp(_meleeReadyConstraint.weight,
                                                      0f, 20 * (float)base.TimeManager.TickDelta);

            _offhandStrikeConstraint.weight = Mathf.Lerp(_offhandStrikeConstraint.weight,
                                                         1f, 20 * (float)base.TimeManager.TickDelta);
            _meleeStrikeConstraint.weight = Mathf.Lerp(_meleeStrikeConstraint.weight,
                                                       1f, 20 * (float)base.TimeManager.TickDelta);
        }
        else
        {
            _offhandReadyConstraint.weight = 1f;
            _meleeReadyConstraint.weight = 1f;

            _offhandStrikeConstraint.weight = Mathf.Lerp(_offhandStrikeConstraint.weight,
                                                         0f, 20 * (float)base.TimeManager.TickDelta);
            _meleeStrikeConstraint.weight = Mathf.Lerp(_meleeStrikeConstraint.weight,
                                                       0f, 20 * (float)base.TimeManager.TickDelta);
        }
    }
    #endregion

    #region BACKUP
    //    #region Properties
    //    // aiming
    //    [Tooltip("Normal sensitivity")]
    //    [SerializeField] private float _normalSensitivity;

    //    [Tooltip("Sensitivity while aiming")]
    //    [SerializeField] private float _aimSensitivity;

    //    [SyncVar]
    //    [Tooltip("Aim target at mouse world position")]
    //    [SerializeField] private Transform _aimTarget;

    //    [Tooltip("Capsule hitmask on own body (to exclude from aim target collisions)")]
    //    [SerializeField] private GameObject _bodyHitMask;

    //    [Tooltip("Capsule hitmask on own head (to exclude from aim target collisions)")]
    //    [SerializeField] private GameObject _headHitMask;

    //    private LayerMask _aimColliderLayerMask = new LayerMask();

    //    //animation rigging
    //    [Tooltip("Animation rig for head rotation")]
    //    [SerializeField] private Rig _headRig;

    //    [Tooltip("Animation rig for gun pointing")]
    //    [SerializeField] private Rig _aimRig;

    //    [Tooltip("Animation rig for gun holding")]
    //    [SerializeField] private Rig _handRig;

    //    [Tooltip("Animation rig for melee weapon position")]
    //    [SerializeField] private Rig _meleeRig;

    //    [Tooltip("Weapon to pivot constraint when readying")]
    //    [SerializeField] private TwoBoneIKConstraint _meleeReadyConstraint;

    //    [Tooltip("Offhand to pivot constraint when readying")]
    //    [SerializeField] private TwoBoneIKConstraint _offhandReadyConstraint;

    //    [Tooltip("Weapon to pivot constraint when striking")]
    //    [SerializeField] private TwoBoneIKConstraint _meleeStrikeConstraint;

    //    [Tooltip("Offhand to pivot constraint when striking")]
    //    [SerializeField] private TwoBoneIKConstraint _offhandStrikeConstraint;

    //    [Tooltip("Melee striking pivots container")]
    //    [SerializeField] private Transform _meleeStrikePivot;

    //    private float _headRigTargetWeight = 1f;
    //    private float _aimRigTargetWeight = 0f;
    //    private float _aimLayerTargetWeight = 0f;
    //    private float _meleeRigTargetWeight = 0f;

    //    public bool alwaysAim = false;
    //    public bool alwaysReady = false;
    //    public bool alwaysStrike = false;
    //    public bool controlChest = false;

    //    // cinemachine
    //    private CinemachineVirtualCamera _aimVirtualCamera;
    //    private CinemachineVirtualCamera _meleeVirtualCamera;

    //    // input
    //#if ENABLE_INPUT_SYSTEM
    //    private PlayerInput _playerInput;
    //#endif
    //    private InputController _input;
    //    private bool _shoot;

    //    // controllers
    //    private PlayerState _state;
    //    private PlayerAnimation _animation;
    //    private PlayerAttributes _attributes;
    //    private PlayerMovement _movement;
    //    private PlayerCameraControl _cameraControl;
    //    private PlayerWeapons _weapons;

    //    // player
    //    [field: SyncVar(Channel = Channel.Unreliable, ReadPermissions = ReadPermission.ExcludeOwner, OnChange = nameof(OnStrafe))]
    //    [HideInInspector] public bool strafe { get; [ServerRpc(RunLocally = true)] set; } = false;
    //    private void OnStrafe(bool prev, bool next, bool asServer)
    //    {
    //        if (!base.IsOwner) SwapRangedRigs(next);
    //    }

    //    public Vector2 strikeDir;
    //    private Vector2 _prevStrikeDir;
    //    private float _readyTransition;
    //    #endregion

    //    #region ClientSidePredicitonProperties
    //    public struct CombatData : IReplicateData
    //    {
    //        public Vector2 Look;
    //        public Quaternion CameraRotation;
    //        public bool Aim;
    //        public bool Shoot;
    //        public Ray ScreenCenterRay;
    //        public CombatData(Vector2 look, Quaternion cameraRotation, bool aim, bool shoot, Ray screenCenterRay)
    //        {
    //            Look = look;
    //            CameraRotation = cameraRotation;
    //            Aim = aim;
    //            Shoot = shoot;
    //            ScreenCenterRay = screenCenterRay;
    //            _tick = 0;
    //        }

    //        private uint _tick;
    //        public void Dispose() { }
    //        public uint GetTick() => _tick;
    //        public void SetTick(uint value) => _tick = value;
    //    };

    //    public struct ReconcileData : IReconcileData
    //    {
    //        public Vector3 AimTargetPosition;
    //        public Vector3 Forward;

    //        public ReconcileData(Vector3 aimTargetPosition, Vector3 forward)
    //        {
    //            AimTargetPosition = aimTargetPosition;
    //            Forward = forward;
    //            _tick = 0;
    //        }

    //        private uint _tick;
    //        public void Dispose() { }
    //        public uint GetTick() => _tick;
    //        public void SetTick(uint value) => _tick = value;
    //    };
    //    #endregion

    //    #region Initialization
    //    private void Awake()
    //    {
    //        _input = GetComponent<InputController>();
    //        _state = GetComponent<PlayerState>();
    //        _animation = GetComponent<PlayerAnimation>();
    //        _attributes = GetComponent<PlayerAttributes>();
    //        _movement = GetComponent<PlayerMovement>();
    //        _cameraControl = GetComponent<PlayerCameraControl>();
    //        _weapons = GetComponent<PlayerWeapons>();

    //        _aimColliderLayerMask = LayerMask.GetMask("Default") | LayerMask.GetMask("PlayerBody")
    //                                                             | LayerMask.GetMask("PlayerHead");
    //    }


    //    public override void OnStartNetwork()
    //    {
    //        base.OnStartNetwork();
    //        base.TimeManager.OnTick += TimeManager_OnTick;
    //        //base.TimeManager.OnUpdate += TimeManager_OnUpdate;
    //    }


    //    public override void OnStopNetwork()
    //    {
    //        base.OnStopNetwork();
    //        base.TimeManager.OnTick -= TimeManager_OnTick;
    //        //base.TimeManager.OnUpdate -= TimeManager_OnUpdate;
    //    }


    //    public override void OnStartClient()
    //    {
    //        base.OnStartClient();
    //        Initialize();
    //    }


    //    private void Initialize()
    //    {
    //        if (_aimVirtualCamera != null) return;

    //        _aimVirtualCamera = GameObject.FindGameObjectWithTag("PlayerAimCamera").GetComponent<CinemachineVirtualCamera>();
    //        if (base.IsOwner) _aimVirtualCamera.Follow = transform.Find("Graphical").transform.Find("PlayerCameraRoot").transform;

    //        _meleeVirtualCamera = GameObject.FindGameObjectWithTag("PlayerMeleeCamera").GetComponent<CinemachineVirtualCamera>();
    //        if (base.IsOwner) _meleeVirtualCamera.Follow = transform.Find("Graphical").transform.Find("PlayerCameraRoot").transform;

    //        _aimTarget.parent.SetParent(null, true);

    //        if (base.IsOwner)
    //        {
    //            _bodyHitMask.layer = LayerMask.NameToLayer("MyPlayerBody");
    //            _headHitMask.layer = LayerMask.NameToLayer("MyPlayerHead");
    //        }
    //    }
    //    #endregion

    //    #region ClientSidePredictionLogic
    //    private void Update()
    //    {
    //        // gather trigger input between network ticks
    //        if (_weapons.rangedWeapon != null)
    //        {
    //            if (_weapons.rangedWeapon.automatic)
    //                _shoot = _input.shoot;
    //            else
    //            {
    //                if (_input.shoot) _shoot = true;
    //                _input.shoot = false;
    //            }
    //        }

    //        if (_weapons.meleeWeapon != null) _shoot = _input.shoot;
    //    }

    //    private void TimeManager_OnTick()
    //    {
    //        CombatData cd = BuildCombatData();
    //        Attack(cd);

    //        //float delta = (float)TimeManager.TickDelta;

    //        //Vector3 mouseWorldPosition = Vector3.zero;
    //        //RaycastHit raycastHit = new RaycastHit();
    //        //if (Physics.Raycast(cd.ScreenCenterRay, out raycastHit, 999f, _aimColliderLayerMask))
    //        //    mouseWorldPosition = raycastHit.point;

    //        //// transition animations
    //        //TransitionRigs(delta);
    //        //_animation.TransitionLayerWeight(2, _aimLayerTargetWeight, delta);

    //        //// ranged logic
    //        //if (_state.Is(RangedState.Inactive, RangedState.Holstered)) ExitRangedCombat(delta);
    //        //else RangedCombat(cd, delta, raycastHit);

    //        //// melee logic
    //        //if (_state.Is(MeleeState.Inactive, MeleeState.Sheathed)) ExitMeleeCombat(delta);
    //        //else MeleeCombat(cd, delta);

    //        if (base.IsServer)
    //        {
    //            ReconcileData rd = new ReconcileData(_aimTarget.position, transform.forward);
    //            Reconciliation(rd);
    //        }
    //    }


    //    [ReconcileV2]
    //    private void Reconciliation(ReconcileData rd, Channel channel = Channel.Unreliable)
    //    {
    //        _aimTarget.position = rd.AimTargetPosition;
    //        transform.forward = rd.Forward;
    //    }


    //    private CombatData BuildCombatData()
    //    {
    //        if (!base.IsOwner) return default;

    //        Vector2 screenCenterPoint = new Vector2(Screen.width / 2f, Screen.height / 2f);

    //        Debug.Log(_cameraControl.CinemachineCameraTarget.transform);

    //        CombatData ad = new CombatData(_input.look, _cameraControl.CinemachineCameraTarget.transform.rotation, _input.aim || alwaysAim,
    //                                       _shoot && _input.aim || _shoot && _weapons.meleeWeapon != null || alwaysReady,
    //                                       Camera.main.ScreenPointToRay(screenCenterPoint));

    //        if (_weapons.rangedWeapon != null && !_weapons.rangedWeapon.automatic) _shoot = false;

    //        return ad;
    //    }


    //    [ReplicateV2]
    //    private void Attack(CombatData ad, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
    //    {
    //        AttackWithData(ad, (float)base.TimeManager.TickDelta, state);
    //    }
    //    #endregion

    //    #region CombatLogic
    //    private void AttackWithData(CombatData ad, float delta, ReplicateState state = ReplicateState.Invalid)
    //    {
    //        bool replaying = (state == ReplicateState.ReplayedUserCreated
    //                           || state == ReplicateState.ReplayedPredicted);

    //        // update mouse aim target in world
    //        Vector3 mouseWorldPosition = Vector3.zero;
    //        RaycastHit raycastHit = new RaycastHit();
    //        if (Physics.Raycast(ad.ScreenCenterRay, out raycastHit, 999f, _aimColliderLayerMask))
    //        {
    //            _aimTarget.position = raycastHit.point;
    //            mouseWorldPosition = raycastHit.point;
    //        }

    //        // align player rotation to camera
    //        if (_state.Is(RangedState.Aiming, RangedState.Shooting)
    //            || _state.Is(MeleeState.Readied, MeleeState.Striking, MeleeState.Blocking))
    //        {
    //            Vector3 worldAimTarget = mouseWorldPosition;
    //            worldAimTarget.y = transform.position.y;
    //            Vector3 aimDirection = (worldAimTarget - transform.position).normalized;
    //            transform.forward = Vector3.Lerp(transform.forward, aimDirection, delta * 20f);
    //        }

    //        //Debug.Log($"{ad.CameraRotation} {_meleeStrikePivot.rotation}");
    //        //_meleeStrikePivot.rotation = ad.CameraRotation;

    //        if (replaying) return;

    //        // transition animations
    //        TransitionRigs(delta);
    //        _animation.TransitionLayerWeight(2, _aimLayerTargetWeight, delta);

    //        // ranged logic
    //        if (_state.Is(RangedState.Inactive, RangedState.Holstered)) ExitRangedCombat(delta);
    //        else RangedCombat(ad, delta, raycastHit);

    //        // melee logic
    //        if (_state.Is(MeleeState.Inactive, MeleeState.Sheathed)) ExitMeleeCombat(delta);
    //        else MeleeCombat(ad, delta);

    //        if (base.IsServer)
    //        {
    //            ReconcileData rd = new ReconcileData(_aimTarget.position, transform.forward);
    //            Reconciliation(rd);
    //        }
    //    }

    //    private void TransitionRigs(float delta)
    //    {
    //        _headRig.weight = Mathf.Lerp(_headRig.weight, _headRigTargetWeight, 25f * delta);
    //        _aimRig.weight = Mathf.Lerp(_aimRig.weight, _aimRigTargetWeight, 25f * delta);
    //        _meleeRig.weight = Mathf.Lerp(_meleeRig.weight, _meleeRigTargetWeight, 25f * delta);
    //    }
    //    #endregion

    //    #region RangedLogic
    //    private void RangedCombat(CombatData ad, float delta, RaycastHit raycastHit)
    //    {
    //        EnterRangedCombat(delta);

    //        if (base.IsOwner) strafe = ad.Aim;
    //        _weapons.rangedWeapon.RecoverTime(delta);

    //        _state.Set(!ad.Aim ? RangedState.Drawn : ad.Shoot ? RangedState.Shooting : RangedState.Aiming);

    //        if (_state.Is(RangedState.Aiming, RangedState.Shooting))
    //        {
    //            if (base.IsOwner) _aimVirtualCamera.m_Priority = 20;
    //            _aimLayerTargetWeight = 1f;
    //            SwapRangedRigs(true);
    //            SetSensitivity(_aimSensitivity);
    //        }
    //        else
    //        {
    //            if (base.IsOwner) _aimVirtualCamera.m_Priority = 5;
    //            _aimLayerTargetWeight = 0f;
    //            SwapRangedRigs(false);
    //            SetSensitivity(_normalSensitivity);
    //        }

    //        if (_state.Is(RangedState.Shooting))
    //            _weapons.rangedWeapon.UpdateFiring(raycastHit, _attributes.size, delta);
    //        else _weapons.rangedWeapon.StopFiring();
    //    }

    //    private void EnterRangedCombat(float delta)
    //    {
    //        if (_handRig.weight < 1f) _handRig.weight = Mathf.Lerp(_handRig.weight, 1f, 25f * delta);
    //    }

    //    private void ExitRangedCombat(float delta)
    //    {
    //        if (_handRig.weight > 0f) _handRig.weight = Mathf.Lerp(_handRig.weight, 0f, 25f * delta);
    //        if (base.IsOwner && _aimVirtualCamera.m_Priority > 5) _aimVirtualCamera.m_Priority = 5;
    //    }

    //    public void SetSensitivity(float newSensitivity) => _cameraControl.Sensitivity = newSensitivity;

    //    private void SwapRangedRigs(bool aim)
    //    {
    //        _headRigTargetWeight = aim ? 0f : 1f;
    //        _aimRigTargetWeight = aim ? 1f : 0f;
    //        _weapons.SetPoseRig(!aim);
    //    }
    //    #endregion

    //    #region MeleeLogic
    //    private void MeleeCombat(CombatData ad, float delta)
    //    {
    //        Debug.Log($"{base.Owner} {ad.Aim} {ad.Shoot}");

    //        //EnterMeleeCombat(delta);
    //        if (base.IsOwner && _meleeVirtualCamera.m_Priority < 20) _meleeVirtualCamera.m_Priority = 20;

    //        if (base.IsOwner) strafe = !_state.Is(MeleeState.Drawn);

    //        if (alwaysStrike) _state.Set(MeleeState.Striking);

    //        if (_state.Is(MeleeState.Drawn))
    //            strikeDir = Vector2.zero;

    //        // input to state
    //        if (!_state.Is(MeleeState.Striking))
    //        {
    //            _weapons.meleeWeapon.canHit = false;
    //            if (ad.Shoot)
    //                _state.Set(MeleeState.Readied);
    //            //else if (_state.Is(MeleeState.Readied))
    //            //{
    //            //    _state.Set(MeleeState.Striking);
    //            //    if (base.IsServer) _weapons.meleeWeapon.canHit = true;
    //            //}
    //            else
    //                _state.Set(MeleeState.Drawn);
    //        }

    //        // animator and rigging
    //        var chestPositionRigComponent = _meleeRig.transform.Find("ChestPosition").GetComponent<TwoBoneIKConstraint>();
    //        chestPositionRigComponent.weight = Mathf.Lerp(chestPositionRigComponent.weight,
    //                                                      controlChest ? 1f : _state.Is(MeleeState.Striking) ? 1f : 0f,
    //                                                      20 * delta);

    //        _aimLayerTargetWeight = _state.Is(MeleeState.Drawn) ? 0f : 1f;
    //        SwapMeleeRigs(!_state.Is(MeleeState.Drawn));
    //        StrikingRigConstraints();

    //        // state-specific logic
    //        switch (_state.GetMeleeState())
    //        {
    //            case MeleeState.Readied:
    //                UpdateStrikeDir(ad.Look);
    //                _readyTransition = Mathf.Lerp(_readyTransition, 1f, 25f * delta);
    //                _animation.SetStrikeDirection(_readyTransition * (_readyTransition <= 0f ? -_prevStrikeDir : strikeDir));
    //                break;

    //            case MeleeState.Striking:
    //                if (alwaysStrike) break;
    //                _animation.SetStrike(strikeDir);
    //                strikeDir = Vector2.zero;
    //                break;

    //            case MeleeState.Blocking:
    //                break;
    //        }
    //    }

    //    //private void EnterMeleeCombat(float delta)
    //    //{
    //    //    if (base.IsOwner && _meleeVirtualCamera.m_Priority < 20) _meleeVirtualCamera.m_Priority = 20;
    //    //}

    //    private void ExitMeleeCombat(float delta)
    //    {
    //        SwapMeleeRigs(false);
    //        if (base.IsOwner && _meleeVirtualCamera.m_Priority > 5) _meleeVirtualCamera.m_Priority = 5;
    //    }

    //    private void UpdateStrikeDir(Vector2 lookDirection)
    //    {
    //        _prevStrikeDir = strikeDir;
    //        if (alwaysReady) return;

    //        // strike from the right be default
    //        if (lookDirection == Vector2.zero && strikeDir == Vector2.zero)
    //            strikeDir = Vector2.right;

    //        // change direction threshold
    //        if (lookDirection != Vector2.zero && lookDirection.magnitude > 2f)
    //        {
    //            Vector2 lookNormalized = lookDirection.normalized;
    //            if (Mathf.Abs(lookNormalized.x) > .9 && lookDirection.magnitude > 3f)
    //                strikeDir = Mathf.Sign(lookDirection.x) * Vector2.right;
    //            else if (Mathf.Abs(lookNormalized.y) > .9 && lookDirection.magnitude > 2f)
    //                strikeDir = Mathf.Sign(lookDirection.y) * Vector2.up;
    //        }

    //        // update animator variable
    //        if (strikeDir != _prevStrikeDir)
    //            _readyTransition = _prevStrikeDir == Vector2.zero ? 0f : -1f;
    //    }

    //    private void SwapMeleeRigs(bool shoot)
    //    {
    //        _headRigTargetWeight = shoot ? 0f : 1f;
    //        _meleeRigTargetWeight = shoot ? 1f : 0f;
    //    }

    //    private void StrikingRigConstraints()
    //    {
    //        bool strike = _state.Is(MeleeState.Striking);
    //        //_offhandReadyConstraint.weight = Mathf.Lerp(_offhandReadyConstraint.weight,
    //        //                                            strike ? 0f : 1f, 20*(float)base.TimeManager.TickDelta);
    //        //_meleeReadyConstraint.weight = Mathf.Lerp(_meleeReadyConstraint.weight,
    //        //                                          strike ? 0f : 1f, 20 * (float)base.TimeManager.TickDelta);

    //        //_offhandStrikeConstraint.weight = Mathf.Lerp(_offhandStrikeConstraint.weight,
    //        //                                             strike ? 1f : 0f, 20 * (float)base.TimeManager.TickDelta);
    //        //_meleeStrikeConstraint.weight = Mathf.Lerp(_meleeStrikeConstraint.weight,
    //        //                                           strike ? 1f : 0f, 20 * (float)base.TimeManager.TickDelta);
    //        if (strike)
    //        {
    //            _offhandReadyConstraint.weight = Mathf.Lerp(_offhandReadyConstraint.weight,
    //                                                        0f, 20 * (float)base.TimeManager.TickDelta);
    //            _meleeReadyConstraint.weight = Mathf.Lerp(_meleeReadyConstraint.weight,
    //                                                      0f, 20 * (float)base.TimeManager.TickDelta);

    //            _offhandStrikeConstraint.weight = Mathf.Lerp(_offhandStrikeConstraint.weight,
    //                                                         1f, 20 * (float)base.TimeManager.TickDelta);
    //            _meleeStrikeConstraint.weight = Mathf.Lerp(_meleeStrikeConstraint.weight,
    //                                                       1f, 20 * (float)base.TimeManager.TickDelta);
    //        }
    //        else
    //        {
    //            _offhandReadyConstraint.weight = 1f;
    //            _meleeReadyConstraint.weight = 1f;

    //            _offhandStrikeConstraint.weight = Mathf.Lerp(_offhandStrikeConstraint.weight,
    //                                                         0f, 20 * (float)base.TimeManager.TickDelta);
    //            _meleeStrikeConstraint.weight = Mathf.Lerp(_meleeStrikeConstraint.weight,
    //                                                       0f, 20 * (float)base.TimeManager.TickDelta);
    //        }
    //    }
    //    #endregion
#endregion
}
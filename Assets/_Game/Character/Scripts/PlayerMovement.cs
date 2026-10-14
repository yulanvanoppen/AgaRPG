using FishNet;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using System.Threading.Tasks;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
#endif

[RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM 
[RequireComponent(typeof(PlayerInput))]
#endif
public class PlayerMovement : NetworkBehaviour
{
    [Header("Player")]
    [Tooltip("Move speed of the character in m/s")]
    public float MoveSpeed = 2.0f;

    [Tooltip("Sprint speed of the character in m/s")]
    public float SprintSpeed = 5.335f;

    [Tooltip("How fast the character turns to face movement direction")]
    [Range(0.0f, 0.3f)]
    public float RotationSmoothTime = 0.12f;

    [Tooltip("Acceleration and deceleration")]
    public float SpeedChangeRate = 10.0f;

    [Space(10)]
    [Tooltip("The height the player can jump")]
    public float JumpHeight = 1.2f;

    [Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
    public float Gravity = -15.0f;

    [Space(10)]
    [Tooltip("Time required to pass before being able to jump again. Set to 0f to instantly jump again")]
    public float JumpTimeout = 0.30f;

    [Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
    public float FallTimeout = 0.15f;

    [Header("Player Grounded")]
    [Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
    public bool Grounded = true;

    [Tooltip("Useful for rough ground")]
    public float GroundedOffset = -0.14f;

    [Tooltip("The radius of the grounded check. Should match the radius of the CharacterController")]
    public float GroundedRadius = 0.28f;

    [Tooltip("What layers the character uses as ground")]
    public LayerMask GroundLayers;


    // player
    public float speed;
    private Vector2 _directionalSpeed;
    private float _animationBlend;
    private float _targetRotation = 0.0f;
    private float _rotationVelocity;
    private float _verticalVelocity;
    private float _terminalVelocity = 53.0f;

    // timeout deltatime
    private float _jumpTimeoutDelta;
    private float _fallTimeoutDelta;

    // input 
#if ENABLE_INPUT_SYSTEM 
    private PlayerInput _playerInput;
#endif
    private CharacterController _controller;
    private InputController _input;
    private bool _jump;

    // animator
    private PlayerAnimation _animation;

    // controller
    private PlayerState _state;
    private PlayerAttributes _attributes;
    private PlayerCameraControl _cameraControl;
    private PlayerCombat _shooting;
    private PlayerWeapons _weapons;


    //MoveData for replication
    private MoveData _lastMoveData = default;

    public struct MoveData : IReplicateData
    {
        public bool AnalogMovement;
        public Vector2 Move;
        public bool Jump;
        public float CameraEulerY;
        public bool Sprint;
        public bool Aim;
        public bool Shoot;

        public MoveData(bool analogMovement, Vector2 move, bool jump, float cameraEulerY, bool sprint, bool aim, bool shoot)
        {
            AnalogMovement = analogMovement;
            Move = move;
            Jump = jump;
            CameraEulerY = cameraEulerY;
            Sprint = sprint;
            Aim = aim;
            Shoot = shoot;

            _tick = 0;
        }

        private uint _tick;
        public void Dispose() { }
        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
    }

    //ReconcileData for Reconciliation
    public struct ReconcileData : IReconcileData
    {
        public Vector3 Position;
        public float TargetRotation;
        public float VerticalVelocity;
        public float FallTimeout;
        public float JumpTimeout;
        public bool Grounded;

        public ReconcileData(Vector3 position, float targetRotation, float verticalVelocity, float fallTimeout,
                             float jumpTimeout, bool grounded)
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


    private void Awake()
    {
        _input = GetComponent<InputController>();
        _controller = GetComponent<CharacterController>();
        _animation = GetComponent<PlayerAnimation>();
        _state = GetComponent<PlayerState>();
        _attributes = GetComponent<PlayerAttributes>();
        _cameraControl = GetComponent<PlayerCameraControl>();
        _shooting = GetComponent<PlayerCombat>();
        _weapons = GetComponent<PlayerWeapons>();

        _jumpTimeoutDelta = JumpTimeout;
        _fallTimeoutDelta = FallTimeout;
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
        Initialize();
    }


    private void Initialize()
    {
        if (base.IsOwner)
        {
            _playerInput = GetComponent<PlayerInput>();
            _playerInput.enabled = true;
        }
    }


    private void Update()
    {
        if (base.IsOwner && _input.jump) _jump = true;
        _input.jump = false;
    }


    private void TimeManager_OnTick()
    {
        Move(BuildMoveData());

        if (base.IsServer)
        {
            ReconcileData rd = new ReconcileData(transform.position, _targetRotation, _verticalVelocity,
                                                 _fallTimeoutDelta, _jumpTimeoutDelta, Grounded);
            Reconciliation(rd);
        }
    }


    [ReconcileV2]
    private void Reconciliation(ReconcileData rd, Channel channel = Channel.Unreliable)
    {
        transform.position = rd.Position;

        _targetRotation = rd.TargetRotation;

        _verticalVelocity = rd.VerticalVelocity;

        _fallTimeoutDelta = rd.FallTimeout;
        _jumpTimeoutDelta = rd.JumpTimeout;
        Grounded = rd.Grounded;
    }


    private MoveData BuildMoveData()
    {
        if (!base.IsOwner) return default;

        bool shoot = _weapons.meleeWeapon != null && _input.shoot;

        MoveData md = new MoveData(_input.analogMovement, _input.move, _jump,
                                   _cameraControl.EulerAngles().y, _input.sprint, _input.aim, shoot);
        _jump = false;

        return md;
    }


    [ReplicateV2]
    private void Move(MoveData md, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
    {
        JumpAndGravity(md, (float)base.TimeManager.TickDelta, state);
        GroundedCheck();
        MoveWithData(md, (float)base.TimeManager.TickDelta, state);
    }


    private void MoveWithData(MoveData md, float delta, ReplicateState state = ReplicateState.Invalid)
    {
        bool replaying = (state == ReplicateState.ReplayedUserCreated
                           || state == ReplicateState.ReplayedPredicted);

        bool combat = md.Aim && _weapons.rangedWeapon != null
                      || _state.Is(MeleeState.Readied, MeleeState.Striking, MeleeState.Blocking);

        float backCorrection = combat && md.Move.y < -.9 && Mathf.Abs(md.Move.x) < .5 ? .7f : 1;
        float targetSpeed = md.Sprint && !combat ? SprintSpeed : MoveSpeed * backCorrection;
        targetSpeed *= _attributes.size;
        if (!_state.Is(MeleeState.Inactive)) targetSpeed *= 1.2f;
        if (md.Move == Vector2.zero || _attributes.dead) targetSpeed = 0.0f;
        speed = targetSpeed;

        float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;
        Vector3 inputDirection = new Vector3(md.Move.x, 0.0f, md.Move.y).normalized;

        if (md.Move != Vector2.zero && !_attributes.dead)
        {
            _targetRotation = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg + md.CameraEulerY;
            float rotation = Mathf.SmoothDampAngle(transform.eulerAngles.y, _targetRotation, ref _rotationVelocity,
                                                   RotationSmoothTime, Mathf.Infinity, delta);

            if (!combat) transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
        }

        Vector3 targetDirection = Quaternion.Euler(0.0f, _targetRotation, 0.0f) * Vector3.forward;

        if (!_attributes.dead && _controller.enabled)
            _controller.Move(targetDirection.normalized * (targetSpeed * delta) +
                             new Vector3(0.0f, _verticalVelocity, 0.0f) * delta);

        //if (!base.IsOwner)
        //{
        //    if (state == ReplicateState.UserCreated || state == ReplicateState.ReplayedUserCreated)
        //        _lastMoveData = md;
        //    else
        //    {
        //        uint tick = md.GetTick();
        //        md = _lastMoveData;
        //        md.Move = Vector2.Lerp(md.Move, Vector2.zero, 50f * delta);
        //        md.SetTick(tick);
        //    }
        //}
        //combat = md.Aim && _weapons.rangedWeapon != null
        //         || _state.Is(MeleeState.Readied, MeleeState.Striking, MeleeState.Blocking);
        //backCorrection = combat && md.Move.y < -.9 && Mathf.Abs(md.Move.x) < .5 ? .7f : 1;
        //targetSpeed = md.Sprint && !combat ? SprintSpeed : MoveSpeed * backCorrection;
        //targetSpeed *= _attributes.size;
        //if (!_state.Is(MeleeState.Inactive)) targetSpeed *= 1.2f;
        //if (md.Move == Vector2.zero || _attributes.dead) targetSpeed = 0.0f;
        //inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

        if (!replaying)
        {
            _animationBlend = Mathf.Lerp(_animationBlend, targetSpeed / _attributes.size, delta * SpeedChangeRate);
            if (_animationBlend < 0.01f) _animationBlend = 0f;

            _directionalSpeed = Vector2.Lerp(_directionalSpeed, md.Move, delta * SpeedChangeRate / MoveSpeed);
            float multiplier = _state.Is(MeleeState.Inactive) ? 1f : 1.2f;

            _animation.SetSpeed(_animationBlend);
            _animation.SetMotionSpeed(inputMagnitude * multiplier);
            _animation.SetForward(_directionalSpeed.x * targetSpeed / _attributes.size);
            _animation.SetStrafe(_directionalSpeed.y * targetSpeed / _attributes.size);
        }
    }


    private void JumpAndGravity(MoveData md, float delta, ReplicateState state)
    {
        bool replaying = (state == ReplicateState.ReplayedUserCreated
                           || state == ReplicateState.ReplayedPredicted);

        if (Grounded)
        {
            _fallTimeoutDelta = FallTimeout;

            if (!replaying)
            {
                _animation.SetJump(false);
                _animation.SetFreeFall(false);
            }

            if (_verticalVelocity < 0.0f) _verticalVelocity = -2f;

            if (md.Jump && !_attributes.dead && _jumpTimeoutDelta <= 0.0f)
            {
                _verticalVelocity = Mathf.Sqrt(JumpHeight * _attributes.size * -2f * Gravity);
                if (!replaying) _animation.SetJump(true);
            }

            if (_jumpTimeoutDelta >= 0.0f) _jumpTimeoutDelta -= delta;
        }
        else
        {
            _jumpTimeoutDelta = JumpTimeout;

            if (_fallTimeoutDelta >= 0.0f) _fallTimeoutDelta -= delta;
            else if (!replaying) _animation.SetFreeFall(true);
        }

        if (_verticalVelocity < _terminalVelocity) _verticalVelocity += Gravity * delta;
    }


    private void GroundedCheck()
    {
        Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset,
            transform.position.z);
        Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers, QueryTriggerInteraction.Ignore);

        _animation.SetGrounded(Grounded);
    }


    private void OnDrawGizmosSelected()
    {
        Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
        Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

        if (Grounded) Gizmos.color = transparentGreen;
        else Gizmos.color = transparentRed;

        Gizmos.DrawSphere(
            new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z),
            GroundedRadius);
    }



    //private void Update()
    //{
    //    if (!base.IsOwner) return;

    //    JumpAndGravity();
    //    GroundedCheck();
    //    Move();
    //}


    //private void Move()
    //{
    //    bool aiming = _input.aim && _weapons.weapon != null;

    //    // set target speed based on move speed, sprint speed and if sprint is pressed
    //    float backCorrection = aiming && Input.GetAxis("Vertical") < -.9
    //                                  && Mathf.Abs(Input.GetAxis("Horizontal")) < .5 ? .7f : 1;
    //    float targetSpeed = _input.sprint && !aiming ? SprintSpeed : MoveSpeed * backCorrection;
    //    targetSpeed *= _state.size;

    //    // a simplistic acceleration and deceleration designed to be easy to remove, replace, or iterate upon

    //    // note: Vector2's == operator uses approximation so is not floating point error prone, and is cheaper than magnitude
    //    // if there is no input, set the target speed to 0
    //    if (_input.move == Vector2.zero || _state.dead) targetSpeed = 0.0f;
        

    //    // a reference to the players current horizontal velocity
    //    float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;

    //    float speedOffset = 0.1f;
    //    float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

    //    // accelerate or decelerate to target speed
    //    if (currentHorizontalSpeed < targetSpeed - speedOffset ||
    //        currentHorizontalSpeed > targetSpeed + speedOffset)
    //    {
    //        // creates curved result rather than a linear one giving a more organic speed change
    //        // note T in Lerp is clamped, so we don't need to clamp our speed
    //        speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude,
    //            Time.deltaTime * SpeedChangeRate);

    //        // round speed to 3 decimal places
    //        speed = Mathf.Round(speed * 1000f) / 1000f;
    //    }
    //    else
    //        speed = targetSpeed;

    //    // normalise input direction
    //    Vector3 inputDirection = new Vector3(_input.move.x, 0.0f, _input.move.y).normalized;

    //    // note: Vector2's != operator uses approximation so is not floating point error prone, and is cheaper than magnitude
    //    // if there is a move input rotate player when the player is moving
    //    if (_input.move != Vector2.zero && !_state.dead)
    //    {
    //        _targetRotation = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg + _cameraControl.EulerAngles().y;
    //        float rotation = Mathf.SmoothDampAngle(transform.eulerAngles.y, _targetRotation, ref _rotationVelocity,
    //            RotationSmoothTime);

    //        // rotate to face input direction relative to camera position
    //        //if (!aiming) 
    //        transform.rotation = Quaternion.Euler(0.0f, _targetRotation, 0.0f);
    //    }


    //    Vector3 targetDirection = Quaternion.Euler(0.0f, _targetRotation, 0.0f) * Vector3.forward;

    //    // move the player
    //    if (!_state.dead)
    //        _controller.Move(targetDirection.normalized * (speed * Time.deltaTime) +
    //                         new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);

    //    // update animator if using character
    //    _animationBlend = Mathf.Lerp(_animationBlend, targetSpeed / _state.size, Time.deltaTime * SpeedChangeRate);
    //    if (_animationBlend < 0.01f) _animationBlend = 0f;

    //    _directionalSpeed = Vector2.Lerp(_directionalSpeed, _input.move, Time.deltaTime * SpeedChangeRate / MoveSpeed);

    //    _animation.SetSpeed(_animationBlend);
    //    _animation.SetMotionSpeed(inputMagnitude);
    //    _animation.SetForward(_directionalSpeed.x * targetSpeed / _state.size);
    //    _animation.SetStrafe(_directionalSpeed.y * targetSpeed / _state.size);
    //}


    //private void JumpAndGravity()
    //{
    //    if (Grounded)
    //    {
    //        // reset the fall timeout timer
    //        _fallTimeoutDelta = FallTimeout;

    //        // update animator if using character
    //        _animation.SetJump(false);
    //        _animation.SetFreeFall(false);

    //        // stop our velocity dropping infinitely when grounded
    //        if (_verticalVelocity < 0.0f)
    //            _verticalVelocity = -2f;

    //        // Jump
    //        if ((_input.jump && !_state.dead) && _jumpTimeoutDelta <= 0.0f)
    //        {
    //            // the square root of H * -2 * G = how much velocity needed to reach desired height
    //            _verticalVelocity = Mathf.Sqrt(JumpHeight * _state.size * -2f * Gravity);

    //            // update animator if using character
    //            _animation.SetJump(true);
    //        }

    //        // jump timeout
    //        if (_jumpTimeoutDelta >= 0.0f)
    //            _jumpTimeoutDelta -= Time.deltaTime;
    //    }
    //    else
    //    {
    //        // reset the jump timeout timer
    //        _jumpTimeoutDelta = JumpTimeout;

    //        // fall timeout
    //        if (_fallTimeoutDelta >= 0.0f)
    //            _fallTimeoutDelta -= Time.deltaTime;
    //        else
    //            // update animator if using character
    //            _animation.SetFreeFall(true);

    //        // if we are not grounded, do not jump
    //        _input.jump = false;
    //    }

    //    // apply gravity over time if under terminal (multiply by delta time twice to linearly speed up over time)
    //    if (_verticalVelocity < _terminalVelocity)
    //        _verticalVelocity += Gravity * Time.deltaTime;
    //}
}
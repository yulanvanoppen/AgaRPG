using Unity.Cinemachine;
using FishNet.Component.Animating;
using FishNet.Object;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/* Note: animations are called via the controller for both the character and capsule using animator null checks
 */

[RequireComponent(typeof(CharacterController))]
public class PlayerAnimation : NetworkBehaviour
{
    public AudioClip LandingAudioClip;
    public AudioClip[] FootstepAudioClips;
    public AudioClip DeathAudioClip;
    [Range(0, 1)] public float FootstepAudioVolume = 0.3f;
    
    // animation IDs
    private int _animIDSpeed;
    private int _animIDGrounded;
    private int _animIDJump;
    private int _animIDFreeFall;
    private int _animIDMotionSpeed;
    private int _animIDStrafe;
    private int _animIDForward;
    private int _animIDDeath;
    private int _animIDRespawn;
    private int _animIDStrikeDirX;
    private int _animIDStrikeDirY;
    private int _animIDStrikeRight;
    private int _animIDStrikeLeft;
    private int _animIDStrikeAbove;
    private int _animIDStrikeBelow;
    private int _animIDStrikeSpeed;
    private int _animIDReady;
    private int _animIDBlock;

    // animator
    private Animator _animator;
    //private NetworkAnimator _networkAnimator;
    [SerializeField] private Animator _rigController;
    [SerializeField] private Animator _meleeController;

    // controllers
    private CharacterController _controller;
    private PlayerCombat _aiming;


    private void Awake()
    {
        _animator = GetComponent<Animator>();
        //_networkAnimator = GetComponent<NetworkAnimator>();
        _controller  = GetComponent<CharacterController>();
        _aiming = GetComponent<PlayerCombat>();
        AssignAnimationIDs();
    }

    private void AssignAnimationIDs()
    {
        _animIDSpeed = Animator.StringToHash("Speed");
        _animIDGrounded = Animator.StringToHash("Grounded");
        _animIDJump = Animator.StringToHash("Jump");
        _animIDFreeFall = Animator.StringToHash("FreeFall");
        _animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
        _animIDStrafe = Animator.StringToHash("Strafe");
        _animIDForward = Animator.StringToHash("Forward");
        _animIDDeath = Animator.StringToHash("Death");
        _animIDRespawn = Animator.StringToHash("Respawn");
        _animIDStrikeDirX = Animator.StringToHash("StrikeDirX");
        _animIDStrikeDirY = Animator.StringToHash("StrikeDirY");
        _animIDStrikeRight = Animator.StringToHash("StrikeRight");
        _animIDStrikeLeft = Animator.StringToHash("StrikeLeft");
        _animIDStrikeAbove = Animator.StringToHash("StrikeAbove");
        _animIDStrikeBelow = Animator.StringToHash("StrikeBelow");
        _animIDStrikeSpeed = Animator.StringToHash("StrikeSpeed");
        _animIDReady = Animator.StringToHash("Ready");
        _animIDBlock = Animator.StringToHash("Block");
    }

    public void SetSpeed(float speed) => _animator.SetFloat(_animIDSpeed, speed);

    public void SetGrounded(bool grounded) =>  _animator.SetBool(_animIDGrounded, grounded);

    public void SetJump(bool jump) => _animator.SetBool(_animIDJump, jump);

    public void SetFreeFall(bool freeFall) => _animator.SetBool(_animIDFreeFall, freeFall);

    public void SetMotionSpeed(float speed) => _animator.SetFloat(_animIDMotionSpeed, speed);

    public void SetForward(float speed) => _animator.SetFloat(_animIDForward, speed);

    public void SetStrafe(float speed) => _animator.SetFloat(_animIDStrafe, speed);

    //public void SetDeath() => _networkAnimator.SetTrigger(_animIDDeath);
    public void SetDeath() => _animator.SetTrigger(_animIDDeath);

    public void SetRespawn(bool respawned) => _animator.SetBool(_animIDRespawn, respawned);

    public void SetStrikeDirection(Vector2 strikeDirection)
    {
        _animator.SetFloat(_animIDStrikeDirX, strikeDirection.x);
        _animator.SetFloat(_animIDStrikeDirY, strikeDirection.y);
    }

    public void SetStrike(Vector2 strikeDirection)
    {
        Debug.Log($"Strike from direction {strikeDirection}");
        if (strikeDirection == Vector2.zero) return;

        int animID = _animIDStrikeRight;
        if (strikeDirection == Vector2.left) animID = _animIDStrikeLeft;
        if (strikeDirection == Vector2.down) animID = _animIDStrikeAbove;
        if (strikeDirection == Vector2.up) animID = _animIDStrikeBelow;

        _meleeController.GetComponent<Animator>().SetTrigger(animID);
    }

    public void SetStrikeSpeed(float speed) => _meleeController.SetFloat(_animIDStrikeSpeed, speed);

    //public void SetReady() => _networkAnimator.SetTrigger(_animIDReady);
    public void SetReady() => _animator.SetTrigger(_animIDReady);


    //public void SetBlock() => _networkAnimator.SetTrigger(_animIDBlock);
    public void SetBlock() => _animator.SetTrigger(_animIDBlock);



    public AnimatorStateInfo GetStateInfo(string layerName) => _animator.GetCurrentAnimatorStateInfo(_animator.GetLayerIndex(layerName));


    public void TransitionLayerWeight(int layer, float weight, float delta) =>
        _animator.SetLayerWeight(layer, Mathf.Lerp(_animator.GetLayerWeight(layer), weight, delta * 25f));

    private void OnFootstep(AnimationEvent animationEvent)
    {
        if (!_aiming.strafe && animationEvent.animatorClipInfo.weight > 0.5f)
            if (FootstepAudioClips.Length > 0)
            {
                var index = Random.Range(0, FootstepAudioClips.Length);
                AudioSource.PlayClipAtPoint(FootstepAudioClips[index], transform.TransformPoint(_controller.center), FootstepAudioVolume);
            }
    }

    private void OnLand(AnimationEvent animationEvent)
    {
        if (!_aiming.strafe && animationEvent.animatorClipInfo.weight > 0.5f)
            AudioSource.PlayClipAtPoint(LandingAudioClip, transform.TransformPoint(_controller.center), FootstepAudioVolume);
    }

    private void OnFootstepAim(AnimationEvent animationEvent)
    {
        if (_aiming.strafe && animationEvent.animatorClipInfo.weight > 0.5f)
            if (FootstepAudioClips.Length > 0)
            {
                var index = Random.Range(0, FootstepAudioClips.Length);
                AudioSource.PlayClipAtPoint(FootstepAudioClips[index], transform.TransformPoint(_controller.center), FootstepAudioVolume);
            }
    }

    private void OnLandAim(AnimationEvent animationEvent)
    {
        if (_aiming.strafe && animationEvent.animatorClipInfo.weight > 0.5f)
            AudioSource.PlayClipAtPoint(LandingAudioClip, transform.TransformPoint(_controller.center), FootstepAudioVolume);
    }


    private void OnDeath(AnimationEvent animationEvent)
    {
        if (animationEvent.animatorClipInfo.weight > 0.5f)
            AudioSource.PlayClipAtPoint(DeathAudioClip, transform.TransformPoint(_controller.center), 1);
    }
}
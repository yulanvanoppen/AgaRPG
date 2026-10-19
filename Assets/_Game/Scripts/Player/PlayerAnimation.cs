using AgaRPG.Combat;
using FishNet.Object;
using UnityEngine;
using UnityEngine.Serialization;

namespace AgaRPG.Player
{
    /// <summary>
    /// Thin wrapper around the character's animators: owns the parameter hashes and plays the
    /// footstep / landing / death sounds raised by animation events.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerAnimation : NetworkBehaviour
    {
        [Header("Audio")]
        [FormerlySerializedAs("LandingAudioClip")]
        [SerializeField] private AudioClip _landingClip;

        [FormerlySerializedAs("FootstepAudioClips")]
        [SerializeField] private AudioClip[] _footstepClips;

        [FormerlySerializedAs("DeathAudioClip")]
        [SerializeField] private AudioClip _deathClip;

        [FormerlySerializedAs("FootstepAudioVolume")]
        [Range(0, 1)]
        [SerializeField] private float _footstepVolume = 0.3f;

        [Header("Animators")]
        [Tooltip("Animator driving the melee weapon strike animations")]
        [SerializeField] private Animator _meleeController;

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int JumpId = Animator.StringToHash("Jump");
        private static readonly int FreeFallId = Animator.StringToHash("FreeFall");
        private static readonly int MotionSpeedId = Animator.StringToHash("MotionSpeed");
        private static readonly int StrafeId = Animator.StringToHash("Strafe");
        private static readonly int ForwardId = Animator.StringToHash("Forward");
        private static readonly int DeathId = Animator.StringToHash("Death");
        private static readonly int RespawnId = Animator.StringToHash("Respawn");
        private static readonly int StrikeDirXId = Animator.StringToHash("StrikeDirX");
        private static readonly int StrikeDirYId = Animator.StringToHash("StrikeDirY");
        private static readonly int StrikeRightId = Animator.StringToHash("StrikeRight");
        private static readonly int StrikeLeftId = Animator.StringToHash("StrikeLeft");
        private static readonly int StrikeAboveId = Animator.StringToHash("StrikeAbove");
        private static readonly int StrikeBelowId = Animator.StringToHash("StrikeBelow");
        private static readonly int StrikeSpeedId = Animator.StringToHash("StrikeSpeed");
        private static readonly int ReadyId = Animator.StringToHash("Ready");
        private static readonly int BlockId = Animator.StringToHash("Block");

        private const float AnimationEventWeightThreshold = 0.5f;
        private const float LayerBlendSpeed = 25f;

        private Animator _animator;
        private CharacterController _controller;
        private PlayerCombat _combat;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _controller = GetComponent<CharacterController>();
            _combat = GetComponent<PlayerCombat>();
        }

        public void SetSpeed(float speed) => _animator.SetFloat(SpeedId, speed);
        public void SetGrounded(bool grounded) => _animator.SetBool(GroundedId, grounded);
        public void SetJump(bool jump) => _animator.SetBool(JumpId, jump);
        public void SetFreeFall(bool freeFall) => _animator.SetBool(FreeFallId, freeFall);
        public void SetMotionSpeed(float speed) => _animator.SetFloat(MotionSpeedId, speed);
        public void SetForward(float speed) => _animator.SetFloat(ForwardId, speed);
        public void SetStrafe(float speed) => _animator.SetFloat(StrafeId, speed);
        public void SetDeath() => _animator.SetTrigger(DeathId);
        public void SetRespawn(bool respawned) => _animator.SetBool(RespawnId, respawned);
        public void SetReady() => _animator.SetTrigger(ReadyId);
        public void SetBlock() => _animator.SetTrigger(BlockId);

        public void SetStrikeDirection(Vector2 strikeDirection)
        {
            _animator.SetFloat(StrikeDirXId, strikeDirection.x);
            _animator.SetFloat(StrikeDirYId, strikeDirection.y);
        }

        /// <summary>Triggers the strike animation that matches a (cardinal) strike direction.</summary>
        public void SetStrike(Vector2 strikeDirection)
        {
            if (strikeDirection == Vector2.zero) return;

            int trigger = StrikeRightId;
            if (strikeDirection == Vector2.left) trigger = StrikeLeftId;
            else if (strikeDirection == Vector2.down) trigger = StrikeAboveId;
            else if (strikeDirection == Vector2.up) trigger = StrikeBelowId;

            _meleeController.SetTrigger(trigger);
        }

        /// <summary>Playback speed of the strike animation, used for latency compensation.</summary>
        public void SetStrikeSpeed(float speed) => _meleeController.SetFloat(StrikeSpeedId, speed);

        public AnimatorStateInfo GetStateInfo(string layerName) =>
            _animator.GetCurrentAnimatorStateInfo(_animator.GetLayerIndex(layerName));

        public void TransitionLayerWeight(int layer, float weight, float delta) =>
            _animator.SetLayerWeight(layer, Mathf.Lerp(_animator.GetLayerWeight(layer), weight, delta * LayerBlendSpeed));

        #region Animation events
        // Each locomotion blend tree exists twice (free and strafing); only the active one plays sounds.
        private void OnFootstep(AnimationEvent animationEvent)
        {
            if (!_combat.IsStrafing) PlayFootstep(animationEvent);
        }

        private void OnFootstepAim(AnimationEvent animationEvent)
        {
            if (_combat.IsStrafing) PlayFootstep(animationEvent);
        }

        private void OnLand(AnimationEvent animationEvent)
        {
            if (!_combat.IsStrafing) PlayAtFeet(animationEvent, _landingClip, _footstepVolume);
        }

        private void OnLandAim(AnimationEvent animationEvent)
        {
            if (_combat.IsStrafing) PlayAtFeet(animationEvent, _landingClip, _footstepVolume);
        }

        private void OnDeath(AnimationEvent animationEvent) => PlayAtFeet(animationEvent, _deathClip, 1f);

        private void PlayFootstep(AnimationEvent animationEvent)
        {
            if (_footstepClips.Length == 0) return;
            PlayAtFeet(animationEvent, _footstepClips[Random.Range(0, _footstepClips.Length)], _footstepVolume);
        }

        private void PlayAtFeet(AnimationEvent animationEvent, AudioClip clip, float volume)
        {
            if (clip == null || animationEvent.animatorClipInfo.weight <= AnimationEventWeightThreshold) return;
            AudioSource.PlayClipAtPoint(clip, transform.TransformPoint(_controller.center), volume);
        }
        #endregion
    }
}

using AgaRPG.Player;
using FishNet.Object;
using UnityEngine;
using UnityEngine.Serialization;

namespace AgaRPG.Combat
{
    /// <summary>
    /// Melee weapon with trigger-based hit detection. Every machine that simulates the strike
    /// detects hits (for immediate feedback), but only the server applies damage.
    /// </summary>
    public class MeleeWeapon : AbstractWeapon
    {
        [Header("Audio")]
        [Tooltip("Weapon swinging audio clips")]
        [SerializeField] private AudioClip[] _swingEffects;

        [Tooltip("Body impact audio clip")]
        [SerializeField] private AudioClip _bodyImpactEffect;

        [Tooltip("Head impact audio clip")]
        [SerializeField] private AudioClip _headImpactEffect;

        [Tooltip("Block impact audio clip")]
        [SerializeField] private AudioClip _blockImpactEffect;

        [Header("Timing")]
        [Tooltip("Strikes per second")]
        [FormerlySerializedAs("strikeRate")]
        [SerializeField] private float _strikeRate = 1f;

        private const string HeadTag = "PlayerHead";
        private const string BodyTag = "PlayerBody";
        private const string WeaponTag = "MeleeWeapon";
        private const float BlockedDamageMultiplier = 0.1f;

        private enum Sound { Swing, BodyImpact, HeadImpact, BlockImpact }

        private float _cooldown;

        /// <summary>The weapon is mid-swing and can register a hit.</summary>
        public bool CanHit { get; set; }

        /// <summary>A registered hit applies damage. Only ever true on the server.</summary>
        public bool CanDamage { get; set; }

        /// <summary>The wielder is blocking: hits on this weapon are absorbed.</summary>
        public bool CanBlock { get; set; }

        /// <summary>True when the strike cooldown has elapsed.</summary>
        public bool IsReady => _cooldown <= 0f;

        public void Initialize() => _cooldown = 0f;

        public void StartCooldown() => _cooldown = 1f / _strikeRate;

        public void RecoverTime(float delta)
        {
            if (_cooldown > 0f) _cooldown = Mathf.Max(0f, _cooldown - delta);
        }

        public void PlaySwingSound() => PlaySound(Sound.Swing, 0.3f);

        private void OnTriggerEnter(Collider other)
        {
            if (!CanHit) return;

            bool isHead = other.CompareTag(HeadTag);
            bool isBody = other.CompareTag(BodyTag);
            bool isBlock = other.CompareTag(WeaponTag) && other.TryGetComponent(out MeleeWeapon otherWeapon) && otherWeapon.CanBlock;
            if (!isHead && !isBody && !isBlock) return;

            if (!other.transform.root.TryGetComponent(out PlayerAttributes target)
                || !transform.root.TryGetComponent(out PlayerAttributes wielder)
                || target.Owner == base.Owner)
                return;   // not a player, or our own body

            float damage = BaseDamage * wielder.Size;
            if (isHead)
            {
                damage *= HeadshotMultiplier;
                PlaySound(Sound.HeadImpact);
            }
            else if (isBlock)
            {
                damage *= BlockedDamageMultiplier;
                PlaySound(Sound.BlockImpact, 0.45f);
            }
            else PlaySound(Sound.BodyImpact);

            if (CanDamage) target.TakeDamage(damage / target.Size, base.Owner);

            // One hit per swing.
            CanHit = CanDamage = false;
        }

        /// <summary>The owner hears its own sounds immediately; the server relays them to everyone else.</summary>
        private void PlaySound(Sound sound, float volume = 0.6f)
        {
            if (base.IsOwner) PlayClip(sound, volume);
            if (base.IsServerInitialized) ObserversRpcPlaySound(sound, volume);
        }

        [ObserversRpc(ExcludeOwner = true)]
        private void ObserversRpcPlaySound(Sound sound, float volume) => PlayClip(sound, volume);

        private void PlayClip(Sound sound, float volume)
        {
            AudioClip clip = sound switch
            {
                Sound.Swing => _swingEffects.Length > 0 ? _swingEffects[Random.Range(0, _swingEffects.Length)] : null,
                Sound.BodyImpact => _bodyImpactEffect,
                Sound.HeadImpact => _headImpactEffect,
                Sound.BlockImpact => _blockImpactEffect,
                _ => null
            };
            if (clip != null) AudioSource.PlayClipAtPoint(clip, transform.position, volume);
        }
    }
}

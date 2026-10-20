using AgaRPG.Player;
using FishNet.Object;
using UnityEngine;
using UnityEngine.Serialization;

namespace AgaRPG.Combat
{
    /// <summary>
    /// Melee weapon with swept (continuous) hit detection. Every machine that simulates the strike
    /// detects hits for immediate feedback, but only the server applies damage.
    /// </summary>
    /// <remarks>
    /// Trigger colliders only test the blade where it is in each physics step, so a fast swing or a
    /// frame hitch lets the blade "teleport" through a target. Instead, every frame the blade is swept
    /// from its previous pose to its current pose: the pose is interpolated (position lerp, rotation
    /// slerp, so the tip follows an arc rather than a chord) in as many substeps as needed to keep each
    /// step short, and a sphere is cast for every sample point along the blade between consecutive
    /// substeps. The earliest hit along the swing wins.
    /// </remarks>
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

        [Header("Swept hit detection")]
        [Tooltip("Capsule describing the blade. Defaults to the first non-trigger capsule on the weapon.")]
        [SerializeField] private CapsuleCollider _bladeCollider;

        [Tooltip("Extra radius added to the blade capsule radius for the sweep")]
        [SerializeField] private float _sweepRadiusPadding = 0.02f;

        [Tooltip("Longest distance (m) any blade point may travel in one substep")]
        [SerializeField] private float _maxStepLength = 0.05f;

        [Tooltip("Upper bound of substeps per frame, to cap the cost of very long frames")]
        [SerializeField] private int _maxSubsteps = 24;

        [Tooltip("Layers that can be hit: player hitboxes and other weapons (for blocking). " +
                 "Leave empty for PlayerBody, PlayerHead and MeleeWeapon.")]
        [SerializeField] private LayerMask _hitLayers;

        [Tooltip("Draw the swept blade in the Scene view")]
        [SerializeField] private bool _debugDraw;

        private const string HeadTag = "PlayerHead";
        private const string BodyTag = "PlayerBody";
        private const string WeaponTag = "MeleeWeapon";
        private const float BlockedDamageMultiplier = 0.1f;
        private const int MaxHitsPerCast = 16;

        private enum Sound { Swing, BodyImpact, HeadImpact, BlockImpact }

        private static readonly RaycastHit[] HitBuffer = new RaycastHit[MaxHitsPerCast];
        private static readonly Collider[] OverlapBuffer = new Collider[MaxHitsPerCast];

        private float _cooldown;

        // Blade sample points in the weapon's local space (scale applied at runtime).
        private Vector3[] _bladePoints;
        private float _bladeRadius;

        private bool _hasPreviousPose;
        private Vector3 _previousPosition;
        private Quaternion _previousRotation;

        /// <summary>The weapon is mid-swing and can register a hit.</summary>
        public bool CanHit { get; set; }

        /// <summary>A registered hit applies damage. Only ever true on the server.</summary>
        public bool CanDamage { get; set; }

        /// <summary>The wielder is blocking: hits on this weapon are absorbed.</summary>
        public bool CanBlock { get; set; }

        /// <summary>True when the strike cooldown has elapsed.</summary>
        public bool IsReady => _cooldown <= 0f;

        private void Awake()
        {
            if (_hitLayers.value == 0) _hitLayers = LayerMask.GetMask("PlayerBody", "PlayerHead", "MeleeWeapon");
            BuildBladeSamples();
        }

        public void Initialize() => _cooldown = 0f;

        public void StartCooldown() => _cooldown = 1f / _strikeRate;

        public void RecoverTime(float delta)
        {
            if (_cooldown > 0f) _cooldown = Mathf.Max(0f, _cooldown - delta);
        }

        public void PlaySwingSound() => PlaySound(Sound.Swing, 0.3f);

        #region Swept hit detection
        /// <summary>Samples the blade capsule into points spaced at most one radius apart.</summary>
        private void BuildBladeSamples()
        {
            if (_bladeCollider == null)
            {
                foreach (CapsuleCollider capsule in GetComponentsInChildren<CapsuleCollider>(true))
                {
                    if (capsule.isTrigger) continue;
                    _bladeCollider = capsule;
                    break;
                }
            }

            if (_bladeCollider == null)
            {
                Debug.LogError($"{name}: {nameof(MeleeWeapon)} needs a {nameof(CapsuleCollider)} describing the blade.", this);
                _bladePoints = new[] { Vector3.zero };
                _bladeRadius = 0.05f;
                return;
            }

            Vector3 axis = _bladeCollider.direction switch { 0 => Vector3.right, 1 => Vector3.up, _ => Vector3.forward };
            float halfLength = Mathf.Max(0f, _bladeCollider.height * 0.5f - _bladeCollider.radius);
            Transform capsuleTransform = _bladeCollider.transform;

            // Endpoints of the capsule's inner segment, expressed in this weapon's local space.
            Vector3 start = transform.InverseTransformPoint(capsuleTransform.TransformPoint(_bladeCollider.center - axis * halfLength));
            Vector3 end = transform.InverseTransformPoint(capsuleTransform.TransformPoint(_bladeCollider.center + axis * halfLength));

            // Like Unity, scale the capsule radius by the larger of the two axes perpendicular to its direction,
            // then store it relative to this weapon's scale (re-applied at runtime, e.g. when the player grows).
            Vector3 scale = capsuleTransform.lossyScale;
            float radiusScale = _bladeCollider.direction switch
            {
                0 => Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)),
                1 => Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)),
                _ => Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y)),
            };
            _bladeRadius = (_bladeCollider.radius * radiusScale + _sweepRadiusPadding) / Mathf.Max(0.0001f, MaxAbs(transform.lossyScale));

            // Spheres closer than their radius overlap, so the blade is covered without gaps.
            float length = Vector3.Scale(end - start, transform.lossyScale).magnitude;
            int count = Mathf.Max(2, Mathf.CeilToInt(length / Mathf.Max(0.01f, _bladeRadius)) + 1);
            _bladePoints = new Vector3[count];
            for (int i = 0; i < count; i++) _bladePoints[i] = Vector3.Lerp(start, end, i / (count - 1f));
        }

        private void LateUpdate()
        {
            // Runs after the Animator and Animation Rigging have posed the blade for this frame.
            if (!CanHit)
            {
                _hasPreviousPose = false;
                return;
            }

            Vector3 position = transform.position;
            Quaternion rotation = transform.rotation;
            Vector3 scale = transform.lossyScale;
            float radius = _bladeRadius * MaxAbs(scale);

            // Animation moved the hitboxes outside the physics step; make queries see this frame's poses.
            Physics.SyncTransforms();

            if (!_hasPreviousPose)
            {
                // First frame of the swing: anything the blade already touches counts.
                _hasPreviousPose = true;
                _previousPosition = position;
                _previousRotation = rotation;
                TryHitOverlapping(position, rotation, scale, radius);
                return;
            }

            SweepBlade(position, rotation, scale, radius);

            _previousPosition = position;
            _previousRotation = rotation;
        }

        private void SweepBlade(Vector3 position, Quaternion rotation, Vector3 scale, float radius)
        {
            // Substeps from the largest distance any blade point travelled this frame.
            float maxTravel = 0f;
            foreach (Vector3 local in _bladePoints)
            {
                float travel = Vector3.Distance(BladePoint(_previousPosition, _previousRotation, scale, local),
                                                BladePoint(position, rotation, scale, local));
                maxTravel = Mathf.Max(maxTravel, travel);
            }
            if (maxTravel < 0.0001f) return;

            int substeps = Mathf.Clamp(Mathf.CeilToInt(maxTravel / Mathf.Max(0.001f, _maxStepLength)), 1, _maxSubsteps);

            for (int step = 1; step <= substeps && CanHit; step++)
            {
                float t0 = (step - 1f) / substeps;
                float t1 = (float)step / substeps;
                Vector3 fromPosition = Vector3.Lerp(_previousPosition, position, t0);
                Vector3 toPosition = Vector3.Lerp(_previousPosition, position, t1);
                Quaternion fromRotation = Quaternion.Slerp(_previousRotation, rotation, t0);
                Quaternion toRotation = Quaternion.Slerp(_previousRotation, rotation, t1);

                // Earliest hit within this substep (smallest cast distance over all blade points).
                Collider best = null;
                float bestDistance = float.MaxValue;

                foreach (Vector3 local in _bladePoints)
                {
                    Vector3 from = BladePoint(fromPosition, fromRotation, scale, local);
                    Vector3 to = BladePoint(toPosition, toRotation, scale, local);
                    Vector3 delta = to - from;
                    float distance = delta.magnitude;
                    if (distance < 0.0001f) continue;

                    if (_debugDraw) Debug.DrawLine(from, to, Color.red, 1f);

                    int count = Physics.SphereCastNonAlloc(from, radius, delta / distance, HitBuffer, distance,
                                                           _hitLayers, QueryTriggerInteraction.Collide);
                    for (int i = 0; i < count; i++)
                    {
                        RaycastHit hit = HitBuffer[i];
                        // distance 0 = already overlapping at the cast start; that was tested in the previous substep.
                        if (hit.distance <= 0f || hit.distance >= bestDistance || !IsValidTarget(hit.collider)) continue;
                        best = hit.collider;
                        bestDistance = hit.distance;
                    }
                }

                if (best != null) RegisterHit(best);
            }
        }

        private void TryHitOverlapping(Vector3 position, Quaternion rotation, Vector3 scale, float radius)
        {
            foreach (Vector3 local in _bladePoints)
            {
                int count = Physics.OverlapSphereNonAlloc(BladePoint(position, rotation, scale, local), radius,
                                                          OverlapBuffer, _hitLayers, QueryTriggerInteraction.Collide);
                for (int i = 0; i < count; i++)
                {
                    if (!IsValidTarget(OverlapBuffer[i])) continue;
                    RegisterHit(OverlapBuffer[i]);
                    return;
                }
            }
        }

        private static Vector3 BladePoint(Vector3 position, Quaternion rotation, Vector3 scale, Vector3 local) =>
            position + rotation * Vector3.Scale(local, scale);

        private static float MaxAbs(Vector3 v) => Mathf.Max(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        /// <summary>A hitbox of another player, or another player's blocking weapon.</summary>
        private bool IsValidTarget(Collider other)
        {
            if (other.transform.root == transform.root) return false;   // our own body or weapon

            if (other.CompareTag(HeadTag) || other.CompareTag(BodyTag)) return true;
            return other.CompareTag(WeaponTag) && other.GetComponentInParent<MeleeWeapon>() is { CanBlock: true };
        }

        private void RegisterHit(Collider other)
        {
            if (!other.transform.root.TryGetComponent(out PlayerAttributes target)
                || !transform.root.TryGetComponent(out PlayerAttributes wielder)
                || target.Owner == base.Owner)
                return;

            bool isHead = other.CompareTag(HeadTag);
            bool isBlock = other.CompareTag(WeaponTag);

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

        private void OnDrawGizmosSelected()
        {
            if (_bladePoints == null || _bladePoints.Length == 0) BuildBladeSamples();

            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.6f);
            float radius = _bladeRadius * MaxAbs(transform.lossyScale);
            foreach (Vector3 local in _bladePoints)
                Gizmos.DrawWireSphere(BladePoint(transform.position, transform.rotation, transform.lossyScale, local), radius);
        }
        #endregion

        #region Audio
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
        #endregion
    }
}

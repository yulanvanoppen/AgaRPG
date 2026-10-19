using AgaRPG.Player;
using FishNet.Object;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor.Animations;
#endif

namespace AgaRPG.Combat
{
    /// <summary>
    /// Owns the weapon a player holds (one at a time) and the rigging / animator setup for it.
    /// Equipping is server authoritative: the server spawns the weapon with the player as owner
    /// and tells every client which weapon to attach through a buffered RPC, so late joiners
    /// see the correct weapon too.
    /// </summary>
    public class PlayerWeapons : NetworkBehaviour
    {
        [Header("Melee")]
        [Tooltip("Hand bone the melee weapon is parented to")]
        [FormerlySerializedAs("meleeWeaponHandPivot")]
        [SerializeField] private Transform _meleeWeaponHandPivot;

        [Header("Rigging")]
        [Tooltip("Animation rig for ranged weapon posing")]
        [SerializeField] private Rig _poseRig;

#pragma warning disable CS0169, CS0649 // only used by the editor-only context menus below
        [Header("Hand pose authoring (editor context menus)")]
        [FormerlySerializedAs("leftGrip")]
        [SerializeField] private Transform _leftGrip;
        [FormerlySerializedAs("rightGrip")]
        [SerializeField] private Transform _rightGrip;
        [FormerlySerializedAs("leftGripHint")]
        [SerializeField] private Transform _leftGripHint;
        [FormerlySerializedAs("rightGripHint")]
        [SerializeField] private Transform _rightGripHint;
        [FormerlySerializedAs("meleeWeaponPivot")]
        [SerializeField] private Transform _meleeWeaponPivot;
        [FormerlySerializedAs("meleeWeaponHint")]
        [SerializeField] private Transform _meleeWeaponHint;
        [FormerlySerializedAs("leftHandPivot")]
        [SerializeField] private Transform _leftHandPivot;
        [FormerlySerializedAs("leftHandHint")]
        [SerializeField] private Transform _leftHandHint;
        [FormerlySerializedAs("meleeWeaponPositionClip")]
        [SerializeField] private AnimationClip _meleeWeaponPositionClip;
#pragma warning restore CS0169, CS0649

        private const string GripLayerName = "Weapon Gripping";
        private const string GripOverrideClip = "EmptyGrip";

        private Transform _rangedWeaponPivot;
        private Animator _animator;
        private Animator _rigLayerAnimator;
        private AnimatorOverrideController _animatorOverride;
        private PlayerState _state;

        public RaycastWeapon RangedWeapon { get; private set; }
        public MeleeWeapon MeleeWeapon { get; private set; }

        private void Awake()
        {
            Transform graphical = transform.Find("Graphical");
            _rangedWeaponPivot = graphical.Find("Weapons/RangedWeaponHolder/WeaponPivot");
            _rigLayerAnimator = graphical.Find("RigLayer").GetComponent<Animator>();
            _animator = GetComponent<Animator>();
            _animatorOverride = _animator.runtimeAnimatorController as AnimatorOverrideController;
            _state = GetComponent<PlayerState>();

            // Support a weapon placed on the prefab for testing.
            RaycastWeapon existingWeapon = GetComponentInChildren<RaycastWeapon>();
            if (existingWeapon != null) Attach(existingWeapon);
        }

        public void SetPoseRig(bool on) => _poseRig.weight = RangedWeapon != null && on ? 1f : 0f;

        #region Networked equip (server)
        /// <summary>
        /// Replaces the current weapon with a new instance of <paramref name="weaponPrefab"/>,
        /// or just unequips when it is null. Server only.
        /// </summary>
        [Server]
        public void ServerEquip(AbstractWeapon weaponPrefab)
        {
            // Explicit Unity null checks: '??' would not treat destroyed objects as null.
            AbstractWeapon previous = RangedWeapon != null ? RangedWeapon : MeleeWeapon != null ? MeleeWeapon : null;

            AbstractWeapon weapon = null;
            if (weaponPrefab != null)
            {
                weapon = Instantiate(weaponPrefab);
                base.Spawn(weapon.gameObject, base.Owner);
            }

            SetWeapon(weapon);
            ObserversRpcSetWeapon(weapon);

            if (previous != null && previous.IsSpawned) previous.Despawn();
        }

        // ExcludeServer: the server already applied it in ServerEquip (this also covers a host's client).
        [ObserversRpc(BufferLast = true, ExcludeServer = true)]
        private void ObserversRpcSetWeapon(AbstractWeapon weapon) => SetWeapon(weapon);
        #endregion

        #region Local attach / detach
        private void SetWeapon(AbstractWeapon weapon)
        {
            Detach();
            if (weapon != null) Attach(weapon);
        }

        private void Attach(AbstractWeapon weapon)
        {
            switch (weapon)
            {
                case RaycastWeapon ranged:
                    RangedWeapon = ranged;
                    ranged.transform.SetParent(_rangedWeaponPivot, false);
                    ranged.Recoil.RigController = _rigLayerAnimator;
                    ranged.Initialize();

                    _state.Set(RangedState.Drawn);
                    _poseRig.weight = 1f;
                    _animator.SetLayerWeight(_animator.GetLayerIndex(GripLayerName), 1f);

                    // The override must be applied after the animator has bound the new hierarchy.
                    Invoke(nameof(ApplyGripOverride), 0.001f);
                    break;

                case MeleeWeapon melee:
                    MeleeWeapon = melee;
                    melee.transform.SetParent(_meleeWeaponHandPivot, false);
                    melee.Initialize();
                    _state.Set(MeleeState.Drawn);
                    break;
            }
        }

        private void Detach()
        {
            if (!_state.Is(RangedState.Inactive))
            {
                CancelInvoke(nameof(ApplyGripOverride));
                _poseRig.weight = 0f;
                _animator.SetLayerWeight(_animator.GetLayerIndex(GripLayerName), 0f);
                _animatorOverride[GripOverrideClip] = null;
                _state.Set(RangedState.Inactive);
            }

            if (!_state.Is(MeleeState.Inactive)) _state.Set(MeleeState.Inactive);

            // The server despawns the objects; locally we only drop the references.
            if (RangedWeapon != null) RangedWeapon.transform.SetParent(null, true);
            if (MeleeWeapon != null) MeleeWeapon.transform.SetParent(null, true);
            RangedWeapon = null;
            MeleeWeapon = null;
        }

        private void ApplyGripOverride()
        {
            if (RangedWeapon != null) _animatorOverride[GripOverrideClip] = RangedWeapon.HandPositionClip;
        }
        #endregion

#if UNITY_EDITOR
        [ContextMenu("Save hand positions")]
        private void SaveHandPositions()
        {
            var recorder = new GameObjectRecorder(gameObject);
            recorder.BindComponentsOfType<Transform>(_rangedWeaponPivot.gameObject, false);
            recorder.BindComponentsOfType<Transform>(_leftGrip.gameObject, false);
            recorder.BindComponentsOfType<Transform>(_rightGrip.gameObject, false);
            recorder.BindComponentsOfType<Transform>(_leftGripHint.gameObject, false);
            recorder.BindComponentsOfType<Transform>(_rightGripHint.gameObject, false);
            recorder.TakeSnapshot(0f);
            recorder.SaveToClip(RangedWeapon.HandPositionClip);
            UnityEditor.AssetDatabase.SaveAssets();
        }

        [ContextMenu("Save melee weapon position")]
        private void SaveWeaponPosition()
        {
            var recorder = new GameObjectRecorder(gameObject);
            recorder.BindComponentsOfType<Transform>(_meleeWeaponPivot.gameObject, false);
            recorder.BindComponentsOfType<Transform>(_meleeWeaponHint.gameObject, false);
            recorder.BindComponentsOfType<Transform>(_leftHandPivot.gameObject, false);
            recorder.BindComponentsOfType<Transform>(_leftHandHint.gameObject, false);
            recorder.TakeSnapshot(0f);
            recorder.SaveToClip(_meleeWeaponPositionClip);
            UnityEditor.AssetDatabase.SaveAssets();
        }
#endif
    }
}

using Cinemachine;
using FishNet;
using FishNet.Component.Animating;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using Unity.VisualScripting;
#if UNITY_EDITOR
using UnityEditor.Animations;
#endif
using UnityEngine;
using UnityEngine.Animations.Rigging;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
#endif



public class PlayerWeapons : NetworkBehaviour
{
    // weapon
    public RaycastWeapon rangedWeapon;
    public MeleeWeapon meleeWeapon;
    private Transform _weaponPivot;
    public Transform meleeWeaponHandPivot;
    public Transform meleeWeaponPivot;
    public Transform meleeWeaponHint;
    public Transform leftHandPivot;
    public Transform leftHandHint;
    public Transform chestPivot;
    public Transform chestHint;

    // animation rigging
    [Tooltip("Animation rig for gun posing")]
    [SerializeField] private Rig _poseRig;    

    [Tooltip("Gripping position transforms")]
    public Transform leftGrip;
    public Transform rightGrip;
    public Transform leftGripHint;
    public Transform rightGripHint;

    public AnimationClip meleeWeaponPositionClip;

    private Animator _animator;
    private AnimatorOverrideController _animatorOverride;

    // player
    private PlayerState _state;



    private void Awake()
    {
        RaycastWeapon existingWeapon = GetComponentInChildren<RaycastWeapon>();
        if (existingWeapon != null) Equip(existingWeapon);
        _weaponPivot = transform.Find("Graphical").Find("Weapons").Find("RangedWeaponHolder").Find("WeaponPivot");
        _animator = GetComponent<Animator>();
        _animatorOverride = _animator.runtimeAnimatorController as AnimatorOverrideController;
        _state = GetComponent<PlayerState>();
    }


    public void Equip(AbstractWeapon newWeapon)
    {
        if (newWeapon == null) return;

        if (newWeapon is RaycastWeapon)
        {
            rangedWeapon = (RaycastWeapon)newWeapon;
            rangedWeapon.transform.SetParent(_weaponPivot, false);
            rangedWeapon.Initialize();

            _state.Set(RangedState.Drawn);

            _poseRig.weight = 1f;
            _animator.SetLayerWeight(_animator.GetLayerIndex("Weapon Gripping"), 1f);

            rangedWeapon.weaponRecoil.rigController = transform.Find("Graphical")
                                                         .Find("RigLayer")
                                                         .GetComponent<Animator>();
                                                         //.GetComponent<NetworkAnimator>();
            Invoke(nameof(SetAnimationDelayed), .001f);
        }

        if (newWeapon is MeleeWeapon)
        {
            meleeWeapon = (MeleeWeapon)newWeapon;
            meleeWeapon.transform.SetParent(meleeWeaponHandPivot, false);
            meleeWeapon.Initialize();

            _state.Set(MeleeState.Drawn);
        }
    }


    [ServerRpc(RunLocally = true)]
    public void RpcEquip(AbstractWeapon newWeapon)
    {
        Equip(newWeapon);
        if (base.IsServer) ObsRpcEquip(newWeapon);
    }

    [ObserversRpc(ExcludeOwner = true, BufferLast = true)]
    private void ObsRpcEquip(AbstractWeapon newWeapon) => Equip(newWeapon);


    public void Unequip()
    {
        if (!_state.Is(RangedState.Inactive))
        {
            _poseRig.weight = 0f;
            _animator.SetLayerWeight(_animator.GetLayerIndex("Weapon Gripping"), 0f);
            _animatorOverride["EmptyGrip"] = null;

            if (base.IsOwner && rangedWeapon != null)
            {
                rangedWeapon.transform.SetParent(null, true);
                rangedWeapon.Despawn();
            }

            _state.Set(RangedState.Inactive);
        }
        
        if (!_state.Is(MeleeState.Inactive))
        {
            if (base.IsOwner && meleeWeapon != null)
            {
                meleeWeapon.transform.SetParent(null, true);
                meleeWeapon.Despawn();
            }
            _state.Set(MeleeState.Inactive);
        }
    }

    [ServerRpc(RunLocally = true)]
    public void RpcUnequip()
    {
        Unequip();
        if (base.IsServer) ObsRpcUnequip();
    }

    [ObserversRpc(ExcludeOwner = true, BufferLast = true)]
    private void ObsRpcUnequip() => Unequip();


    private void SetAnimationDelayed() => _animatorOverride["EmptyGrip"] = rangedWeapon.handPositionClip;


    public void SetPoseRig(bool on) => _poseRig.weight = rangedWeapon != null && on ? 1f : 0f;


    //public void RequestWeapon(RaycastWeapon weaponPrefab)
    //{
    //    if (!base.IsOwner) return;
    //    RpcRequestWeapon(weaponPrefab);
    //}


    //[ServerRpc]
    //private void RpcRequestWeapon(RaycastWeapon weaponPrefab)
    //{
    //    RaycastWeapon newWeapon = Instantiate(weaponPrefab,
    //                                          transform.Find("Graphical")
    //                                                   .Find("WeaponHolder")
    //                                                   .Find("WeaponPivot"));
    //    InstanceFinder.ServerManager.Spawn(newWeapon.gameObject, base.Owner);
    //    Equip(newWeapon);
    //}

#if UNITY_EDITOR
    [ContextMenu("Save hand positions")]
    void SaveHandPositions()
    {
        GameObjectRecorder recorder = new GameObjectRecorder(gameObject);
        recorder.BindComponentsOfType<Transform>(_weaponPivot.gameObject, false);
        recorder.BindComponentsOfType<Transform>(leftGrip.gameObject, false);
        recorder.BindComponentsOfType<Transform>(rightGrip.gameObject, false);
        recorder.BindComponentsOfType<Transform>(leftGripHint.gameObject, false);
        recorder.BindComponentsOfType<Transform>(rightGripHint.gameObject, false);
        recorder.TakeSnapshot(0f);
        recorder.SaveToClip(rangedWeapon.handPositionClip);
        UnityEditor.AssetDatabase.SaveAssets();
    }

    [ContextMenu("Save melee weapon position")]
    void SaveWeaponPosition()
    {
        GameObjectRecorder recorder = new GameObjectRecorder(gameObject);
        recorder.BindComponentsOfType<Transform>(meleeWeaponPivot.gameObject, false);
        recorder.BindComponentsOfType<Transform>(meleeWeaponHint.gameObject, false);
        recorder.BindComponentsOfType<Transform>(leftHandPivot.gameObject, false);
        recorder.BindComponentsOfType<Transform>(leftHandHint.gameObject, false);
        //recorder.BindComponentsOfType<Transform>(chestPivot.gameObject, false);
        //recorder.BindComponentsOfType<Transform>(chestHint.gameObject, false);
        recorder.TakeSnapshot(0f);
        recorder.SaveToClip(meleeWeaponPositionClip);
        UnityEditor.AssetDatabase.SaveAssets();
    }
#endif
}
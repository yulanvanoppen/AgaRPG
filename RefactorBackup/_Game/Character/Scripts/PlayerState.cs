using FishNet.Object;
using System.Linq;

public enum DeathState { Respawning, Alive, Dying, Dead }
public enum RangedState { Inactive, Holstered, Drawn, Aiming, Shooting, Reloading };
public enum MeleeState { Inactive, Sheathed, Drawn, Readied, Striking, Blocking };
public enum MoveState { Still, Walking, Sprinting };
public enum JumpState { Grounded, Airborne }


public class PlayerState : NetworkBehaviour
{
    public DeathState _deathState;
    public RangedState _rangedState;
    public MeleeState _meleeState;
    public MoveState _moveState;
    public JumpState _jumpState;

    private PlayerAttributes _attributes;
    private PlayerWeapons _weapons;

    public void Set(DeathState state)
    {
        _deathState = state;
        if (Is(DeathState.Dying))
        {
            if (!Is(RangedState.Holstered)) Set(RangedState.Inactive);
            if (!Is(MeleeState.Sheathed)) Set(MeleeState.Inactive);
        }
        if (Is(DeathState.Respawning))
        {
            if (_weapons.rangedWeapon != null) Set(RangedState.Holstered);
            if (_weapons.meleeWeapon != null) Set(MeleeState.Sheathed);
        }
    }
    public void Set(RangedState state) => _rangedState = state;
    public void Set(MeleeState state) => _meleeState = state;
    public void Set(MoveState state) => _moveState = state;
    public void Set(JumpState state) => _jumpState = state;

    public DeathState GetDeathState() => _deathState;
    public RangedState GetRangedState() => _rangedState;
    public MeleeState GetMeleeState() => _meleeState;
    public MoveState GetMoveState() => _moveState;
    public JumpState GetJumpState() => _jumpState;

    public bool Is(params DeathState[] states) => states.Any(state => state == _deathState);
    public bool Is(params RangedState[] states) => states.Any(state => state == _rangedState);
    public bool Is(params MeleeState[] states) => states.Any(state => state == _meleeState);
    public bool Is(params MoveState[] states) => states.Any(state => state == _moveState);
    public bool Is(params JumpState[] states) => states.Any(state => state == _jumpState);


    private void Awake()
    {
        _attributes = GetComponent<PlayerAttributes>();
        _weapons = GetComponent<PlayerWeapons>();
    }


    //public override void OnStartClient()
    //{
    //    base.OnStartClient();
    //    Initialize();
    //}


    //private void Initialize()
    //{
        
    //}


    //private void Update()
    //{
    //    if (!base.IsOwner) return;
    //}
}
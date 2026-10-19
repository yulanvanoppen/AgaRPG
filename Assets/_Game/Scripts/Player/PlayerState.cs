using FishNet.Object;

namespace AgaRPG.Player
{
    /// <summary>State of the ranged-weapon layer of a player.</summary>
    public enum RangedState { Inactive, Holstered, Drawn, Aiming, Shooting, Reloading }

    /// <summary>State of the melee-weapon layer of a player.</summary>
    public enum MeleeState { Inactive, Sheathed, Drawn, Readied, Striking, Blocking }

    /// <summary>
    /// Holds the combat state machine values of a player.
    /// The state is written from the predicted combat tick (see <c>PlayerCombat</c>) on every
    /// machine that simulates the player, so it is intentionally not network-synchronised.
    /// </summary>
    /// <remarks>
    /// The <c>Is</c> overloads are allocation free: they are called many times per tick, so
    /// they avoid the <c>params</c> array + LINQ pattern used previously.
    /// </remarks>
    public class PlayerState : NetworkBehaviour
    {
        private RangedState _rangedState;
        private MeleeState _meleeState;

        public RangedState Ranged => _rangedState;
        public MeleeState Melee => _meleeState;

        public void Set(RangedState state) => _rangedState = state;
        public void Set(MeleeState state) => _meleeState = state;

        public bool Is(RangedState a) => _rangedState == a;
        public bool Is(RangedState a, RangedState b) => _rangedState == a || _rangedState == b;

        public bool Is(MeleeState a) => _meleeState == a;
        public bool Is(MeleeState a, MeleeState b) => _meleeState == a || _meleeState == b;
        public bool Is(MeleeState a, MeleeState b, MeleeState c) => _meleeState == a || _meleeState == b || _meleeState == c;
    }
}

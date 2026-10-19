using UnityEngine;

namespace AgaRPG.Player
{
    /// <summary>
    /// Animator state behaviour on the strike states: ends the strike as soon as the strike
    /// animation finishes, if that happens before the strike timer in <c>PlayerCombat</c> runs out.
    /// </summary>
    public class OnStrikeEnd : StateMachineBehaviour
    {
        public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (animator.transform.root.TryGetComponent(out PlayerState state) && state.Is(MeleeState.Striking))
                state.Set(MeleeState.Drawn);
        }
    }
}

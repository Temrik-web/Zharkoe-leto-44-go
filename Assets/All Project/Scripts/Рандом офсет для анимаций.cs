using UnityEngine;

/// <summary>Starts each animator instance at a different point in a looping animation.</summary>
public sealed class RandomAnimationCycleOffset : StateMachineBehaviour
{
    private static readonly int CycleOffset = Animator.StringToHash("CycleOffset");

    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        animator.SetFloat(CycleOffset, Random.value);
    }
}

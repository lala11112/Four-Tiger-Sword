using UnityEngine;

public sealed class PlayerJumpIdleArmsBehaviour : StateMachineBehaviour
{
    private const string ArmsLayerName = "Jump Idle Arms";
    private static readonly int JumpHash = Animator.StringToHash("Jump");
    private static readonly int AirHash = Animator.StringToHash("Jump_Air");
    private static readonly int LandHash = Animator.StringToHash("Jump_Land");

    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        => UpdateArmsWeight(animator, layerIndex);

    public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        => UpdateArmsWeight(animator, layerIndex);

    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        => UpdateArmsWeight(animator, layerIndex, stateInfo.fullPathHash);

    private static void UpdateArmsWeight(Animator animator, int layerIndex, int exitingStateHash = 0)
    {
        if (layerIndex != 0) return;

        int armsLayer = animator.GetLayerIndex(ArmsLayerName);
        if (armsLayer <= 0) return;

        // Use the destination so an attack or a run can take over the arms as soon as its blend begins.
        bool transitioning = animator.IsInTransition(layerIndex);
        var target = transitioning
            ? animator.GetNextAnimatorStateInfo(layerIndex)
            : animator.GetCurrentAnimatorStateInfo(layerIndex);
        bool targetIsExiting = !transitioning && target.fullPathHash == exitingStateHash;
        bool targetIsJump = target.shortNameHash == JumpHash || target.shortNameHash == AirHash
            || target.shortNameHash == LandHash;
        float weight = targetIsJump && !targetIsExiting ? 1f : 0f;
        if (animator.GetLayerWeight(armsLayer) != weight)
            animator.SetLayerWeight(armsLayer, weight);
    }
}

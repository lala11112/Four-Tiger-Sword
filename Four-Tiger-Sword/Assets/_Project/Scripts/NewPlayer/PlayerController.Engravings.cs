using UnityEngine;

public partial class PlayerController
{
    public PlayerEngravingRuntime Engravings => GetComponent<PlayerEngravingRuntime>();
    public float EffectiveRunSpeed => RunSpeed * (Engravings != null ? Engravings.MoveMultiplier : 1f);

    private void HandleEngravingRevive()
    {
        CancelFinisherSelection();
        CancelPendingSwitchAttack();
        ConsumeHitReaction();
        CancelHitStops();
        if (Animator != null && Movement != null && StateMachine != null)
            StateMachine.ChangeState(new PlayerIdleState(this));
        Engravings?.PushAfterRevive();
    }
}

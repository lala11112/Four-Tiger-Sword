public class PlayerStaggerState : PlayerHitState
{
    public PlayerStaggerState(PlayerController player) : base(player) { }

    public override PlayerHitReaction Reaction => PlayerHitReaction.Stagger;
    protected override float Duration => Player.StaggerDuration;
    protected override string AnimationName => Player.StaggerAnimationName;
}

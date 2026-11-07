using UnityEngine;

public class PlayerKnockbackState : PlayerHitState
{
    public PlayerKnockbackState(PlayerController player) : base(player) { }

    public override PlayerHitReaction Reaction => PlayerHitReaction.Knockback;
    protected override string AnimationName => Player.KnockbackAnimationName;

    protected override Vector3 GetKnockbackDisplacement(float deltaTime)
    {
        return KnockbackMotion.Displacement(HitVelocity, Elapsed, deltaTime,
            Mathf.Min(Player.KnockbackDuration, Player.HitDuration));
    }
}

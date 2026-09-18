using UnityEngine;

public class PlayerKnockbackState : PlayerHitState
{
    public PlayerKnockbackState(PlayerController player) : base(player) { }

    public override PlayerHitReaction Reaction => PlayerHitReaction.Knockback;
    protected override float Duration => Mathf.Max(Player.StaggerDuration, Player.KnockbackDuration);
    protected override string AnimationName => Player.KnockbackAnimationName;

    protected override Vector3 GetKnockbackDisplacement(float deltaTime)
    {
        float duration = Player.KnockbackDuration;
        if (duration <= 0f || Elapsed >= duration) return Vector3.zero;

        // 선형 감속을 적분하여 프레임 길이에 따라 밀리는 거리가 달라지지 않게 합니다.
        float end = Mathf.Min(Elapsed + deltaTime, duration);
        float distanceScale = (end - Elapsed) * (1f - (Elapsed + end) / (2f * duration));
        return HitVelocity * distanceScale;
    }
}

using UnityEngine;

/// <summary>A committed down-and-rise reaction. Input resumes after the get-up interval.</summary>
public sealed class PlayerKnockdownState : PlayerHitState
{
    private float _downSeconds;
    private float _getUpSeconds;
    private bool _gettingUp;

    public PlayerKnockdownState(PlayerController player) : base(player) { }
    public override PlayerHitReaction Reaction => PlayerHitReaction.Knockdown;
    public bool IsGettingUp => _gettingUp;
    protected override float Duration => _downSeconds + _getUpSeconds;
    protected override string AnimationName => Player.KnockdownAnimationName;

    protected override void RestartReaction()
    {
        _downSeconds = Player.PendingKnockdownDownSeconds;
        _getUpSeconds = Player.PendingKnockdownGetUpSeconds;
        _gettingUp = false;
        base.RestartReaction();
        PlayTimed(Player.KnockdownAnimationName, _downSeconds);
    }

    protected override void UpdateReaction(float deltaTime)
    {
        base.UpdateReaction(deltaTime);
        if (!_gettingUp && Elapsed + 0.000001f >= _downSeconds)
        {
            _gettingUp = true;
            PlayTimed(Player.GetUpAnimationName, _getUpSeconds, Mathf.Max(0f, Elapsed - _downSeconds));
        }
    }

    protected override Vector3 GetKnockbackDisplacement(float deltaTime)
        => KnockbackMotion.Displacement(HitVelocity, Elapsed, deltaTime, Player.KnockbackDuration);

    private void PlayTimed(string stateName, float seconds, float elapsed = 0f)
    {
        if (seconds <= 0f || string.IsNullOrWhiteSpace(stateName) || Player.Animator == null) return;
        int hash = Animator.StringToHash(stateName);
        if (!Player.Animator.HasState(0, hash)) return;
        Player.SetAnimationSpeed(1f);
        Player.Animator.Play(hash, 0, Mathf.Clamp01(elapsed / seconds));
        Player.Animator.Update(0f);
        float length = Player.Animator.GetCurrentAnimatorStateInfo(0).length;
        if (KnockbackMotion.IsFinite(length) && length > 0f)
            Player.SetAnimationSpeed(length / seconds);
    }
}

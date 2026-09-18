using UnityEngine;

public enum PlayerHitReaction { None, Hit, Stagger, Knockback }

/// <summary>피격 중 행동을 막고 중력을 처리합니다. 경직/넉백도 같은 복귀 규칙을 사용합니다.</summary>
public class PlayerHitState : IPlayerState
{
    protected readonly PlayerController Player;
    protected float Elapsed;
    protected Vector3 HitVelocity;
    private bool _previousRootMotion;

    public bool IsComplete { get; private set; }
    public virtual PlayerHitReaction Reaction => PlayerHitReaction.Hit;
    protected virtual float Duration => Player.HitDuration;
    protected virtual string AnimationName => Player.HitAnimationName;

    public PlayerHitState(PlayerController player) => Player = player;

    public void Enter()
    {
        Player.StopParryKnockback();
        _previousRootMotion = Player.Animator.applyRootMotion;
        Player.Animator.applyRootMotion = false;
        RestartReaction();
    }

    private void RestartReaction()
    {
        HitVelocity = Player.ConsumeHitReaction();
        Elapsed = 0f;
        IsComplete = false;
        Player.Animator.speed = 1f;
        ClearActionBuffers();

        // 전용 모션이 아직 없으면 Hit, Idle 순으로 폴백하여 이전 공격 모션을 끊습니다.
        if (!TryPlay(AnimationName) && !TryPlay(Player.HitAnimationName))
            TryPlay("Idle");
    }

    private bool TryPlay(string stateName)
    {
        if (string.IsNullOrWhiteSpace(stateName)) return false;
        int hash = Animator.StringToHash(stateName);
        if (!Player.Animator.HasState(0, hash)) return false;
        Player.Animator.CrossFade(hash, 0.05f, 0, 0f);
        return true;
    }

    public void Update()
    {
        // StateMachine은 동일 타입으로 재진입하지 않으므로 연속 피격은 여기서 갱신합니다.
        if (Player.PendingHitReaction != PlayerHitReaction.None)
            RestartReaction();

        ClearActionBuffers();
        Player.Movement.ApplyGravity();
        Vector3 displacement = GetKnockbackDisplacement(Time.deltaTime);
        displacement.y = Player.VerticalVelocity * Time.deltaTime;
        if (Player.Controller.enabled)
        {
            CollisionFlags flags = Player.Controller.Move(displacement);
            if ((flags & CollisionFlags.Above) != 0 && Player.VerticalVelocity > 0f)
                Player.VerticalVelocity = 0f;
        }

        Elapsed += Time.deltaTime;
        IsComplete = Elapsed >= Mathf.Max(0f, Duration);
    }

    protected virtual Vector3 GetKnockbackDisplacement(float deltaTime) => Vector3.zero;

    private void ClearActionBuffers()
    {
        Player.Input.AttackBuffer.Consume();
        Player.Input.SkillBuffer.Consume();
        Player.Input.UltimateBuffer.Consume();
        Player.Input.JumpBuffer.Consume();
        Player.Input.DashBuffer.Consume();
        Player.Input.ParryBuffer.Consume();
    }

    public void Exit()
    {
        Player.Animator.applyRootMotion = _previousRootMotion;
        Player.Animator.speed = 1f;
        HitVelocity = Vector3.zero;
    }
}

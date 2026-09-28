using UnityEngine;

public abstract partial class BaseForm
{
    protected virtual WeaponActionData HeavyAttackStep => null;

    protected void BeginHeavyAttack()
    {
        _currentAction = ActionType.HeavyAttack;
        _timer = 0f;
        ClearHitTargets();
        ResetTargetApproach(HeavyAttackStep);
        _playerController.Animator.speed = AttackSpeed;
        if (_playerController.Animator.HasState(0, Animator.StringToHash(HeavyAttackStep.AnimationName)))
            _playerController.Animator.CrossFade(HeavyAttackStep.AnimationName, 0f, 0, 0f);
        if (HeavyAttackStep.SlashVFX != null)
            Object.Instantiate(HeavyAttackStep.SlashVFX,
                _playerController.transform.position + _playerController.transform.rotation * HeavyAttackStep.HitBoxOffset,
                _playerController.transform.rotation);
        if (HeavyAttackStep.SwingSound != null)
            AudioSource.PlayClipAtPoint(HeavyAttackStep.SwingSound, _playerController.transform.position);
    }

    public virtual void UpdateHeavyAttack(out bool isComplete)
    {
        if (HeavyAttackStep == null) { isComplete = true; return; }
        _timer += Time.deltaTime * AttackSpeed;
        ProcessHit(HeavyAttackStep);
        MoveForward(HeavyAttackStep);
        isComplete = _timer >= HeavyAttackStep.Duration;
    }

    public virtual void EndHeavyAttack() => EndAttack();
}

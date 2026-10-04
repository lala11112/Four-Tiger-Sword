using UnityEngine;

public abstract partial class BaseForm
{
    protected virtual WeaponActionData HeavyAttackStep => null;

    protected void BeginHeavyAttack(bool continuing = false, Transform cameraTarget = null,
        Vector3 cameraDirection = default)
    {
        _currentAction = ActionType.HeavyAttack;
        _timer = 0f;
        ClearHitTargets();
        ResetTargetApproach(HeavyAttackStep);
        BeginAttackCamera(HeavyAttackStep, continuing, cameraTarget, cameraDirection);
        _playerController.Animator.speed = AttackSpeed;
        if (_playerController.Animator.HasState(0, Animator.StringToHash(HeavyAttackStep.AnimationName)))
            _playerController.Animator.CrossFade(HeavyAttackStep.AnimationName, 0f, 0, 0f);
        if (HeavyAttackStep.SwingSound != null)
            AudioSource.PlayClipAtPoint(HeavyAttackStep.SwingSound, _playerController.transform.position);
        _playerController.UpdateAttackTrails(HeavyAttackStep, 0f);
    }

    public virtual void UpdateHeavyAttack(out bool isComplete)
    {
        if (HeavyAttackStep == null) { isComplete = true; return; }
        _timer += Time.deltaTime * AttackSpeed;
        _playerController.UpdateAttackTrails(HeavyAttackStep, _timer);
        MoveForward(HeavyAttackStep);
        ProcessHit(HeavyAttackStep);
        isComplete = _timer >= HeavyAttackStep.Duration;
    }

    public virtual void EndHeavyAttack() => EndAttack();
}

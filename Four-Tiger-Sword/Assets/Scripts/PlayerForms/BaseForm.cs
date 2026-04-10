using UnityEngine;
using System.Collections.Generic;

public abstract class BaseForm : IForm
{
    protected PlayerController _playerController;
    protected WeaponActionDataSO _weaponActionData;

    protected int _comboStep = 0;
    protected float _timer = 0;

    protected LayerMask _enemyLayer;

    // key: HitEvent 인덱스 (싱글히트 모드에서는 -1 사용)
    private Dictionary<int, HashSet<Collider>> _hitEventTargets = new Dictionary<int, HashSet<Collider>>();

    protected virtual DamageType FormElement => DamageType.Normal;

    public BaseForm(WeaponActionDataSO weaponActionData)
    {
        _weaponActionData = weaponActionData;
    }

    public virtual void Equip(PlayerController playerController)
    {
        _playerController = playerController;
        _enemyLayer = LayerMask.GetMask("Enemy");
    }

    public virtual void Unequip(PlayerController playerController) { }

    public virtual void BeginAttack()
    {
        _comboStep = 0;
        PlayCombo();
    }
    public abstract void UpdateAttack(out bool isComplete);
    public virtual void EndAttack()
    {
        _comboStep = 0;
        ClearHitTargets();
    }

    public virtual void BeginAirAttack()
    {
        _timer = 0;
        ClearHitTargets();
    }

    public virtual void UpdateAirAttack(out bool isComplete)
    {
        isComplete = false;

        if (_weaponActionData == null || _weaponActionData.AirAttackStep == null)
        {
            isComplete = true;
            return;
        }

        _timer += Time.deltaTime;
        ProcessHit(_weaponActionData.AirAttackStep);
        isComplete = _timer >= _weaponActionData.AirAttackStep.Duration;
    }

    public virtual void EndAirAttack()
    {
        ClearHitTargets();
    }

    public virtual void BeginSkill()
    {
        _timer = 0;
        ClearHitTargets();
    }

    public virtual void UpdateSkill(out bool isComplete)
    {
        isComplete = false;

        if (_weaponActionData == null || _weaponActionData.SkillStep == null)
        {
            isComplete = true;
            return;
        }

        _timer += Time.deltaTime;
        ProcessHit(_weaponActionData.SkillStep);
        isComplete = _timer >= _weaponActionData.SkillStep.Duration;
    }

    public virtual void EndSkill()
    {
        ClearHitTargets();
    }

    public virtual void BeginUltimate()
    {
        _timer = 0;
        ClearHitTargets();
    }

    public virtual void UpdateUltimate(out bool isComplete)
    {
        isComplete = false;

        if (_weaponActionData == null || _weaponActionData.UltimateStep == null)
        {
            isComplete = true;
            return;
        }

        _timer += Time.deltaTime;
        ProcessHit(_weaponActionData.UltimateStep);
        isComplete = _timer >= _weaponActionData.UltimateStep.Duration;
    }

    public virtual void EndUltimate()
    {
        ClearHitTargets();
    }

    protected void PlayCombo()
    {
        _timer = 0;
        _playerController.Input.AttackBuffer.Consume();
        ClearHitTargets();

        WeaponActionData step = _weaponActionData.ComboSteps[_comboStep];

        Debug.Log("공격이름 : " + step.AnimationName + " 공격타수 : " + _comboStep);

        //_playerController.Animator.CrossFade(step.AnimationName);
    }

    private void ClearHitTargets()
    {
        _hitEventTargets.Clear();
    }

    protected void ProcessHit(WeaponActionData step)
    {
        if (step.HitEvents != null && step.HitEvents.Count > 0)
        {
            // 멀티히트 모드: 각 HitEvent를 독립적으로 처리
            for (int i = 0; i < step.HitEvents.Count; i++)
            {
                HitEvent hitEvent = step.HitEvents[i];

                if (_timer < hitEvent.StartTime || _timer > hitEvent.StartTime + hitEvent.Duration)
                    continue;

                if (!_hitEventTargets.ContainsKey(i))
                    _hitEventTargets[i] = new HashSet<Collider>();

                int damage = hitEvent.Damage > 0 ? hitEvent.Damage : step.Damage;
                ExecuteHit(step, damage, _hitEventTargets[i]);
            }
        }
        else
        {
            // 싱글히트 모드: 기존 방식
            if (_timer < step.HitStartTime || _timer > step.HitStartTime + step.HitDuration)
                return;

            if (!_hitEventTargets.ContainsKey(-1))
                _hitEventTargets[-1] = new HashSet<Collider>();

            ExecuteHit(step, step.Damage, _hitEventTargets[-1]);
        }
    }

    private void ExecuteHit(WeaponActionData step, int damage, HashSet<Collider> hitTargets)
    {
        Vector3 hitboxCenter = _playerController.transform.position + (_playerController.transform.rotation * step.HitBoxOffset);
        Collider[] hits = GetOverlap(step, hitboxCenter);

        foreach (var hit in hits)
        {
            if (hitTargets.Contains(hit)) continue;
            hitTargets.Add(hit);

            bool isCritical = Random.value < step.CriticalChance;
            int finalDamage = isCritical ? Mathf.RoundToInt(damage * step.CriticalMultiplier) : damage;

            Debug.Log($"적 <color=red>{hit.name}</color>에게 <color=yellow>{finalDamage}</color> 데미지 적중! (속성: {FormElement}, 치명타: {isCritical})");
            hit.GetComponent<IDamageable>().TakeDamage(finalDamage, FormElement, isCritical);
        }
    }

    private Collider[] GetOverlap(WeaponActionData step, Vector3 hitboxCenter)
    {
        switch (step.HitBoxShape)
        {
            case HitBoxShape.Sphere:
                return Physics.OverlapSphere(hitboxCenter, step.HitBoxRadius, _enemyLayer);
            case HitBoxShape.Box:
                return Physics.OverlapBox(hitboxCenter, step.HitBoxSize, _playerController.transform.rotation, _enemyLayer);
            case HitBoxShape.Capsule:
                float pointOffset = (step.HitBoxHeight / 2f) - step.HitBoxRadius;
                Vector3 point1 = hitboxCenter + _playerController.transform.up * pointOffset;
                Vector3 point2 = hitboxCenter - _playerController.transform.up * pointOffset;
                return Physics.OverlapCapsule(point1, point2, step.HitBoxRadius, _enemyLayer);
            default:
                return new Collider[0];
        }
    }

    public virtual void DrawHitboxGizmo() //기즈모그리는 함수(테스트용)
    {
        if (_weaponActionData == null) return;

        WeaponActionData currentStep = null;

        if (_playerController.StateMachine.CurrentState is PlayerAttackState)
        {
            if (_weaponActionData.ComboSteps.Count == 0) return;
            currentStep = _weaponActionData.ComboSteps[_comboStep];
        }
        else if (_playerController.StateMachine.CurrentState is PlayerAirAttackState)
        {
            if (_weaponActionData.AirAttackStep == null) return;
            currentStep = _weaponActionData.AirAttackStep;
        }
        else
        {
            return;
        }

        bool isHitActive = IsHitWindowActive(currentStep);
        if (!isHitActive) return;

        Gizmos.color = new Color(1f, 0f, 0f, 0.4f);
        DrawHitboxShape(currentStep);
    }

    private bool IsHitWindowActive(WeaponActionData step)
    {
        if (step.HitEvents != null && step.HitEvents.Count > 0)
        {
            foreach (var hitEvent in step.HitEvents)
            {
                if (_timer >= hitEvent.StartTime && _timer <= hitEvent.StartTime + hitEvent.Duration)
                    return true;
            }
            return false;
        }

        return _timer >= step.HitStartTime && _timer <= step.HitStartTime + step.HitDuration;
    }

    private void DrawHitboxShape(WeaponActionData step)
    {
        Vector3 hitboxCenter = _playerController.transform.position + (_playerController.transform.rotation * step.HitBoxOffset);

        switch (step.HitBoxShape)
        {
            case HitBoxShape.Sphere:
                Gizmos.DrawSphere(hitboxCenter, step.HitBoxRadius);
                break;

            case HitBoxShape.Box:
                Matrix4x4 oldMatrix = Gizmos.matrix;
                Gizmos.matrix = Matrix4x4.TRS(hitboxCenter, _playerController.transform.rotation, Vector3.one);
                Gizmos.DrawCube(Vector3.zero, step.HitBoxSize * 2f);
                Gizmos.matrix = oldMatrix;
                break;

            case HitBoxShape.Capsule:
                float pointOffset = (step.HitBoxHeight / 2f) - step.HitBoxRadius;
                Vector3 p1 = hitboxCenter + _playerController.transform.up * pointOffset;
                Vector3 p2 = hitboxCenter - _playerController.transform.up * pointOffset;
                Gizmos.DrawSphere(p1, step.HitBoxRadius);
                Gizmos.DrawSphere(p2, step.HitBoxRadius);
                Gizmos.DrawLine(p1, p2);
                break;
        }
    }
}

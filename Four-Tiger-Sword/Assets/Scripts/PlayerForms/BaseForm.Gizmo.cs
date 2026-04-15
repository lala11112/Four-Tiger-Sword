using UnityEngine;

public abstract partial class BaseForm
{
    public virtual void DrawHitboxGizmo()
    {
        if (_weaponActionData == null) return;

        WeaponActionData step = GetCurrentGizmoStep();
        if (step == null || !IsHitWindowActive(step)) return;

        Gizmos.color = new Color(1f, 0f, 0f, 0.4f);
        DrawHitboxShape(step);
    }

    private WeaponActionData GetCurrentGizmoStep()
    {
        var state = _playerController.StateMachine.CurrentState;

        if (state is PlayerAttackState)
            return _weaponActionData.ComboSteps?.Count > 0 ? _weaponActionData.ComboSteps[_comboStep] : null;

        if (state is PlayerAirAttackState)
            return _weaponActionData.AirAttackStep;

        if (state is PlayerSkillState)
            return _weaponActionData.SkillSteps?.Count > 0 ? _weaponActionData.SkillSteps[_skillStep] : null;

        if (state is PlayerUltimateState)
            return _weaponActionData.UltimateSteps?.Count > 0 ? _weaponActionData.UltimateSteps[_ultimateStep] : null;

        return null;
    }

    private bool IsHitWindowActive(WeaponActionData step)
    {
        if (step.HitEvents?.Count > 0)
        {
            foreach (var e in step.HitEvents)
                if (_timer >= e.StartTime && _timer <= e.StartTime + e.Duration) return true;
            return false;
        }

        return _timer >= step.HitStartTime && _timer <= step.HitStartTime + step.HitDuration;
    }

    private void DrawHitboxShape(WeaponActionData step)
    {
        Vector3 center = _playerController.transform.position
                       + _playerController.transform.rotation * step.HitBoxOffset;

        switch (step.HitBoxShape)
        {
            case HitBoxShape.Sphere:
                Gizmos.DrawSphere(center, step.HitBoxRadius);
                break;

            case HitBoxShape.Box:
                Matrix4x4 prev = Gizmos.matrix;
                Gizmos.matrix = Matrix4x4.TRS(center, _playerController.transform.rotation, Vector3.one);
                Gizmos.DrawCube(Vector3.zero, step.HitBoxSize * 2f);
                Gizmos.matrix = prev;
                break;

            case HitBoxShape.Capsule:
                float offset = (step.HitBoxHeight / 2f) - step.HitBoxRadius;
                Vector3 p1 = center + _playerController.transform.up * offset;
                Vector3 p2 = center - _playerController.transform.up * offset;
                Gizmos.DrawSphere(p1, step.HitBoxRadius);
                Gizmos.DrawSphere(p2, step.HitBoxRadius);
                Gizmos.DrawLine(p1, p2);
                break;
        }
    }
}

using UnityEngine;

public partial class PlayerController
{
    [Header("Attack Trail VFX")]
    [SerializeField, Tooltip("각 폼의 Trail 컨트롤러를 모두 등록하세요. 비워두면 플레이어 자식에서 자동으로 찾습니다. 활성 상태이며 현재 폼과 일치하는 효과와 공통 효과만 재생합니다.")]
    private TrailVfxController[] _attackTrails;

    private bool _attackTrailsInitialized;

    private void InitializeAttackTrails()
    {
        if (_attackTrailsInitialized) return;
        if (_attackTrails == null || _attackTrails.Length == 0)
            _attackTrails = GetComponentsInChildren<TrailVfxController>(true);
        _attackTrailsInitialized = true;
    }

    /// <summary>Uses the attack motion's clock, independently of damage windows.</summary>
    public void UpdateAttackTrails(WeaponActionData step, float attackTime)
    {
        bool emitting = false;
        if (step != null && !step.DisableTrail)
        {
            float start = step.UseCustomTrailTiming ? Mathf.Max(0f, step.TrailStartTime) : 0f;
            float end = step.UseCustomTrailTiming
                ? Mathf.Min(step.Duration, step.TrailEndTime) : step.Duration;
            emitting = attackTime >= start && attackTime < end;
        }

        InitializeAttackTrails();
        ElementType currentForm = Element;
        foreach (TrailVfxController trail in _attackTrails)
        {
            if (trail == null) continue;
            trail.SetEmitting(emitting && isActiveAndEnabled && trail.isActiveAndEnabled
                && trail.MatchesForm(currentForm));
        }
    }

    public void StopAttackTrails(bool clear = false)
    {
        InitializeAttackTrails();
        foreach (TrailVfxController trail in _attackTrails)
        {
            if (trail == null) continue;
            if (clear) trail.ClearTrail();
            else trail.EndTrail();
        }
    }
}

using UnityEngine;

/// <summary>이동에는 영향을 주지 않고 공격만 막습니다.</summary>
public sealed class SilenceEffect : IStatusEffect
{
    public float Remaining { get; private set; }
    public bool IsExpired => Remaining <= 0f;
    public SilenceEffect(float duration) => Refresh(duration);
    public void Refresh(float duration) => Remaining = Mathf.Max(Remaining, duration);
    public void OnApply(GameObject target)
    {
        var enemy = target.GetComponent<Enemy>();
        if (enemy == null) return;
        enemy.CurrentAction?.Exit();
        enemy.CurrentAction = null;
        enemy.HasSelectedAttack = false;
    }
    public void OnUpdate(float deltaTime) => Remaining = Mathf.Max(0f, Remaining - deltaTime);
    public void OnRemove(GameObject target) { }
}

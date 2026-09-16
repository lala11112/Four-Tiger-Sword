using UnityEngine;

public abstract partial class BaseForm
{
    protected FormEffectRunner Effects => _playerController.GetComponent<FormEffectRunner>()
        ?? _playerController.gameObject.AddComponent<FormEffectRunner>();

    protected HitInfo EffectHit(float multiplier, float extraDamage = 0f, bool trueDamage = false)
        => new HitInfo(multiplier * _playerController.StatManager.GetStat(StatType.ST_ATK) + extraDamage,
            Element, trueDamage ? 0f : _playerController.StatManager.GetStat(StatType.ST_CRT) / 100f,
            _playerController.StatManager.GetStat(StatType.ST_CRTD) / 100f, trueDamage: trueDamage, source: _playerController);

    protected static FormTargetEffects TargetEffects(GameObject target)
        => target.GetComponent<FormTargetEffects>() ?? target.AddComponent<FormTargetEffects>();

    protected virtual bool DealsTrueDamage => false;
    public virtual bool BlocksIncomingDamage(float damage, Vector3 incomingPower, object source = null) => false;
    public virtual void CleanupTransientEffects() { }

    protected float FirstHitTime(WeaponActionData step)
    {
        float time = step.HitStartTime;
        if (step.HitEvents != null && step.HitEvents.Count > 0)
        {
            time = float.MaxValue;
            foreach (var hit in step.HitEvents)
                if (hit != null) time = Mathf.Min(time, hit.StartTime);
        }
        return Mathf.Max(0f, time);
    }
}

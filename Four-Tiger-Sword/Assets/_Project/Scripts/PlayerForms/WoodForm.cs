using UnityEngine;

public class WoodForm : BaseForm
{
    private readonly WoodFormActionDataSO _woodData;
    public override ElementType Element => ElementType.ELEMENT_WOOD;
    public override float SkillSpCost => Mathf.Max(0f, _woodData.SkillSpiritCost);
    public override float SkillCooldown => Mathf.Max(0f, _woodData.SkillCooldownSeconds);
    public override bool CanUltimate => _woodData != null && UltimateResourcesReady;
    private bool _skillApplied, _skillActive;
    private float _elementBonus;
    public float ElementBuffRemaining { get; private set; }
    public float UltimateFieldRemaining { get; private set; }
    public Vector3 UltimateFieldCenter { get; private set; }
    public WoodForm(WoodFormActionDataSO data) : base(data) { _woodData = data; }
    public override void Equip(PlayerController player)
    {
        base.Equip(player);
        PlayerUIManager.Instance?.WoodElement?.SetActive(false);
    }
    public override void Unequip(PlayerController player)
    {
        _skillActive = false;
        PlayerUIManager.Instance?.WoodElement?.SetActive(true);
    }
    private static WoodTargetEffects WoodEffects(GameObject target)
        => target.GetComponent<WoodTargetEffects>() ?? target.AddComponent<WoodTargetEffects>();
    protected override void OnHitEnemy(GameObject enemy)
    {
        base.OnHitEnemy(enemy);
        WoodEffects(enemy).AddStacks(_woodData);
    }
    public override void BeginSkill()
    {
        base.BeginSkill();
        _skillApplied = false;
        _skillActive = true;
        _softTarget = null;
    }
    public override void UpdateSkill(out bool isComplete) => AdvanceSkill(Time.deltaTime, out isComplete);
    private void AdvanceSkill(float dt, out bool isComplete)
    {
        var step = _woodData.SkillSteps[0];
        if (!_skillActive) { isComplete = true; return; }
        _timer += Mathf.Max(0f, dt) * AttackSpeed;
        if (!_skillApplied && _timer >= FirstHitTime(step))
        {
            _skillApplied = true;
            ApplyGather();
        }
        isComplete = _timer >= Mathf.Max(step.Duration, FirstHitTime(step));
    }
    private void ApplyGather()
    {
        Vector3 center = _playerController.transform.position + _playerController.transform.rotation * _woodData.SkillCenterOffset;
        foreach (var target in FormEffectRunner.Targets(center, Mathf.Max(0.1f, _woodData.SkillRadius)))
        {
            if (target.GetComponent<EnemyStat>()?.IsDead == true) continue;
            var result = DamageManager.Apply(EffectHit(Mathf.Max(0f, _woodData.SkillDamageMultiplier)), (IDamageable)target, target.gameObject);
            if (!result.Applied || result.Killed) continue;
            base.OnHitEnemy(target.gameObject);
            var effects = WoodEffects(target.gameObject);
            effects.AddStacks(_woodData, true);
            effects.Pull(center, _woodData.PullDuration, _woodData.PullSpeed, _woodData.PullStopDistance);
        }
    }
    public override void EndSkill() { _skillActive = false; base.EndSkill(); }
    public override void BeginUltimate()
    {
        ConsumeUltimateResources();
        _currentAction = ActionType.Ultimate;
        RemoveElementBuff();
        ElementBuffRemaining = Mathf.Max(0f, _woodData.ElementBuffDuration);
        if (ElementBuffRemaining > 0f)
        {
            _elementBonus = Mathf.Max(0f, _woodData.ElementDamageBonusPercent);
            _playerController.StatManager.AddModifier(StatType.ST_ELM_ATK, _elementBonus, 0f);
        }
        UltimateFieldCenter = _playerController.transform.position;
        UltimateFieldRemaining = Mathf.Max(0f, _woodData.UltimateFieldDuration);
        if (UltimateFieldRemaining > 0f) ApplySilenceField();
    }
    public override void UpdateUltimate(out bool isComplete) => isComplete = true;
    private void ApplySilenceField()
    {
        foreach (var target in FormEffectRunner.Targets(UltimateFieldCenter, Mathf.Max(0.1f, _woodData.UltimateRadius)))
        {
            if (target.GetComponent<EnemyStat>()?.IsDead == true) continue;
            var handler = target.GetComponent<StatusEffectHandler>() ?? target.gameObject.AddComponent<StatusEffectHandler>();
            float duration = Mathf.Max(0.01f, _woodData.SilenceDuration);
            var silence = handler.Get<SilenceEffect>();
            if (silence == null || silence.IsExpired) handler.Apply(new SilenceEffect(duration));
            else silence.Refresh(duration);
        }
    }
    protected override void OnTick(float dt)
    {
        if (_playerController == null) return;
        if (!_playerController.isActiveAndEnabled || _playerController.StatManager.CurrentHp <= 0f)
        { CleanupTransientEffects(); return; }
        ElementBuffRemaining = Mathf.Max(0f, ElementBuffRemaining - Mathf.Max(0f, dt));
        if (ElementBuffRemaining <= 0f) RemoveElementBuff();
        UltimateFieldRemaining = Mathf.Max(0f, UltimateFieldRemaining - Mathf.Max(0f, dt));
        if (UltimateFieldRemaining > 0f) ApplySilenceField();
    }
    private void RemoveElementBuff()
    {
        if (_elementBonus != 0f && _playerController != null)
            _playerController.StatManager.RemoveModifier(StatType.ST_ELM_ATK, _elementBonus, 0f);
        _elementBonus = 0f;
        ElementBuffRemaining = 0f;
    }
    public override void CleanupTransientEffects()
    {
        _skillActive = false;
        UltimateFieldRemaining = 0f;
        RemoveElementBuff();
    }
}

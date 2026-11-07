using UnityEngine;

public abstract partial class BaseForm
{
    public FormChainSettings ChainSettings => _weaponActionData?.ChainSettings;
    public float ChainBuffRemaining { get; private set; }
    public bool IsChainBuffActive => ChainBuffRemaining > 0f;

    private bool _finalComboHitNotified;
    private float _chainAttackBonus, _chainAttackSpeedBonus;
    private float _chainVulnerabilityBonusPercent, _chainVulnerabilityDuration;

    private static float ChainNonnegative(float value)
        => KnockbackMotion.IsFinite(value) ? Mathf.Max(0f, value) : 0f;

    private void ApplyChainBuff()
    {
        RemoveChainBuff();
        var settings = ChainSettings;
        if (settings == null || !settings.Enabled || _playerController?.StatManager == null) return;
        _chainVulnerabilityBonusPercent = ChainNonnegative(settings.VulnerabilityBonusPercent);
        _chainVulnerabilityDuration = ChainNonnegative(settings.BuffDuration);
        ChainBuffRemaining = _chainVulnerabilityDuration;
        if (!IsChainBuffActive) return;

        _chainAttackBonus = ChainNonnegative(settings.AttackBonusPercent) / 100f;
        _chainAttackSpeedBonus = ChainNonnegative(settings.AttackSpeedBonusPercent) / 100f;
        if (_chainAttackBonus > 0f)
            _playerController.StatManager.AddModifier(StatType.ST_ATK, 0f, _chainAttackBonus);
        if (_chainAttackSpeedBonus > 0f)
            _playerController.StatManager.AddModifier(StatType.ST_ATK_SPD, 0f, _chainAttackSpeedBonus);
    }

    private void UpdateChainBuff(float deltaTime)
    {
        if (_playerController == null) return;
        if (!_playerController.isActiveAndEnabled || _playerController.StatManager == null
            || _playerController.StatManager.CurrentHp <= 0f)
        {
            RemoveChainBuff();
            return;
        }
        if (!IsChainBuffActive) return;
        ChainBuffRemaining = Mathf.Max(0f, ChainBuffRemaining - ChainNonnegative(deltaTime));
        if (!IsChainBuffActive) RemoveChainBuff();
    }

    private void RemoveChainBuff()
    {
        var stats = _playerController?.StatManager;
        if (stats != null)
        {
            if (_chainAttackBonus > 0f) stats.RemoveModifier(StatType.ST_ATK, 0f, _chainAttackBonus);
            if (_chainAttackSpeedBonus > 0f) stats.RemoveModifier(StatType.ST_ATK_SPD, 0f, _chainAttackSpeedBonus);
        }
        ChainBuffRemaining = 0f;
        _chainAttackBonus = _chainAttackSpeedBonus = 0f;
    }

    private void NotifyFinalComboHit(GameObject target)
    {
        if (_finalComboHitNotified || !_normalAttackActive || _currentAction != ActionType.Attack
            || target == null || target.GetComponent<EnemyStat>() == null) return;
        bool finalStage = _finisherAttackActive || _weaponActionData?.ComboSteps != null
            && _comboStep == _weaponActionData.ComboSteps.Count - 1;
        if (!finalStage) return;
        _finalComboHitNotified = true;
        _playerController?.FormManager?.NotifyFinalComboHit(Element);
    }

    private void ApplyChainVulnerability(GameObject target, DamageResult result)
    {
        if (_currentAction != ActionType.SwitchAttack || result.Killed || target == null
            || !target.activeInHierarchy || _chainVulnerabilityBonusPercent <= 0f
            || _chainVulnerabilityDuration <= 0f) return;
        var stats = target.GetComponent<EnemyStat>();
        if (stats == null || stats.IsDead) return;
        var effects = target.GetComponent<StatusEffectHandler>() ?? target.AddComponent<StatusEffectHandler>();
        effects.TryApply(new WaterChainVulnerabilityEffect(_chainVulnerabilityDuration, _chainVulnerabilityBonusPercent));
    }
}

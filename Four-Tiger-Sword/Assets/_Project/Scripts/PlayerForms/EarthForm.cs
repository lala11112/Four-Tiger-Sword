using UnityEngine;

public class EarthForm : BaseForm
{
    public override ElementType Element => ElementType.ELEMENT_EARTH;
    public override float SkillSpCost => 150f;
    public override float SkillCooldown => 8f;
    private readonly EarthAbsorptionStat _absorption = new();
    private float _ultimateElapsed;
    private bool _ultimateReleased;
    private bool _guardCounterReady;
    public bool GuardCounterReady => _guardCounterReady;
    public EarthForm(WeaponActionDataSO data) : base(data) { }
    public override void Equip(PlayerController player)
    {
        base.Equip(player);
        PlayerUIManager.Instance?.EarthElement?.SetActive(false);
    }
    public override void Unequip(PlayerController player)
    {
        _absorption.StopAndGet();
        PlayerUIManager.Instance?.EarthElement?.SetActive(true);
    }
    public override void BeginAttack()
    {
        _guardCounterReady = false;
        base.BeginAttack();
    }
    public override void BeginCounter()
    {
        _guardCounterReady = false;
        base.BeginCounter();
        // TODO: 저스트 가드 올려베기 반격 모션.
        // _playerController.Animator.CrossFade("EarthCounter", 0.1f);
    }
    public override bool BlocksIncomingDamage(float damage, Vector3 incomingPower, object attackSource = null)
    {
        if (_absorption.IsAbsorbing)
        {
            _absorption.Absorb(Mathf.CeilToInt(Mathf.Max(0f, damage)));
            return true;
        }
        bool guarding = _playerController.StateMachine.CurrentState is PlayerAttackState
            && _comboStep == 0 && _timer >= 10f / 60f && _timer <= 22f / 60f;
        Vector3 toSource = -incomingPower;
        if (attackSource is EnemyAction source && source.Owner != null)
            toSource = source.Owner.transform.position - _playerController.transform.position;
        if (!guarding || toSource.sqrMagnitude < 0.001f
            || Vector3.Dot(_playerController.transform.forward, toSource.normalized) <= 0f) return false;
        _guardCounterReady = true;
        _playerController.OnParrySuccess();
        (attackSource as EnemyAction)?.OnParried();
        return true;
    }
    protected override void OnHitEnemy(GameObject target)
    {
        base.OnHitEnemy(target);
        if (_currentAction == ActionType.Counter)
        {
            var effects = target.GetComponent<StatusEffectHandler>() ?? target.AddComponent<StatusEffectHandler>();
            effects.Apply(new AirborneEffect(1.5f));
        }
    }
    public override void BeginSkill()
    {
        base.BeginSkill();
        _playerController.StatManager.GrantShield(_weaponActionData.ShieldAmount, _weaponActionData.ShieldDuration);
        // TODO: 지각 변동 방패 내려찍기 모션.
        // _playerController.Animator.CrossFade("EarthShockwave", 0.1f);
    }
    public override void BeginUltimate()
    {
        base.BeginUltimate();
        _ultimateElapsed = 0f;
        _ultimateReleased = false;
        _absorption.StartAbsorbing();
        // TODO: 철옹성 방어/해제 모션. 5초는 공격속도와 무관한 게임 시간입니다.
        // _playerController.Animator.CrossFade("EarthFortress", 0.1f);
    }
    public override void UpdateUltimate(out bool isComplete)
    {
        _ultimateElapsed += Time.deltaTime;
        isComplete = _ultimateElapsed >= 5f;
        if (!isComplete || _ultimateReleased) return;
        _ultimateReleased = true;
        float absorbed = _absorption.StopAndGet();
        FormEffectRunner.DamageArea(_playerController.transform.position, _weaponActionData.AreaRadius,
            EffectHit(4f, absorbed * 0.8f));
        _playerController.ImpulseSource?.GenerateImpulse();
    }
    public override void EndUltimate()
    {
        _absorption.StopAndGet();
        base.EndUltimate();
    }
    public override void CleanupTransientEffects() => _absorption.StopAndGet();
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class WaterForm : BaseForm
{
    public override ElementType Element => ElementType.ELEMENT_WATER;
    public override float SkillSpCost => 150f;
    public override float SkillCooldown => 6f;
    private readonly WaterFlowGauge _flow = new();
    public float WaterGauge => _flow.CurrentGauge;
    public bool IsBuffActive => _flow.IsBuffActive;
    private bool _buffApplied;
    private float _baseMoveSpeed, _baseRunSpeed;
    private Image _waterGaugeUI;
    private LayerMask _savedExcludeLayers;
    private bool _skillCollisionOverride;
    private Vector3 _trailStart;
    private readonly HashSet<IDamageable> _dashHitTargets = new();
    private bool _ultimateActive;
    private float _ultimateElapsed;
    private int _ultimateHits;
    private Vector3 _ultimateCenter;
    private Transform _ultimateTarget;
    private readonly Dictionary<Renderer, bool> _visibility = new();

    public WaterForm(WeaponActionDataSO data) : base(data)
    {
        _flow.OnBuffActivated += ActivateBuff;
        _flow.OnBuffExpired += DeactivateBuff;
    }
    public override void Equip(PlayerController player)
    {
        base.Equip(player);
        _baseMoveSpeed = player.MoveSpeed; _baseRunSpeed = player.RunSpeed;
        _waterGaugeUI = PlayerUIManager.Instance?.WaterGauge;
        if (_waterGaugeUI != null) _waterGaugeUI.gameObject.SetActive(true);
        PlayerUIManager.Instance?.WaterElement?.SetActive(false);
        if (_flow.IsBuffActive) ActivateBuff();
    }
    public override void Unequip(PlayerController player)
    {
        CleanupTransientEffects();
        DeactivateBuff();
        if (_waterGaugeUI != null) _waterGaugeUI.gameObject.SetActive(false);
        PlayerUIManager.Instance?.WaterElement?.SetActive(true);
    }
    protected override void OnHitEnemy(GameObject enemy)
    {
        base.OnHitEnemy(enemy);
        _flow.Charge(12f); // 문서 미지정: 기존 타당 충전량 유지.
        if (_currentAction == ActionType.Skill && enemy.GetComponent<IDamageable>() is Component target)
            Effects.StartCoroutine(Effects.DelayedHits(target, EffectHit(0.5f), _weaponActionData.DelayedHitVFX));
    }
    protected override void OnTick(float dt)
    {
        _flow.Tick(dt);
        if (_waterGaugeUI != null) _waterGaugeUI.fillAmount = _flow.GaugeRatio;
        // 버프 중 변경되는 데이터 타이머와 모션 속도를 함께 맞춥니다.
        if (_playerController != null && _playerController.FormManager?.CurrentForm == this
            && (_playerController.StateMachine.CurrentState is PlayerAttackState
                || _playerController.StateMachine.CurrentState is PlayerSkillState))
            _playerController.Animator.speed = AttackSpeed;
    }
    private void ActivateBuff()
    {
        if (_buffApplied || _playerController == null) return;
        _buffApplied = true;
        _playerController.StatManager.AddModifier(StatType.ST_ATK_SPD, 0f, 0.3f);
        _playerController.MoveSpeed = _baseMoveSpeed * 1.3f;
        _playerController.RunSpeed = _baseRunSpeed * 1.3f;
    }
    private void DeactivateBuff()
    {
        if (!_buffApplied || _playerController == null) return;
        _buffApplied = false;
        _playerController.StatManager.RemoveModifier(StatType.ST_ATK_SPD, 0f, 0.3f);
        _playerController.MoveSpeed = _baseMoveSpeed;
        _playerController.RunSpeed = _baseRunSpeed;
    }
    public override void BeginSkill()
    {
        base.BeginSkill();
        if (_softTarget != null)
        {
            var dir = Vector3.ProjectOnPlane(_softTarget.position - _playerController.transform.position, Vector3.up);
            if (dir.sqrMagnitude > 0.001f) _playerController.transform.rotation = Quaternion.LookRotation(dir);
        }
        _savedExcludeLayers = _playerController.Controller.excludeLayers;
        _skillCollisionOverride = true;
        _playerController.Controller.excludeLayers |= LayerMask.GetMask("Enemy");
        _softTarget = null; // 한 번 정한 돌진 방향으로 진행.
        _trailStart = _playerController.Controller.bounds.center;
        _dashHitTargets.Clear();
    }
    protected override void ExecuteHit(WeaponActionData step, float damage, float poiseDamage, HashSet<IDamageable> targets)
        => base.ExecuteHit(step, damage, poiseDamage, _currentAction == ActionType.Skill ? _dashHitTargets : targets);

    public override void UpdateSkill(out bool isComplete)
    {
        base.UpdateSkill(out isComplete);
        var step = _weaponActionData.SkillSteps[_skillStep];
        if (_timer < FirstHitTime(step)) return;
        Vector3 end = _playerController.Controller.bounds.center;
        // 선딜 중 이미 통과한 구간도 첫 판정에 포함합니다. 매 적당 최초 돌진 1회만 적용.
        foreach (var collider in Physics.OverlapCapsule(_trailStart, end, Mathf.Max(0.1f, step.HitBoxRadius), _enemyLayer))
        {
            var target = collider.GetComponentInParent<IDamageable>();
            if (!(target is Component component) || !_dashHitTargets.Add(target)) continue;
            var result = DamageManager.Apply(EffectHit(step.Damage), target, component.gameObject);
            if (result.Applied) OnHitEnemy(component.gameObject);
        }
        _trailStart = end;
    }
    public override void EndSkill()
    {
        RestoreSkillCollision();
        base.EndSkill();
    }
    private void RestoreSkillCollision()
    {
        if (!_skillCollisionOverride) return;
        _playerController.Controller.excludeLayers = _savedExcludeLayers;
        _skillCollisionOverride = false;
    }
    public override void BeginUltimate()
    {
        base.BeginUltimate();
        _ultimateActive = true;
        _ultimateElapsed = 0f; _ultimateHits = 0;
        _ultimateTarget = _softTarget;
        _ultimateCenter = _softTarget != null ? _softTarget.position : _playerController.transform.position;
        _visibility.Clear();
        foreach (var renderer in _playerController.GetComponentsInChildren<Renderer>())
        {
            _visibility[renderer] = renderer.enabled;
            renderer.enabled = false;
        }
        // TODO: 만조의 춤 잔상 교차 베기/납도 모션.
        // _playerController.Animator.CrossFade("WaterDance", 0.1f);
    }
    public override bool BlocksIncomingDamage(float damage, Vector3 power, object source = null) => _ultimateActive;
    public override void UpdateUltimate(out bool isComplete)
    {
        _ultimateElapsed += Time.deltaTime;
        if (_ultimateTarget != null) _ultimateCenter = _ultimateTarget.position;
        while (_ultimateHits < 10 && _ultimateElapsed >= (_ultimateHits + 1) * 0.2f)
        {
            var hit = EffectHit(1.2f);
            bool firstHit = true;
            foreach (var target in FormEffectRunner.Targets(_ultimateCenter, _weaponActionData.AreaRadius))
            {
                if (target == null) continue;
                var result = DamageManager.Apply(hit, (IDamageable)target, target.gameObject);
                if (!result.Applied) continue;

                // 일반 공격과 동일하게 실제 적중 시에만 타격 피드백을 재생합니다.
                if (firstHit)
                {
                    _playerController.StartHitStop();
                    firstHit = false;
                }
                _playerController.ImpulseSource?.GenerateImpulse();
            }
            _ultimateHits++;
        }
        isComplete = _ultimateHits >= 10;
    }
    public override void EndUltimate()
    {
        if (_ultimateTarget != null && _playerController.Controller.enabled)
        {
            Vector3 desired = _ultimateTarget.position - _ultimateTarget.forward * 1.5f;
            _playerController.Controller.Move(Vector3.ProjectOnPlane(desired - _playerController.transform.position, Vector3.up));
        }
        CleanupTransientEffects();
        base.EndUltimate();
    }
    public override void CleanupTransientEffects()
    {
        _ultimateActive = false;
        foreach (var item in _visibility) if (item.Key != null) item.Key.enabled = item.Value;
        _visibility.Clear();
        RestoreSkillCollision();
    }
}

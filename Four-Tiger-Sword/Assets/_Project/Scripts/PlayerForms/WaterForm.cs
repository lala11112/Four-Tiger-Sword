using UnityEngine;
using UnityEngine.UI;

public class WaterForm : BaseForm
{
    public override ElementType Element => ElementType.ELEMENT_WATER;

    public override float SkillSpCost => 0f;
    public override float SkillCooldown => 3f;
    public override float UltimateSpCost => 400f;
    public override float UltimateCooldown => 25f;

    // ── 워터 게이지 설정 ─────────────────────────────────────────────────────
    public float MaxWaterGauge = 100f;
    public float GaugePerHit = 12f;  // 적 타격 1회당 게이지 충전량
    public float GaugeDrainRate = 5f;   // 초당 게이지 감소량
    public float BuffThreshold = 80f;  // 버프 발동 임계값

    public float AtkSpdBonus = 0.3f; // 공격속도 30% 증가 (승산)
    public float MoveSpdMultiplier = 1.3f; // 이동속도 30% 증가

    public float WaterGauge { get; private set; } = 0f;
    public bool IsBuffActive { get; private set; } = false;

    private float _baseMoveSpeed;
    private float _baseRunSpeed;
    private GameObject _waterGaugeUI;

    public WaterForm(WeaponActionDataSO weaponActionData) : base(weaponActionData) { }

    public override void Equip(PlayerController playerController)
    {
        base.Equip(playerController);
        _baseMoveSpeed = playerController.MoveSpeed;
        _baseRunSpeed = playerController.RunSpeed;
        //여기서 물속성 폼 전용 UI를 켤 거임.
        playerController.UIManager.WaterGauge.SetActive(true);
        playerController.UIManager.WaterElement.SetActive(false);
        _waterGaugeUI = playerController.UIManager.WaterGauge;
        _waterGaugeUI.GetComponent<Image>().fillAmount = 0f;
    }

    public override void Unequip(PlayerController playerController)
    {
        if (IsBuffActive)
            DeactivateBuff();

        WaterGauge = 0f;
        //여기서 물속성 폼 전용 UI를 끌 거임.
        playerController.UIManager.WaterGauge.SetActive(false);
        playerController.UIManager.WaterElement.SetActive(true);
        _waterGaugeUI.GetComponent<Image>().fillAmount = 0f;
    }

    // ── 게이지 충전: 적 타격 시 호출 ─────────────────────────────────────────
    protected override void OnHitEnemy(GameObject enemy)
    {
        WaterGauge = Mathf.Min(WaterGauge + GaugePerHit, MaxWaterGauge);
        _waterGaugeUI.GetComponent<Image>().fillAmount = WaterGauge / MaxWaterGauge;
        EvaluateBuff();
    }

    // ── 매 프레임 게이지 감소 ─────────────────────────────────────────────────
    protected override void OnTick(float deltaTime)
    {
        if (WaterGauge <= 0f) return;

        WaterGauge = Mathf.Max(0f, WaterGauge - GaugeDrainRate * deltaTime);
        _waterGaugeUI.GetComponent<Image>().fillAmount = WaterGauge / MaxWaterGauge;
        EvaluateBuff();
    }

    private void EvaluateBuff()
    {
        bool shouldActivate = WaterGauge >= BuffThreshold;

        if (shouldActivate && !IsBuffActive)
            ActivateBuff();
        else if (!shouldActivate && IsBuffActive)
            DeactivateBuff();
    }

    private void ActivateBuff()
    {
        IsBuffActive = true;
        _playerController.StatManager.AddModifier(StatType.ST_ATK_SPD, 0f, AtkSpdBonus);
        _playerController.StatManager.AddModifier(StatType.ST_ATK, 0f, 100);
        _playerController.MoveSpeed = _baseMoveSpeed * MoveSpdMultiplier;
        _playerController.RunSpeed = _baseRunSpeed * MoveSpdMultiplier;
    }

    private void DeactivateBuff()
    {
        IsBuffActive = false;
        _playerController.StatManager.RemoveModifier(StatType.ST_ATK_SPD, 0f, AtkSpdBonus);
        _playerController.StatManager.RemoveModifier(StatType.ST_ATK, 0f, 100);
        _playerController.MoveSpeed = _baseMoveSpeed;
        _playerController.RunSpeed = _baseRunSpeed;
    }

    // ── 공격 업데이트 ─────────────────────────────────────────────────────────
    public override void UpdateAttack(out bool isComplete)
    {
        isComplete = false;
        if (_weaponActionData == null || _weaponActionData.ComboSteps.Count == 0)
        {
            isComplete = true;
            return;
        }

        _timer += Time.deltaTime * AttackSpeed;
        WeaponActionData currentStep = _weaponActionData.ComboSteps[_comboStep];

        ProcessHit(currentStep);

        Vector3 moveVelocity = _playerController.transform.forward * currentStep.ForwardThrust;
        moveVelocity.y = _playerController.VerticalVelocity;
        _playerController.Controller.Move(moveVelocity * Time.deltaTime);

        if (_timer >= currentStep.ComboTransitionTime &&
            _playerController.Input.AttackBuffer.IsActive &&
            _comboStep < _weaponActionData.ComboSteps.Count - 1)
        {
            _comboStep++;
            PlayCombo();
            return;
        }

        if (_timer >= currentStep.Duration)
            isComplete = true;
    }

    public override void BeginSkill()
    {
        base.BeginSkill();
        FindSoftTarget();
        if (_softTarget != null)
        {
            Vector3 dir = _softTarget.position - _playerController.transform.position;
            dir.y = 0f;
            Quaternion targetRot = Quaternion.LookRotation(dir);
            _playerController.transform.rotation = targetRot;
        }
        _playerController.Controller.excludeLayers |= 1 << LayerMask.NameToLayer("Enemy");
        PlayStepSound(_weaponActionData.SkillSteps, 0);
    }

    public override void UpdateSkill(out bool isComplete)
    {
        _softTarget = null;
        isComplete = false;
        if (_weaponActionData == null || _weaponActionData.SkillSteps == null || _weaponActionData.SkillSteps.Count == 0) { isComplete = true; return; }

        _timer += Time.deltaTime * AttackSpeed;
        WeaponActionData step = _weaponActionData.SkillSteps[_skillStep];
        ProcessHit(step);
        MoveForward(step);

        if (_timer >= step.Duration)
        {
            if (_skillStep < _weaponActionData.SkillSteps.Count - 1) { _skillStep++; PlaySkillStep(); }
            else isComplete = true;
        }
    }

    public override void EndSkill()
    {
        base.EndSkill();
        _playerController.Controller.excludeLayers &= ~(1 << LayerMask.NameToLayer("Enemy"));
    }

    public override void BeginUltimate()
    {
        base.BeginUltimate();
        SpawnStepVFX(_weaponActionData.UltimateSteps, 0);
    }
}

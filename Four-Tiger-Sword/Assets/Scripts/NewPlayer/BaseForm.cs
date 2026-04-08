using UnityEngine;
using System.Collections.Generic;

public abstract class BaseForm : IForm
{
    protected PlayerController _playerController;
    protected WeaponActionDataSO _weaponActionData;

    protected int _comboStep = 0;
    protected float _timer = 0;

    protected LayerMask _enemyLayer;
    protected HashSet<Collider> _hitTargets = new HashSet<Collider>();

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
        _hitTargets.Clear();
    }

    protected void PlayCombo()
    {
        _timer = 0;
        _playerController.Input.AttackBuffer.Consume();
        _hitTargets.Clear();

        WeaponActionData step = _weaponActionData.ComboSteps[_comboStep];

        Debug.Log("공격이름 : " + step.AnimationName + " 공격타수 : " + _comboStep);

        //_playerController.Animator.CrossFade(step.AnimationName);
    }

    protected void ProcessHit()
    {
        WeaponActionData currentStep = _weaponActionData.ComboSteps[_comboStep];

        if (_timer >= currentStep.HitStartTime && _timer <= currentStep.HitStartTime + currentStep.HitDuration)
        {
            Vector3 hitboxCenter = _playerController.transform.position + (_playerController.transform.rotation * currentStep.HitBoxOffset);

            Collider[] hits = new Collider[0];

            switch (currentStep.HitBoxShape)
            {
                case HitBoxShape.Sphere:
                    hits = Physics.OverlapSphere(hitboxCenter, currentStep.HitBoxRadius, _enemyLayer);
                    break;
                case HitBoxShape.Box:
                    hits = Physics.OverlapBox(hitboxCenter, currentStep.HitBoxSize, _playerController.transform.rotation, _enemyLayer);
                    break;
                case HitBoxShape.Capsule:
                    float pointOffset = (currentStep.HitBoxHeight / 2f) - currentStep.HitBoxRadius;
                    Vector3 point1 = hitboxCenter + _playerController.transform.up * pointOffset;
                    Vector3 point2 = hitboxCenter - _playerController.transform.up * pointOffset;
                    hits = Physics.OverlapCapsule(point1, point2, currentStep.HitBoxRadius, _enemyLayer);
                    break;
            }

            foreach (var hit in hits)
            {
                if (!_hitTargets.Contains(hit))
                {
                    _hitTargets.Add(hit);
                    Debug.Log($"적 <color=red>{hit.name}</color>에게 <color=yellow>{currentStep.Damage}</color> 데미지 적중! (모양: {currentStep.HitBoxShape})");
                    hit.GetComponent<IDamageable>().TakeDamage(currentStep.Damage);
                    //여기서 피격당한 적의 체력깎는거 호출하면 될듯.
                }
            }
        }
    }

    public virtual void DrawHitboxGizmo() //기즈모그리는 함수(테스트용)
    {
        // 1. 공격 데이터가 없거나, 현재 공격 상태가 아니면 그리지 않음
        if (_weaponActionData == null || _weaponActionData.ComboSteps.Count == 0) return;

        // StateMachine에서 현재 상태가 AttackState인지 확인! (공격 중일 때만 그려야 함)
        if (!(_playerController.StateMachine.CurrentState is PlayerAttackState)) return;

        WeaponActionData currentStep = _weaponActionData.ComboSteps[_comboStep];

        // 2. 공격 중이더라도, SO에 설정된 판정 시간(HitStartTime ~ HitDuration)에만 그려야 진짜 히트박스를 볼 수 있습니다.
        if (_timer >= currentStep.HitStartTime && _timer <= (currentStep.HitStartTime + currentStep.HitDuration))
        {
            // 기즈모 색상을 반투명한 빨간색으로 설정
            Gizmos.color = new Color(1f, 0f, 0f, 0.4f);

            // 히트박스 중심 위치 (플레이어 위치 + 앞/옆/위 오프셋)
            Vector3 hitboxCenter = _playerController.transform.position + (_playerController.transform.rotation * currentStep.HitBoxOffset);

            // 3. 모양에 따라 다르게 그리기
            switch (currentStep.HitBoxShape)
            {
                case HitBoxShape.Sphere:
                    Gizmos.DrawSphere(hitboxCenter, currentStep.HitBoxRadius);
                    break;

                case HitBoxShape.Box:
                    // 박스는 기즈모 매트릭스를 플레이어의 회전값과 맞춰주어야 삐딱하게 그려집니다.
                    Matrix4x4 oldMatrix = Gizmos.matrix;
                    Gizmos.matrix = Matrix4x4.TRS(hitboxCenter, _playerController.transform.rotation, Vector3.one);
                    // Extents는 "절반" 크기이므로, 그릴 때는 곱하기 2를 해서 전체 크기로 그려야 실제 판정과 똑같아집니다.
                    Gizmos.DrawCube(Vector3.zero, currentStep.HitBoxSize * 2f);
                    Gizmos.matrix = oldMatrix; // 원래대로 복구
                    break;

                case HitBoxShape.Capsule:
                    // 유니티 2022 이상부터는 Gizmos에 캡슐 그리기 기능이 없어서, 보통 구 2개와 선으로 비슷하게 흉내냅니다.
                    float pointOffset = (currentStep.HitBoxHeight / 2f) - currentStep.HitBoxRadius;
                    Vector3 p1 = hitboxCenter + _playerController.transform.up * pointOffset;
                    Vector3 p2 = hitboxCenter - _playerController.transform.up * pointOffset;

                    Gizmos.DrawSphere(p1, currentStep.HitBoxRadius);
                    Gizmos.DrawSphere(p2, currentStep.HitBoxRadius);
                    Gizmos.DrawLine(p1, p2); // 캡슐 몸통 대신 선을 그음
                    break;
            }
        }
    }
}

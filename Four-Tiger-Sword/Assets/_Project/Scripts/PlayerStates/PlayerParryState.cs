using UnityEngine;

public class PlayerParryState : IPlayerState
{
    private readonly PlayerController _playerController;

    // 패링 진입 후 첫 타가 오기를 기다리는 시간
    private const float PreHitWindowDuration = 1f;
    // 첫 성공 이후 적 공격 액션이 끝나고 패링 판정이 닫히기까지의 딜레이
    private const float PostActionWindowDuration = 0.01f;
    // 반격 버튼을 누를 수 있는 시간
    private const float CounterWindowDuration = 0.5f;

    private float _timer;
    // 첫 번째 히트를 막은 EnemyAction 인스턴스 — 멀티 히트에서 어느 적의 예고로 타이머를 동결할지 특정
    private object _lockedSource;
    private IForm _parryForm;
    private IForm _requestedForm;
    private uint _successAttackPressId;

    /// <summary>현재 패링 판정 구간에 있는지 여부.</summary>
    public bool IsParryWindowActive { get; private set; }

    /// <summary>반격 가능 구간. PlayerStateMachineSetup에서 counter 상태 전환 조건에 사용.</summary>
    public bool IsCounterWindowActive { get; private set; }

    /// <summary>패링 성공 후 새로 누른 공격만 반격으로 사용합니다.</summary>
    public bool CanCounter => IsCounterWindowActive
        && _playerController.Input.AttackBuffer.IsActive
        && _playerController.Input.AttackPressId != _successAttackPressId;

    /// <summary>패링 모션 전체(반격 윈도우 포함)가 끝났는지 여부.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>이번 패링 상태에서 막은 총 타격 횟수.</summary>
    public int ParriedHitCount { get; private set; }

    public PlayerParryState(PlayerController playerController)
    {
        _playerController = playerController;
    }

    public void Enter()
    {
        _timer = 0f;
        _lockedSource = null;
        IsParryWindowActive = true;
        IsCounterWindowActive = false;
        IsComplete = false;
        ParriedHitCount = 0;
        _parryForm = _playerController.FormManager.CurrentForm;
        _requestedForm = _playerController.FormManager.GetRequestedForm();
        _successAttackPressId = _playerController.Input.AttackPressId;

        _playerController.Input.AttackBuffer.Consume();
        _parryForm.BeginParry();
    }

    public void Update()
    {
        if (IsParryWindowActive)
        {
            if (ParriedHitCount == 0)
            {
                // 아직 아무것도 막지 않음: 첫 타 대기 타이머
                _timer += _playerController.ActionDeltaTime;
                if (_timer >= PreHitWindowDuration)
                {
                    IsParryWindowActive = false;
                    IsComplete = true; // 아무것도 못 막았으니 반격 없이 종료
                }
            }
            else
            {
                // 최소 1타 이상 막음: 연격 추적 및 자동 종료 타이머
                bool telegraphActive = ParryEventBus.IsTelegraphActive(_lockedSource);
                bool actionNotDone = !((_lockedSource as EnemyAction)?.IsFinished ?? true);

                if (telegraphActive || actionNotDone)
                    _timer = 0f;
                else
                    _timer += _playerController.ActionDeltaTime;

                if (_timer >= PostActionWindowDuration)
                {
                    IsParryWindowActive = false;
                    _timer = 0f; // 반격 윈도우 카운트다운을 위해 타이머 리셋
                }
            }
        }
        else if (IsCounterWindowActive)
        {
            // IsParryWindowActive가 꺼진 후 반격 윈도우 시간을 별도로 카운트
            // (IsParryWindowActive 블록 안에서 처리하면 다음 프레임부터 타이머가 멈춰버리는 버그 발생)
            _timer += _playerController.ActionDeltaTime;
            if (_timer >= CounterWindowDuration)
            {
                IsCounterWindowActive = false;
                IsComplete = true;
            }
        }
    }

    public void Exit()
    {
        _parryForm.EndParry();
        _playerController.Input.ConsumeFormInput();
        IsParryWindowActive = false;
        IsCounterWindowActive = false;
        _requestedForm = null;
    }

    /// <summary>
    /// 패링 판정 구간에서 적의 공격이 막혔을 때 PlayerController.TakeDamage에서 호출합니다.
    /// </summary>
    public bool OnHitParried(object attackerSource)
    {
        bool firstHit = ParriedHitCount == 0;

        if (firstHit)
        {
            if (!_playerController.FormManager.TrySwitchForm(_requestedForm, FormManager.SwitchReason.Parry))
            {
                IsParryWindowActive = false;
                IsComplete = true;
                return false;
            }
            _lockedSource = attackerSource;
            _parryForm.EndParry();
            _parryForm = _playerController.FormManager.CurrentForm;
            _parryForm.BeginParry();
            _requestedForm = null;

            // 성공 이전에 누르거나 계속 누르고 있던 공격은 반격으로 이어지지 않습니다.
            _successAttackPressId = _playerController.Input.AttackPressId;
            _playerController.Input.AttackBuffer.Consume();
            IsCounterWindowActive = true;
        }
        ParriedHitCount++;

        if (attackerSource == _lockedSource)
            _timer = 0f;
        return true;
    }

}

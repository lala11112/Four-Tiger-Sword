using UnityEngine;

public class PlayerParryState : IPlayerState
{
    private readonly PlayerController _playerController;

    // 예고 종료 후 첫 타가 오기를 기다리는 시간 (_lockedSource == null, 아직 아무것도 못 막은 상태)
    private const float PreHitWindowDuration = 1f;
    // 공격 액션 종료 후 반격 윈도우가 열리기까지의 딜레이 (_lockedSource != null, 이미 1타 이상 막은 상태)
    // 작을수록 반격이 빠르게 느껴짐 (0.05 ~ 0.15 권장)
    private const float PostActionWindowDuration = 0.01f;
    // 반격 버튼을 누를 수 있는 시간
    private const float CounterWindowDuration = 0.5f;

    private float _timer;
    // 첫 번째 히트를 막은 EnemyAction 인스턴스 — 멀티 히트에서 어느 적의 예고로 타이머를 동결할지 특정
    private object _lockedSource;

    /// <summary>현재 패링 판정 구간에 있는지 여부.</summary>
    public bool IsParryWindowActive { get; private set; }

    /// <summary>반격 가능 구간. PlayerStateMachineSetup에서 counter 상태 전환 조건에 사용.</summary>
    public bool IsCounterWindowActive { get; private set; }

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

        _playerController.Input.ParryBuffer.Consume();
        _playerController.FormManager.CurrentForm.BeginParry();
    }

    public void Update()
    {
        if (IsParryWindowActive)
        {
            if (_lockedSource == null)
            {
                // 아직 아무것도 막지 않음: 첫 타 대기 타이머
                _timer += Time.deltaTime;
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
                    _timer += Time.deltaTime;

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
            _timer += Time.deltaTime;
            if (_timer >= CounterWindowDuration)
            {
                IsCounterWindowActive = false;
                IsComplete = true;
            }
        }
    }

    public void Exit()
    {
        _playerController.FormManager.CurrentForm.EndParry();
        IsParryWindowActive = false;
        IsCounterWindowActive = false;
        _playerController.StartParryCooldown();
    }

    /// <summary>
    /// 패링 판정 구간에서 적의 공격이 막혔을 때 PlayerController.TakeDamage에서 호출합니다.
    /// </summary>
    public void OnHitParried(object attackerSource)
    {
        if (_lockedSource == null)
        {
            _lockedSource = attackerSource;

            // ★ 핵심: 첫 타격을 막은 '그 프레임'에 즉시 반격 윈도우를 오픈합니다!
            // 이제 상태머신(StateMachineSetup)은 유저가 공격 버튼을 누르는 순간 즉시 Counter 상태로 전이할 수 있습니다.
            IsCounterWindowActive = true;
        }

        ParriedHitCount++;

        if (attackerSource == _lockedSource)
            _timer = 0f;
    }

}

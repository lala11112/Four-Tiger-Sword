using UnityEngine;

public class PlayerParryState : IPlayerState
{
    private readonly PlayerController _playerController;

    // 패링 판정이 열려 있는 시간
    private const float ActiveWindowDuration  = 0.2f;
    // 판정 윈도우 + 경직 = 일반 패링 전체 지속 시간
    private const float TotalDuration         = 0.55f;
    // 퍼펙트 패링 성공 후 반격 가능 시간
    private const float CounterWindowDuration = 0.5f;

    private float  _timer;
    private bool   _enteredDuringTelegraph; // 입력 시점에 적 예고가 활성 상태였는지
    private object _lockedSource;           // 첫 번째 패링에 성공한 EnemyAction 인스턴스

    /// <summary>
    /// 현재 패링 판정 구간에 있는지 여부.
    /// PlayerController.IsParryActive 및 TakeDamage에서 사용합니다.
    /// </summary>
    public bool IsParryWindowActive { get; private set; }

    /// <summary>
    /// 퍼펙트 패링 성공 후 열리는 반격 가능 구간.
    /// PlayerStateMachineSetup에서 attack 상태 전환 조건에 사용합니다.
    /// </summary>
    public bool IsCounterWindowActive { get; private set; }

    /// <summary>
    /// 적의 예고 중에 패링 입력을 했고, 해당 공격을 최소 1회 이상 막았을 때 true.
    /// </summary>
    public bool IsPerfectParry { get; private set; }

    /// <summary>이번 패링 상태에서 성공한 총 패링 횟수.</summary>
    public int ParriedHitCount { get; private set; }

    /// <summary>패링 모션 전체(및 반격 윈도우 포함)가 끝났는지 여부.</summary>
    public bool IsComplete { get; private set; }

    public PlayerParryState(PlayerController playerController)
    {
        _playerController = playerController;
    }

    public void Enter()
    {
        _timer                = 0f;
        IsParryWindowActive   = true;
        IsCounterWindowActive = false;
        IsComplete            = false;
        IsPerfectParry        = false;
        ParriedHitCount       = 0;
        _lockedSource         = null;

        // 입력 시점에 적이 예고 중이면 퍼펙트 패링 가능 상태
        _enteredDuringTelegraph = ParryEventBus.IsAnyTelegraphActive;

        _playerController.Input.ParryBuffer.Consume();
        _playerController.Animator.CrossFade("Parry", 0.05f);
    }

    public void Update()
    {
        _timer += Time.deltaTime;

        // ── 1. 패링 판정 윈도우 ──────────────────────────────────────────────
        if (IsParryWindowActive)
        {
            // 멀티히트: 잠긴 소스(첫 번째 패링한 그 적)의 예고가 아직 활성 → 윈도우 타이머 고정
            // 다른 적의 예고가 활성이어도 무시 — 이것이 이전 버그의 핵심 수정 지점
            if (_lockedSource != null && ParryEventBus.IsTelegraphActive(_lockedSource))
                _timer = 0f;

            if (_timer >= ActiveWindowDuration)
            {
                IsParryWindowActive = false;
                // 퍼펙트 패링이 확정됐다면 반격 윈도우 자동 개방
                if (IsPerfectParry)
                    OpenCounterWindow();
            }
        }

        // ── 2. 반격 가능 윈도우 (퍼펙트 패링 이후) ──────────────────────────
        if (IsCounterWindowActive)
        {
            if (_timer >= CounterWindowDuration)
            {
                IsCounterWindowActive = false;
                IsComplete            = true;
            }
            return;
        }

        // ── 3. 일반 경직 종료 ────────────────────────────────────────────────
        if (!IsParryWindowActive && _timer >= TotalDuration)
            IsComplete = true;
    }

    public void Exit()
    {
        IsParryWindowActive   = false;
        IsCounterWindowActive = false;
        _playerController.StartParryCooldown();
    }

    /// <summary>
    /// 패링 판정 구간에서 적의 공격이 막혔을 때 PlayerController.TakeDamage에서 호출합니다.
    /// </summary>
    /// <param name="attackerSource">공격을 실행한 EnemyAction 인스턴스. ParryEventBus.CurrentAttackSource에서 전달됩니다.</param>
    public void OnHitParried(object attackerSource)
    {
        // 첫 번째 패링: 소스를 잠금 → 이후 이 소스의 콤보에 대해서만 윈도우를 연장
        if (_lockedSource == null)
            _lockedSource = attackerSource;

        ParriedHitCount++;

        // 첫 번째 패링 + 예고 중 입력 → 퍼펙트 패링 확정
        if (ParriedHitCount == 1 && _enteredDuringTelegraph)
            IsPerfectParry = true;

        // 잠긴 소스의 콤보 타격: 다음 타수를 위해 타이머 초기화
        // 다른 적의 공격(attackerSource != _lockedSource)은 막아주되 윈도우를 연장하지 않음
        if (attackerSource == _lockedSource)
            _timer = 0f;
    }

    private void OpenCounterWindow()
    {
        _timer                = 0f;
        IsCounterWindowActive = true;
    }
}

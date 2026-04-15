using System;
using UnityEngine;

/// <summary>
/// 스킬 / 필살기를 구성하는 단일 페이즈 단위.
/// duration, onBegin, onUpdate, onEnd 콜백을 조합해서 원하는 동작을 구성합니다.
///
/// 사용 예)
///   // 칼 휘두르기 페이즈
///   new SkillPhase(duration: step.Duration, onUpdate: _ => ProcessHit(step))
///
///   // 1초 대기 페이즈
///   new SkillPhase(duration: 1f)
///
///   // 폭발 페이즈 (Begin에서 즉시 1회 발동)
///   new SkillPhase(duration: 0.1f, onBegin: () => TriggerExplosion())
/// </summary>
public class SkillPhase
{
    public float Duration { get; }

    private readonly Action         _onBegin;
    private readonly Action<float>  _onUpdate; // 인자: 페이즈 내부 경과 시간
    private readonly Action         _onEnd;

    private float _timer;

    public SkillPhase(float duration,
                      Action<float> onUpdate = null,
                      Action        onBegin  = null,
                      Action        onEnd    = null)
    {
        Duration  = duration;
        _onUpdate = onUpdate;
        _onBegin  = onBegin;
        _onEnd    = onEnd;
    }

    public void Begin()
    {
        _timer = 0f;
        _onBegin?.Invoke();
    }

    public void Update(out bool isComplete)
    {
        _timer += Time.deltaTime;
        _onUpdate?.Invoke(_timer);
        isComplete = _timer >= Duration;
    }

    public void End() => _onEnd?.Invoke();
}

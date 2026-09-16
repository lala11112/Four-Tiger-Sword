using System;
using UnityEngine;

/// <summary>
/// 적의 경직(Poise) 시스템을 관리합니다.
/// 경직 수치가 소진되면 자세가 무너지며 잠시 무력화됩니다.
/// </summary>
public class PoiseHandler : MonoBehaviour
{
    [SerializeField] private float _maxPoise       = 100f;
    [SerializeField] private float _poiseRegen     = 20f;  // 초당 자동 회복량
    [SerializeField] private float _breakDuration  = 2f;   // 자세 붕괴 지속 시간

    private float _currentPoise;
    private float _breakTimer;

    public bool IsPoiseBreaking => _breakTimer > 0f;
    public float PoiseRatio     => _currentPoise / _maxPoise;

    public event Action OnPoiseBreak;

    private void Awake() => _currentPoise = _maxPoise;

    /// <summary>경직 데미지를 입힙니다. 자세 붕괴 중에는 무시됩니다.</summary>
    public void TakePoiseDamage(float amount)
    {
        if (IsPoiseBreaking || IsCrowdControlled()) return;

        _currentPoise -= amount;
        if (_currentPoise <= 0f)
            TriggerBreak();
    }

    //현재 경직 수치와 상관 없이 그로기에 들어가게 할때 사용용
    public void ForceBreak()
    {
        if (IsPoiseBreaking || IsCrowdControlled()) return;
        _currentPoise = 0f;
        TriggerBreak();
    }

    private void TriggerBreak()
    {
        _currentPoise = 0f;
        _breakTimer   = _breakDuration;
        GetComponent<Enemy>().PendingGroggy = true;
        //OnPoiseBreak?.Invoke();
    }

    private bool IsCrowdControlled()
    {
        var enemy = GetComponent<Enemy>();
        return enemy != null && (enemy.IsGroggy || enemy.PendingGroggy);
    }

    public void ResetPoise()
    {
        _currentPoise = _maxPoise;
        _breakTimer = 0f;
    }

    private void Update()
    {
        if (_breakTimer > 0f)
        {
            _breakTimer -= Time.deltaTime;
            return;
        }

        if (_currentPoise < _maxPoise)
            _currentPoise = Mathf.Min(_currentPoise + _poiseRegen * Time.deltaTime, _maxPoise);
    }
}

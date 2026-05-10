using UnityEngine;

/// <summary>
/// 토(土) 폼 전용: 방어 누적 스탯.
/// 궁극기 시전 중 수신한 피해를 에너지로 전환해 반격에 활용합니다. (SRP)
/// </summary>
public class EarthAbsorptionStat
{
    private float _absorbed;

    public float AbsorbedAmount => _absorbed;
    public bool  IsAbsorbing    { get; private set; }

    public void StartAbsorbing()
    {
        IsAbsorbing = true;
        _absorbed   = 0f;
        Debug.Log("<color=brown>[방어 누적] 피해 흡수 시작</color>");
    }

    /// <summary>PlayerStatManager.OnDamageTaken 이벤트에서 호출됩니다.</summary>
    public void Absorb(int damage)
    {
        if (!IsAbsorbing) return;
        _absorbed += damage;
    }

    /// <summary>흡수를 종료하고 누적된 총량을 반환합니다.</summary>
    public float StopAndGet()
    {
        IsAbsorbing = false;
        float result = _absorbed;
        _absorbed   = 0f;
        Debug.Log($"<color=brown>[방어 누적] 흡수 종료 — 총 흡수량: {result}</color>");
        return result;
    }
}

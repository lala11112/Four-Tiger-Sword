using UnityEngine;

/// <summary>
/// 적의 전투 스탯(방어력, 약점 여부 등)을 관리합니다.
/// StatusEffectHandler와 분리되어 단일 책임을 유지합니다. (SRP)
/// </summary>
public class EnemyStatHandler : MonoBehaviour
{
    [SerializeField] private float _baseDef = 0f;

    private float _defReduction    = 0f;
    private bool  _isWeakPointExposed = false;

    public float BaseDef            => _baseDef;
    public float EffectiveDef       => Mathf.Max(0f, _baseDef - _defReduction);
    public bool  IsWeakPointExposed => _isWeakPointExposed;

    public void ReduceDefense(float amount)
    {
        _defReduction = Mathf.Min(_defReduction + amount, _baseDef);
    }

    public void RestoreDefense(float amount)
    {
        _defReduction = Mathf.Max(0f, _defReduction - amount);
    }

    public void SetWeakPoint(bool exposed)
    {
        _isWeakPointExposed = exposed;
    }
}

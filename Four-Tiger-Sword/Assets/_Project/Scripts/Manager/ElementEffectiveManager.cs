using System;
using UnityEngine;
using System.Collections.Generic;

[Serializable]
public class ElementEffective
{
    public ElementType AttackType;
    public ElementType DefenseType;
    [Min(0f)] public float multiplier = 1f;
}

[CreateAssetMenu(fileName = "ElementEffectiveManager", menuName = "Combat/Element Matchup Table")]
public class ElementEffectiveManager : ScriptableObject
{
    public List<ElementEffective> elementEffectiveList = new();
    private Dictionary<(ElementType, ElementType), float> _lookup;
    private bool _valid;

    private void OnEnable() => InvalidateCache();
    private void OnValidate() => InvalidateCache();

    // Runtime callers that edit the list must invalidate it afterwards.
    public void InvalidateCache() { _lookup = null; _valid = false; }

    public void Initialize()
    {
        InvalidateCache();
        ValidateTable();
    }

    [ContextMenu("Validate Matchup Table")]
    private void ValidateFromMenu()
    {
        Initialize();
        if (_valid) Debug.Log("속성 상성표 검증 완료: 모든 조합이 유효합니다.", this);
    }

    public bool ValidateTable()
    {
        if (_lookup != null) return _valid;
        _lookup = new Dictionary<(ElementType, ElementType), float>();
        var errors = new List<string>();
        if (elementEffectiveList == null) errors.Add("상성 목록이 없습니다.");
        else foreach (var item in elementEffectiveList)
        {
            if (item == null) { errors.Add("비어 있는 항목이 있습니다."); continue; }
            if (!Enum.IsDefined(typeof(ElementType), item.AttackType)
                || !Enum.IsDefined(typeof(ElementType), item.DefenseType))
            { errors.Add("정의되지 않은 속성이 있습니다."); continue; }
            var key = (item.AttackType, item.DefenseType);
            if (float.IsNaN(item.multiplier) || float.IsInfinity(item.multiplier) || item.multiplier < 0f)
            { errors.Add($"잘못된 배율: {key}"); continue; }
            if (!_lookup.TryAdd(key, item.multiplier)) errors.Add($"중복 조합: {key}");
        }
        foreach (ElementType attack in Enum.GetValues(typeof(ElementType)))
            foreach (ElementType defense in Enum.GetValues(typeof(ElementType)))
                if (!_lookup.ContainsKey((attack, defense)))
                    errors.Add($"누락 조합: {attack} → {defense}");
        _valid = errors.Count == 0;
        if (!_valid) Debug.LogError("속성 상성표 오류. 피해 계산을 중단합니다.\n" + string.Join("\n", errors), this);
        return _valid;
    }

    public bool TryGetElementEffective(ElementType attack, ElementType defense, out float multiplier)
    {
        multiplier = 0f;
        return ValidateTable() && _lookup.TryGetValue((attack, defense), out multiplier);
    }

    public float GetElementEffective(ElementType attack, ElementType defense)
    {
        if (TryGetElementEffective(attack, defense, out float multiplier)) return multiplier;
        throw new InvalidOperationException("유효하지 않은 속성 상성표 또는 속성 조합입니다.");
    }
}

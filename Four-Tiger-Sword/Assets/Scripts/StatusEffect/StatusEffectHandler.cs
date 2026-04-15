using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 적 GameObject에 부착되어 활성 상태이상 목록을 관리합니다.
/// IStatusEffect를 새로 추가해도 이 클래스를 수정할 필요가 없습니다. (OCP)
/// </summary>
public class StatusEffectHandler : MonoBehaviour
{
    private readonly List<IStatusEffect> _effects  = new();
    private readonly List<IStatusEffect> _toRemove = new();

    public event Action<IStatusEffect> OnEffectApplied;
    public event Action<IStatusEffect> OnEffectRemoved;

    /// <summary>
    /// 상태이상을 적용합니다. 동일 타입이 이미 있으면 갱신(재적용)합니다.
    /// </summary>
    public void Apply(IStatusEffect effect)
    {
        var existing = Find(effect.GetType());
        if (existing != null)
            Remove(existing);

        _effects.Add(effect);
        effect.OnApply(gameObject);
        OnEffectApplied?.Invoke(effect);
    }

    public bool Has<T>() where T : IStatusEffect
        => _effects.Exists(e => e is T);

    public T Get<T>() where T : class, IStatusEffect
        => _effects.Find(e => e is T) as T;

    private IStatusEffect Find(Type type)
        => _effects.Find(e => e.GetType() == type);

    private void Remove(IStatusEffect effect)
    {
        effect.OnRemove(gameObject);
        _effects.Remove(effect);
        OnEffectRemoved?.Invoke(effect);
    }

    private void Update()
    {
        _toRemove.Clear();

        foreach (var effect in _effects)
        {
            effect.OnUpdate(Time.deltaTime);
            if (effect.IsExpired)
                _toRemove.Add(effect);
        }

        foreach (var effect in _toRemove)
            Remove(effect);
    }
}

using UnityEngine;
using System;

public class Enemy : MonoBehaviour, IDamageable
{
    [SerializeField] private int _maxHp = 100;
    private int _currentHp;

    public int CurrentHp => _currentHp;

    public event Action<int, DamageType, bool> OnDamaged;
    public event Action OnDied;

    private void Start()
    {
        _currentHp = _maxHp;
        DamageTextManager.Instance?.Register(this);
    }

    public void TakeDamage(int damage, DamageType damageType = DamageType.Normal, bool isCritical = false)
    {
        if (_currentHp <= 0) return;

        _currentHp = Mathf.Max(0, _currentHp - damage);
        OnDamaged?.Invoke(damage, damageType, isCritical);

        if (_currentHp <= 0)
        {
            OnDied?.Invoke();
            Die();
        }
    }

    private void Die()
    {
        Debug.Log($"{name} 사망");
        Destroy(gameObject);
    }
}

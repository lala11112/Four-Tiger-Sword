using UnityEngine;

[CreateAssetMenu(fileName = "StackData", menuName = "StackDataSO")]
public class StackDataSO : ScriptableObject
{
    public StackType stackType;
    public int maxStack;
    public float resetTime;

    public float explosionDamage;

    public GameObject explosionEffect; // 이팩트
    public AudioClip explosionSound; // 효과음


    public virtual void OnExplosion(GameObject target)
    {
        target.GetComponent<IDamageable>()
              ?.TakeDamage((int)explosionDamage, DamageType.Normal, false);
    }
}
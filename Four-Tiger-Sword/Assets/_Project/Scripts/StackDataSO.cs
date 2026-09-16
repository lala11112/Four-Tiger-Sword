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
        var receiver = target.GetComponentInParent<IDamageable>();
        if (receiver is Component component)
            DamageManager.Apply(new HitInfo(explosionDamage, ElementType.ELEMENT_NONE, 0f, 1f, poiseDamage: 20f),
                receiver, component.gameObject);
    }
}

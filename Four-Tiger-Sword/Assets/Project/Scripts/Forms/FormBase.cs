using UnityEngine;

public abstract class FormBase : MonoBehaviour
{
    protected int maxComboCount;
    protected int currentComboCount = 0;

    protected float resetTime = 1.2f;

    [SerializeField] GameObject Weapon;

    //[SerializeField] protected WeaponActionData[] combo;

    public virtual void EnterForm(){
        currentComboCount = 0;
    }

    public virtual void ExitForm(){
        currentComboCount = 0;
    }

    public abstract void Attack();

    public abstract void Skill();

    public abstract void Ultimate();
}

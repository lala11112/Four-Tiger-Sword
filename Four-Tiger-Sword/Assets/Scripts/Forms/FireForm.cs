/*
using UnityEngine;

public class FireForm : FormBase
{
    private float lastAttackTime = 0;

    //private PlayerAnimation playerAnimation;

    private WeaponActionExecutor weaponActionExecutor;

    void Start()
    {
        //playerAnimation = GetComponent<PlayerAnimation>();
        maxComboCount = combo.Length;
        weaponActionExecutor = GetComponentInParent<WeaponActionExecutor>();
    }

    public override void Attack()
    {
        if(combo == null || combo.Length == 0) return;

        if(currentComboCount > 0)
        {
            if(Time.time - lastAttackTime > resetTime)
            {
                currentComboCount = 0;
                Debug.Log("Combo reset");   
            }
        }

        WeaponActionData actionData = combo[currentComboCount];

        weaponActionExecutor.ExecuteAttack(actionData);

        lastAttackTime = Time.time;
        currentComboCount++;

        if(currentComboCount >= maxComboCount)
        {
            currentComboCount = 0;
        }
    }

    public override void Skill()
    {
        throw new System.NotImplementedException();
    }

    public override void Ultimate()
    {
        throw new System.NotImplementedException();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color  = new Color(1f, 0f, 0f, 0.4f);
        Vector3 center = transform.position + transform.forward;
        Gizmos.matrix  = Matrix4x4.TRS(center, transform.rotation, Vector3.one);
        Gizmos.DrawCube(Vector3.zero, combo[currentComboCount].hitbox);
    }
}

*/
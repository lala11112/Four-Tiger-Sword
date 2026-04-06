using UnityEngine;

public class RockForm : FormBase
{
    public override void Attack()
    {
        Debug.Log("RockForm Attack");
    }

    public override void Skill()
    {
        throw new System.NotImplementedException();
    }

    public override void Ultimate()
    {
        throw new System.NotImplementedException();
    }
}

using UnityEngine;

public class WoodForm : FormBase
{
    public override void Attack()
    {
        Debug.Log("WoodForm Attack");
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

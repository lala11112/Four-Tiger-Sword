using UnityEngine;

public class IronForm : FormBase
{
    public override void Attack()
    {
        Debug.Log("IronForm Attack");
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

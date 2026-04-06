using UnityEngine;

public class Ground : MonoBehaviour
{
    public string name;
    public float friction;

    public virtual void Awake()
    {
        gameObject.layer = LayerMask.NameToLayer("Ground");
    }
}

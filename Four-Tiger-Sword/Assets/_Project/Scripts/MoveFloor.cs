using UnityEngine;

public class MoveFloor : MonoBehaviour
{
    Transform tr;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        tr = GetComponent<Transform>();
    }

    // Update is called once per frame
    void Update()
    {
        tr.Translate(Vector3.forward * Time.deltaTime * 3f);
    }
}

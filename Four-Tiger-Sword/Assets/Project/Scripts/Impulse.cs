using UnityEngine;
using Unity.Cinemachine;

public class Impulse : MonoBehaviour
{
    private CinemachineImpulseSource cinemachineImpulseSource;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        cinemachineImpulseSource = GetComponent<CinemachineImpulseSource>();
    }

    // Update is called once per frame
    void Update()
    {
        if(Input.GetKeyDown(KeyCode.UpArrow))
        {
            cinemachineImpulseSource.GenerateImpulse();
        }
    }


}

using UnityEngine;
using Unity.Cinemachine;

//나중에 대거 수정 필요

public class CameraMove : MonoBehaviour
{
    public float baseFOV = 60f;

    [SerializeField] private CinemachineCamera cinemachineCamera;
    [SerializeField] private float fovSmoothSpeed = 10f;

    private float targetFOV;

    private void Awake()
    {
        targetFOV = baseFOV;
    }

    private void Update()
    {
        var lens = cinemachineCamera.Lens;
        lens.FieldOfView = Mathf.Lerp(lens.FieldOfView, targetFOV, Time.deltaTime * fovSmoothSpeed);
        cinemachineCamera.Lens = lens;
    }

    public void SetFOVMultiplier(float multiplier)
    {
        targetFOV = baseFOV * multiplier;
    }
}
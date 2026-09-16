using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    private PlayerController _playerController;

    public void Initialize(PlayerController playerController)
    {
        _playerController = playerController;
    }

    public void ApplyGravity()
    {
        if(_playerController.IsGround() && _playerController.VerticalVelocity < 0)
        {
            _playerController.VerticalVelocity = -2f;
        }

        _playerController.VerticalVelocity += _playerController.Gravity * Time.deltaTime;
    }

    public Vector3 GetMoveDirection()
    {
        Transform cameraTransform = _playerController.CameraTransform;
        Vector3 cameraForward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
        cameraForward.y = 0f;
        // 수직으로 내려다보는 카메라도 안정적인 수평 방향을 사용합니다.
        if (cameraForward.sqrMagnitude < 0.0001f)
            cameraForward = cameraTransform != null ? Vector3.ProjectOnPlane(cameraTransform.up, Vector3.up) : Vector3.forward;
        cameraForward.Normalize();
        Vector3 cameraRight = Vector3.Cross(Vector3.up, cameraForward);

        return Vector3.ClampMagnitude(cameraForward * _playerController.Input.MoveInput.y
            + cameraRight * _playerController.Input.MoveInput.x, 1f);
    }

    public Vector3 GetDirectionSlope(Vector3 direction)
    {
        if(Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, out RaycastHit hit, 1.3f, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore))
        {
            if(hit.normal != Vector3.up)
            {
                Vector3 projectedDirection = Vector3.ProjectOnPlane(direction, hit.normal);
                if(projectedDirection.y < 0f)
                {
                    return projectedDirection.normalized * direction.magnitude;
                }
            }
        }

        return direction;
    }

    public void OnAirMovement()
    {
        ApplyGravity();

        Vector3 moveDir = GetMoveDirection();

        Vector3 velocity = moveDir * _playerController.MoveSpeed;
        velocity.y = _playerController.VerticalVelocity;

        CollisionFlags flags = _playerController.Controller.Move(velocity * Time.deltaTime);

        if((flags & CollisionFlags.Above) != 0 && _playerController.VerticalVelocity > 0f)
        {
            _playerController.VerticalVelocity = -2f;
        }
    }
}

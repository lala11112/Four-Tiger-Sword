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
        if(_playerController.Controller.isGrounded && _playerController.VerticalVelocity < 0)
        {
            _playerController.VerticalVelocity = -2f;
            
        }

        _playerController.VerticalVelocity += _playerController.Gravity * Time.deltaTime;
    }

    public Vector3 GetMoveDirection()
    {
        Vector3 cameraForward = _playerController.CameraTransform.forward;
        Vector3 cameraRight = _playerController.CameraTransform.right;

        cameraForward.Normalize();
        cameraRight.Normalize();

        return cameraForward * _playerController.Input.MoveInput.y + cameraRight * _playerController.Input.MoveInput.x;
    }

    public Vector3 GetDirectionSlope(Vector3 direction)
    {
        if(Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, out RaycastHit hit, 1.3f))
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
        moveDir.y = 0;
        moveDir.Normalize();

        Vector3 velocity = moveDir * _playerController.MoveSpeed;
        velocity.y = _playerController.VerticalVelocity;

        CollisionFlags flags = _playerController.Controller.Move(velocity * Time.deltaTime);
        if(flags == CollisionFlags.Above)
        {
            _playerController.VerticalVelocity = -2f;
        }
    }
    
}

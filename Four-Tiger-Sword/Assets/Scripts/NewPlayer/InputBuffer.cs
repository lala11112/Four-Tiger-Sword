using UnityEngine;

public struct InputBuffer
{
    private float _timer;

    public bool IsActive => _timer > 0f;

    public void Set(float time = 0.15f)
    {
        _timer = time;
    }

    public void Update(float deltaTime)
    {
        if(IsActive)
        {
            _timer -= deltaTime;
        }
    }

    public void Consume()
    {
        _timer = 0f;
    }
}
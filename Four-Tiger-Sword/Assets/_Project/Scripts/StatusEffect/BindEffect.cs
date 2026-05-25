using UnityEngine;

/// <summary>목(木) 속박 스택 최대 시: 적을 완전히 속박(이동 불가)합니다.</summary>
public class BindEffect : IStatusEffect
{
    private readonly float _duration;
    private float _elapsed;

    public bool IsExpired => _elapsed >= _duration;

    public BindEffect(float duration = 1.5f)
    {
        _duration = duration;
    }

    public void OnApply(GameObject target)
    {
        Debug.Log("BindEffect OnApply");
        target.GetComponent<Enemy>().RootDuration = _duration;
        target.GetComponent<Enemy>().IsRoot = true;
    }

    public void OnUpdate(float deltaTime) => _elapsed += deltaTime;

    public void OnRemove(GameObject target)
    {
        target.GetComponent<Enemy>().IsRoot = false;
    }
}

using UnityEngine;
using System.Collections;

/// <summary>목(木) 속박 스택 최대 시: 적을 완전히 속박(이동 불가)합니다.</summary>
public class BindEffect : IStatusEffect
{
    private readonly float _duration;
    private float _elapsed;
    private GameObject _vfxPrefab;

    public bool IsExpired => _elapsed >= _duration;

    private GameObject effectInstance;

    public BindEffect(float duration = 1.5f, GameObject vfxPrefab = null)
    {
        _vfxPrefab = vfxPrefab;
        _duration = duration;
    }

    public void OnApply(GameObject target)
    {
        Debug.Log("BindEffect OnApply");
        target.GetComponent<Enemy>().RootDuration = _duration;
        target.GetComponent<Enemy>().IsRoot = true;

        if (_vfxPrefab != null)
            target.GetComponent<Enemy>().StartCoroutine(SpawnVFX(target));
    }

    private IEnumerator SpawnVFX(GameObject target)
    {
        yield return new WaitForSeconds(1f);
        effectInstance = Object.Instantiate(_vfxPrefab, target.transform.position, Quaternion.identity);
    }

    public void OnUpdate(float deltaTime) => _elapsed += deltaTime;

    public void OnRemove(GameObject target)
    {
        if (effectInstance != null)
            Object.Destroy(effectInstance);
        target.GetComponent<Enemy>().IsRoot = false;
    }
}

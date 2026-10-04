using UnityEngine;

/// <summary>Attach to the monster's damage receiver and assign a child transform.</summary>
[DisallowMultipleComponent]
public sealed class HitVfxAnchor : MonoBehaviour
{
    [SerializeField, Tooltip("피격 이펙트가 생성될 위치와 회전 기준. 비워두면 이 오브젝트를 사용합니다.")]
    private Transform spawnPoint;

    public Transform SpawnPoint => spawnPoint != null ? spawnPoint : transform;
}

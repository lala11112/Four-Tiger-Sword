using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct SpawnEntry
{
    [Tooltip("스폰할 몬스터 프리팹")]
    public GameObject Prefab;
    [Tooltip("스폰 위치 Transform (씬에 배치한 빈 오브젝트)")]
    public Transform SpawnPoint;
}

public class StageManager : MonoBehaviour
{
    [Header("스폰 설정")]
    [SerializeField] private SpawnEntry[] _spawnEntries;

    [Header("UI")]
    [SerializeField] private StageResultUI _stageResultUI;

    private readonly List<Enemy> _aliveEnemies = new List<Enemy>();

    public event Action OnStageClear;
    public event Action OnStageStart;

    private void Start()
    {
        SpawnAll();
        OnStageStart?.Invoke();
    }

    private void SpawnAll()
    {
        foreach (var entry in _spawnEntries)
        {
            if (entry.Prefab == null)
            {
                Debug.LogWarning("[StageManager] Prefab이 비어 있는 SpawnEntry가 있습니다. 건너뜁니다.");
                continue;
            }

            if (entry.SpawnPoint == null)
            {
                Debug.LogWarning($"[StageManager] {entry.Prefab.name}의 SpawnPoint가 비어 있습니다. 건너뜁니다.");
                continue;
            }

            var go = Instantiate(entry.Prefab, entry.SpawnPoint.position, entry.SpawnPoint.rotation);
            var enemy = go.GetComponent<Enemy>();

            if (enemy == null)
            {
                Debug.LogWarning($"[StageManager] {entry.Prefab.name}에 Enemy 컴포넌트가 없습니다. 생존 추적에서 제외됩니다.");
                continue;
            }

            _aliveEnemies.Add(enemy);
            enemy.OnDied += () => OnEnemyDied(enemy);
        }

        if (_aliveEnemies.Count == 0)
        {
            Debug.LogWarning("[StageManager] 스폰된 적이 없습니다. 즉시 스테이지 클리어 처리합니다.");
            HandleStageClear();
        }
    }

    private void OnEnemyDied(Enemy enemy)
    {
        _aliveEnemies.Remove(enemy);

        if (_aliveEnemies.Count == 0)
            HandleStageClear();
    }

    private void HandleStageClear()
    {
        _stageResultUI?.ShowResult(true);
        OnStageClear?.Invoke();
    }
}

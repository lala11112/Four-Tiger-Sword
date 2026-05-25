using UnityEngine;
using System.Collections.Generic;

public class EnemyStackManager : MonoBehaviour
{
    public List<StackDataSO> RegisteredStacks;

    private Dictionary<StackType, StackEntry> stackDict = new();

    void Awake()
    {
        foreach (var data in RegisteredStacks)
        {
            if (data == null) continue;

            if (stackDict.ContainsKey(data.stackType))
            {
                Debug.LogWarning($"[EnemyStackManager] {name}: '{data.stackType}' 타입 스택이 중복 등록되었습니다. " +
                                 $"'{data.name}'이 이전 항목을 덮어씁니다. SO의 stackType을 확인하세요.");
            }

            stackDict[data.stackType] = new StackEntry(data);
        }

        Debug.Log("스택매니저 Awake 작동");
    }

    void Update()
    {
        foreach (var entry in stackDict.Values)
        {
            if(entry.IsEmpty) continue;

            entry.Timer += Time.deltaTime;
            if(entry.Timer >= entry.Data.resetTime)
            {
                ResetStack(entry);
            }
        }
    }

    public void AddStack(StackType type)
    {
        if(!stackDict.TryGetValue(type, out var entry)) return;

        Debug.Log("스택매니저 AddStack 작동");

        entry.CurrentStack++;
        entry.Timer = 0f;

        if(entry.IsMaxed)
        {
            TriggerExplosion(entry);
        }
    }

    private void TriggerExplosion(StackEntry entry)
    {
        entry.Data.OnExplosion(gameObject);
        ResetStack(entry);
    }

    private void ResetStack(StackEntry entry)
    {
        entry.CurrentStack = 0;
        entry.Timer = 0f;
        OnStackChanged(entry);
    }

    // UI 등 외부 시스템 알림
    private void OnStackChanged(StackEntry entry)
    {
        // 예: UI 이벤트 발행
        // StackUIEvent.Invoke(entry.Data.stackType, entry.CurrentStack);
    }

    public int GetStack(StackType type)
        => stackDict.TryGetValue(type, out var e) ? e.CurrentStack : 0;
}

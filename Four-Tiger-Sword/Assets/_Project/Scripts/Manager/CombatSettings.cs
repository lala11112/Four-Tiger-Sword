using UnityEngine;

/// <summary>전투 씬마다 하나만 두고 사용할 상성표를 명시적으로 연결합니다.</summary>
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public sealed class CombatSettings : MonoBehaviour
{
    [SerializeField] private ElementEffectiveManager _elementTable;
    public ElementEffectiveManager ElementTable => _elementTable;
    public static CombatSettings Active { get; private set; }
    private static bool _reportedMissing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Active = null; _reportedMissing = false; }

    // Domain/Scene Reload를 꺼도 이전 실행의 정적 참조를 재사용하지 않습니다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RestoreRegistration()
    {
        if (Active != null) return;
        foreach (var settings in FindObjectsByType<CombatSettings>(FindObjectsSortMode.None))
            if (settings.isActiveAndEnabled) settings.Register();
    }

    private void OnEnable() => Register();
    private void Register()
    {
        if (Active != null && Active != this)
        {
            Debug.LogError("CombatSettings가 중복되었습니다. 활성 전투 씬에 하나만 두세요.", this);
            return;
        }
        Active = this;
        _reportedMissing = false;
        if (_elementTable == null) Debug.LogError("CombatSettings의 Element Table을 연결하세요.", this);
        else _elementTable.Initialize();
    }
    private void OnDisable() { if (Active == this) Active = null; }

    public static bool TryGetTable(out ElementEffectiveManager table)
    {
        table = Active != null ? Active._elementTable : null;
        if (table != null) return true;
        if (!_reportedMissing)
        {
            _reportedMissing = true;
            Debug.LogError("전투 설정 또는 상성표가 없습니다. 피해 계산을 중단합니다. CombatSettings의 Element Table을 확인하세요.");
        }
        return false;
    }
}

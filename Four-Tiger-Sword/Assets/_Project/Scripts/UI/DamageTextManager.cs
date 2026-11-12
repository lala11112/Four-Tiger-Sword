using UnityEngine;
using UnityEngine.Pool;
using System.Collections.Generic;

public class DamageTextManager : MonoBehaviour
{
    public static DamageTextManager Instance;

    [SerializeField] private GameObject damageTextPrefab;
    [SerializeField] private Canvas worldSpaceCanvas;

    [SerializeField] private int defaultPoolCapacity = 10;
    [SerializeField] private int maxPoolSize = 20;

    private ObjectPool<DamageText> _pool;
    private Camera _worldCamera;
    private Vector3 _worldTextScale;
    private readonly Dictionary<EnemyAction, DamageText> _attackWarnings = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => Instance = null;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            // The canvas and every pooled label belong to this scene. Keep their owner
            // in the same scene so a new run cannot inherit destroyed canvas/label references.
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        if (worldSpaceCanvas != null)
        {
            // Keep the authored world-space footprint above scene models, below other screen UI.
            _worldCamera = worldSpaceCanvas.worldCamera != null ? worldSpaceCanvas.worldCamera : Camera.main;
            _worldTextScale = worldSpaceCanvas.transform.lossyScale;
            worldSpaceCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            worldSpaceCanvas.overrideSorting = true;
            worldSpaceCanvas.sortingOrder = short.MinValue;
            worldSpaceCanvas.transform.localScale = Vector3.one;
            var raycaster = worldSpaceCanvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            if (raycaster != null) raycaster.enabled = false;
        }

        _pool = new ObjectPool<DamageText>(
            createFunc: CreateDamageText,
            actionOnGet: dt => { if (dt != null) dt.gameObject.SetActive(true); },
            actionOnRelease: dt =>
            {
                if (dt == null) return;
                dt.ResetForPool();
                dt.gameObject.SetActive(false);
            },
            actionOnDestroy: dt => { if (dt != null) Destroy(dt.gameObject); },
            collectionCheck: false,
            defaultCapacity: defaultPoolCapacity,
            maxSize: maxPoolSize
        );
    }

    private DamageText CreateDamageText()
    {
        GameObject go = Instantiate(damageTextPrefab, worldSpaceCanvas.transform);
        var text = go.GetComponent<DamageText>();
        if (text != null)
            text.ConfigureOverlay(worldSpaceCanvas, _worldCamera, Vector3.Scale(_worldTextScale, go.transform.localScale));
        return text;
    }

    public void Register(Enemy enemy)
    {
        enemy.OnDamaged += (damage, damageType, isCritical) =>
            ShowDamageText(damage, enemy.transform.position, damageType, isCritical);
    }

    public void Register(PlayerController player)
    {
        player.StatManager.OnDamageTaken += (damage, damageType, isCritical) =>
            ShowDamageText(damage, player.transform.position, damageType, isCritical);
    }

    public void ShowDamageText(float damage, Vector3 worldPosition, ElementType damageType = ElementType.ELEMENT_NONE, bool isCritical = false)
    {
        // Presentation must not interrupt damage/death notifications during scene teardown.
        if (_pool == null || worldSpaceCanvas == null || damageTextPrefab == null) return;
        Vector3 offset = new Vector3(Random.Range(-0.5f, 0.5f), 1.5f, Random.Range(-0.5f, 0.5f));

        var ownerPool = _pool;
        DamageText dt = ownerPool.Get();
        if (dt == null) return;
        dt.SetWorldPosition(worldPosition + offset);

        dt.Init(Mathf.RoundToInt(damage), damageType, isCritical, () =>
        {
            if (this != null && dt != null && object.ReferenceEquals(_pool, ownerPool)) ownerPool.Release(dt);
        });
    }

    public void ShowAttackWarning(EnemyAction action)
    {
        if (action == null || !action.IsAttackWarningActive || !action.IsActive || action.IsFinished
            || action.WasParried || action.Owner == null || !action.Owner.isActiveAndEnabled
            || action.Owner.IsDead || action.Owner.CurrentAction != action
            || _pool == null || worldSpaceCanvas == null || damageTextPrefab == null
            || _attackWarnings.ContainsKey(action)) return;

        var ownerPool = _pool;
        DamageText label = ownerPool.Get();
        if (label == null) return;
        _attackWarnings.Add(action, label);
        label.InitAttackWarning(action, () =>
        {
            // A late callback must never return a label now owned by a newer action/scene.
            if (this == null || !object.ReferenceEquals(_pool, ownerPool)
                || !_attackWarnings.TryGetValue(action, out var current) || current != label) return;
            HideAttackWarning(action);
        });
    }

    public void HideAttackWarning(EnemyAction action)
    {
        if (action == null || !_attackWarnings.TryGetValue(action, out var label)) return;
        _attackWarnings.Remove(action);
        if (label == null) return;
        label.ResetForPool();
        if (_pool != null) _pool.Release(label);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        foreach (var label in _attackWarnings.Values)
        {
            if (label == null) continue;
            label.ResetForPool();
            if (_pool != null) _pool.Release(label);
            else Destroy(label.gameObject);
        }
        _attackWarnings.Clear();
        _pool?.Dispose();
        _pool = null;
    }
}

using StudioBaekho;
using UnityEngine;

/// <summary>Displays the current generated stage's boss after entering its active encounter.</summary>
[DisallowMultipleComponent]
public sealed class BossHealthHud : MonoBehaviour
{
    [SerializeField] private RunManager run;
    [SerializeField] private CanvasGroup visibility;
    [SerializeField] private UnityEngine.UI.Image fill;
    [SerializeField] private UnityEngine.UI.Image trail;
    [SerializeField] private TMPro.TMP_Text bossName;
    [SerializeField] private float lerpSpeed = 8f;
    [SerializeField, Min(0f)] private float trailDelay = .35f;
    [SerializeField, Min(.01f)] private float trailSpeed = .6f;

    private RunManager _subscribedRun;
    private BaekhoDungeonResult _result;
    private DungeonRoomEncounter _encounter;
    private DungeonEncounterMember _member;
    private Enemy _enemy;
    private Boss _boss;
    private EnemyStat _stats;
    private float _currentHp = float.NaN;
    private float _maxHp = float.NaN;
    private float _targetFill;
    private float _trailDelayRemaining;
    private bool _showingTransition;

    public bool IsVisible { get; private set; }
    public float FillAmount => fill != null ? fill.fillAmount : 0f;
    public float TrailAmount => trail != null ? trail.fillAmount : 0f;

    public void Configure(RunManager targetRun, CanvasGroup targetVisibility, UnityEngine.UI.Image targetFill)
    {
        SetVisible(false);
        visibility = targetVisibility;
        fill = targetFill;
        Bind(targetRun);
    }

    public void ConfigureVisuals(UnityEngine.UI.Image targetTrail, float targetLerpSpeed,
        float targetTrailDelay, float targetTrailSpeed)
    {
        trail = targetTrail;
        lerpSpeed = Mathf.Max(0f, targetLerpSpeed);
        trailDelay = Mathf.Max(0f, targetTrailDelay);
        trailSpeed = Mathf.Max(.01f, targetTrailSpeed);
        _trailDelayRemaining = 0f;
        if (trail != null) trail.fillAmount = IsVisible && fill != null ? fill.fillAmount : 0f;
    }

    public void ConfigureName(TMPro.TMP_Text targetName)
    {
        if (bossName != null && bossName != targetName) bossName.text = string.Empty;
        bossName = targetName;
        RefreshName();
    }

    public void Bind(RunManager targetRun)
    {
        UnsubscribeRun();
        ClearTarget();
        run = targetRun;
        if (isActiveAndEnabled) SubscribeRun();
        Refresh();
    }

    private void OnEnable()
    {
        SubscribeRun();
        Refresh();
    }

    private void LateUpdate()
    {
        if (_subscribedRun != run)
        {
            UnsubscribeRun();
            ClearTarget();
            SubscribeRun();
        }
        Refresh();
        UpdateFill(Time.deltaTime);
    }

    private void SubscribeRun()
    {
        if (run == null || _subscribedRun == run) return;
        _subscribedRun = run;
        _subscribedRun.StateChanged += OnRunStateChanged;
    }

    private void UnsubscribeRun()
    {
        if (_subscribedRun != null) _subscribedRun.StateChanged -= OnRunStateChanged;
        _subscribedRun = null;
    }

    private void OnRunStateChanged(RunState state)
    {
        if (state == RunState.Playing) Refresh();
        else ClearTarget();
    }

    private void Refresh()
    {
        var stage = run != null ? run.stageManager : null;
        var result = stage != null ? stage.Result : null;
        if (!isActiveAndEnabled || visibility == null || fill == null || run == null
            || !run.isActiveAndEnabled || run.State != RunState.Playing || stage == null
            || !stage.isActiveAndEnabled || !stage.IsReady || result == null || !result.success
            || result.root == null || !result.root.activeInHierarchy || stage.Generator == null
            || stage.Generator.IsGenerating || stage.Generator.CurrentRoot != result.root)
        {
            ClearTarget();
            return;
        }

        if (!ReferenceEquals(_result, result)) ResolveTarget(result);
        if (_encounter == null || !_encounter.isActiveAndEnabled
            || !_encounter.transform.IsChildOf(result.root.transform)
            || (_encounter.State != DungeonEncounterState.Waiting && _encounter.State != DungeonEncounterState.Fighting)
            || _encounter.BossDefeated || _member == null || _member.HasDied || _member.IsDead
            || _enemy == null || !_enemy.gameObject.activeInHierarchy || _enemy.IsDead
            || !_enemy.transform.IsChildOf(_encounter.transform) || _stats == null)
        {
            UnbindActor();
            SetVisible(false);
            return;
        }
        RefreshName();

        // Keep the waiting boss bound so entering the room can reveal it without a stage change.
        if (!_encounter.WasEntered || !_encounter.IsCombatActive)
        {
            SetVisible(false);
            return;
        }

        float currentHp = _stats.CurrentHp;
        float maxHp = _stats.MaxHp;
        if (!IsFiniteNonnegative(currentHp) || !IsFinitePositive(maxHp))
        {
            UnbindActor();
            SetVisible(false);
            return;
        }
        if (_boss != null && _boss.IsTransitioning)
        {
            // Zero stays visible during the fall/hold; the rise animation owns the refill timing.
            SetMeterImmediate(Mathf.Clamp01(_boss.ReviveProgress));
            _showingTransition = true;
            _currentHp = currentHp;
            _maxHp = maxHp;
            SetVisible(true);
            return;
        }
        float nextFill = Mathf.Clamp01(currentHp / maxHp);
        if (!IsVisible || _showingTransition)
        {
            // Reveal the boss at its actual health, without animating from a cleared previous stage.
            SetMeterImmediate(nextFill);
            _showingTransition = false;
        }
        else if (_currentHp != currentHp || _maxHp != maxHp)
        {
            if (nextFill < _targetFill)
            {
                if (trail != null) trail.fillAmount = Mathf.Max(trail.fillAmount, fill.fillAmount);
                _trailDelayRemaining = trailDelay;
            }
            _targetFill = nextFill;
        }
        _currentHp = currentHp;
        _maxHp = maxHp;
        SetVisible(true);
    }

    private void UpdateFill(float deltaTime)
    {
        if (!IsVisible || fill == null) return;
        fill.fillAmount = Mathf.Lerp(fill.fillAmount, _targetFill, deltaTime * lerpSpeed);
        if (trail == null) return;

        // Match the monster bar's delayed trail, including frames that cross the hold's end.
        float trailDeltaTime = Mathf.Max(0f, deltaTime - _trailDelayRemaining);
        _trailDelayRemaining = Mathf.Max(0f, _trailDelayRemaining - deltaTime);
        trail.fillAmount = Mathf.Max(fill.fillAmount,
            Mathf.MoveTowards(trail.fillAmount, _targetFill, trailSpeed * trailDeltaTime));
    }

    private void ResolveTarget(BaekhoDungeonResult result)
    {
        ClearTarget();
        _result = result;
        _encounter = result.bossRoom != null ? result.bossRoom.GetComponent<DungeonRoomEncounter>() : null;
        if (_encounter == null || _encounter.Members.Count != 1) return;
        _member = _encounter.Members[0];
        _enemy = _member != null ? _member.Enemy : null;
        if (_enemy == null) return;
        _stats = _enemy.GetComponent<EnemyStat>();
        _boss = _enemy as Boss;
        _enemy.OnDied += OnBossDied;
        if (_boss != null) _boss.OnPhaseDepleted += OnPhaseDepleted;
        RefreshName();
    }

    private void RefreshName()
    {
        if (bossName == null) return;
        string text = string.Empty;
        if (_enemy != null)
        {
            text = _boss != null ? _boss.Data?.bossName : null;
            if (string.IsNullOrWhiteSpace(text))
            {
                // EnemyData has no display name field; use this actor's name for unnamed bosses.
                text = _enemy.name.Trim();
                const string cloneSuffix = "(Clone)";
                if (text.EndsWith(cloneSuffix, System.StringComparison.Ordinal))
                    text = text.Substring(0, text.Length - cloneSuffix.Length);
            }
            text = text.Trim();
        }
        if (bossName.text != text) bossName.text = text;
    }

    private void OnPhaseDepleted(int phaseIndex)
    {
        // Do not leave the old phase's delayed damage trail behind during the down animation.
        SetMeterImmediate(0f);
        _showingTransition = true;
    }

    private void SetMeterImmediate(float amount)
    {
        _targetFill = amount;
        if (fill != null) fill.fillAmount = amount;
        if (trail != null) trail.fillAmount = amount;
        _trailDelayRemaining = 0f;
    }

    private void OnBossDied()
    {
        UnbindActor();
        SetVisible(false);
    }

    private void UnbindActor()
    {
        if (_enemy != null) _enemy.OnDied -= OnBossDied;
        if (_boss != null) _boss.OnPhaseDepleted -= OnPhaseDepleted;
        _member = null;
        _enemy = null;
        _boss = null;
        _stats = null;
        if (bossName != null) bossName.text = string.Empty;
        _showingTransition = false;
        _currentHp = _maxHp = float.NaN;
        _targetFill = _trailDelayRemaining = 0f;
        if (fill != null) fill.fillAmount = 0f;
        if (trail != null) trail.fillAmount = 0f;
    }

    private void ClearTarget()
    {
        UnbindActor();
        _result = null;
        _encounter = null;
        SetVisible(false);
    }

    private void SetVisible(bool value)
    {
        IsVisible = value && visibility != null;
        if (visibility == null) return;
        visibility.alpha = IsVisible ? 1f : 0f;
        visibility.interactable = false;
        visibility.blocksRaycasts = false;
    }

    private static bool IsFinitePositive(float value)
        => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool IsFiniteNonnegative(float value)
        => value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);

    private void OnDisable()
    {
        UnsubscribeRun();
        ClearTarget();
    }

    private void OnDestroy()
    {
        UnsubscribeRun();
        ClearTarget();
    }
}

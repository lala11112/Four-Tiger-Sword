using ArpgHud;
using UnityEngine;

/// <summary>Renders an explicitly assigned player's state after gameplay has updated.</summary>
[DisallowMultipleComponent]
public sealed class PlayerHudPresenter : MonoBehaviour
{
    [SerializeField] private PlayerController player;
    [SerializeField] private HudController hud;
    [SerializeField] private CanvasGroup visibility;

    private bool _warnedMissingDependencies;
    private PlayerHealthEdgeFeedback _healthFeedback;
    private static readonly ElementType[] HudElements =
    {
        ElementType.ELEMENT_FIRE, ElementType.ELEMENT_WATER, ElementType.ELEMENT_WOOD,
        ElementType.ELEMENT_GOLD, ElementType.ELEMENT_EARTH
    };

    public void Configure(PlayerController targetPlayer, HudController targetHud, CanvasGroup targetVisibility)
    {
        SetVisible(false);
        if (_healthFeedback != null) _healthFeedback.SetStats(null);
        player = targetPlayer;
        hud = targetHud;
        visibility = targetVisibility;
        SetVisible(false);
    }

    private void OnEnable() => SetVisible(false);
    private void OnDisable()
    {
        SetVisible(false);
        if (_healthFeedback != null) _healthFeedback.SetStats(null);
    }

    private void OnDestroy()
    {
        if (_healthFeedback != null) _healthFeedback.Dispose();
    }

    private void LateUpdate()
    {
        // PlayerController creates its managers in Awake; always read the current instances.
        var stats = player != null ? player.StatManager : null;
        var feedbackStats = player != null && player.isActiveAndEnabled && !player.IsStageTransitioning ? stats : null;
        if (feedbackStats != null && _healthFeedback == null) _healthFeedback = PlayerHealthEdgeFeedback.Create();
        if (_healthFeedback != null)
        {
            _healthFeedback.SetStats(feedbackStats);
            _healthFeedback.Tick();
        }

        var form = player != null ? player.FormManager?.CurrentForm as BaseForm : null;
        int formIndex = form != null ? GetFormIndex(form.Element) : 0;
        if (player == null || !player.isActiveAndEnabled || stats == null
            || form == null || formIndex == 0 || hud == null || !hud.isActiveAndEnabled
            || hud.View == null || visibility == null)
        {
            SetVisible(false);
            if (!_warnedMissingDependencies)
            {
                Debug.LogWarning("[PlayerHudPresenter] Assign an initialized player, HUD, and visibility CanvasGroup. The HUD stays hidden until all dependencies are available.", this);
                _warnedMissingDependencies = true;
            }
            return;
        }

        bool ultimateActive = form is IToggleUltimateForm toggle && toggle.IsUltimateActive;
        var chainTarget = player.FormManager.ReadyChainSkill;
        var state = new HudState
        {
            Hp = new MeterState(stats.CurrentHp, stats.MaxHp),
            Auxiliary = new MeterState(stats.CurrentSp, stats.MaxSp),
            UltimateCharge = new MeterState(stats.CurrentUltimateGauge, stats.MaxUltimateGauge),
            ActiveFormIndex = formIndex,
            LockedFormsMask = GetLockedFormsMask(player.FormManager),
            FireChain = new ChainSkillState(player.FormManager.GetChainReadyRemaining(ElementType.ELEMENT_FIRE),
                chainTarget != null && chainTarget.Element == ElementType.ELEMENT_FIRE),
            WaterChain = new ChainSkillState(player.FormManager.GetChainReadyRemaining(ElementType.ELEMENT_WATER),
                chainTarget != null && chainTarget.Element == ElementType.ELEMENT_WATER),
            E = new SkillState
            {
                Remaining = form.SkillCooldownRemaining,
                Duration = form.SkillCooldown,
                Unavailable = !form.CanSkill
            },
            R = new SkillState
            {
                Remaining = form.UltimateCooldownRemaining,
                Duration = form.UltimateCooldownDuration,
                Unavailable = !(form.CanUltimate || ultimateActive),
                Active = ultimateActive
            }
        };

        hud.SetState(state);
        SetVisible(true);
    }

    private void SetVisible(bool value)
    {
        if (visibility == null) return;
        visibility.alpha = value ? 1f : 0f;
        visibility.interactable = false;
        visibility.blocksRaycasts = false;
    }

    private static int GetFormIndex(ElementType element)
    {
        switch (element)
        {
            case ElementType.ELEMENT_FIRE: return 1;
            case ElementType.ELEMENT_WATER: return 2;
            case ElementType.ELEMENT_WOOD: return 3;
            case ElementType.ELEMENT_GOLD: return 4;
            case ElementType.ELEMENT_EARTH: return 5;
            default: return 0;
        }
    }

    private static int GetLockedFormsMask(FormManager forms)
    {
        int mask = 0;
        for (int i = 0; i < HudElements.Length; i++)
            if (!forms.IsElementAvailable(HudElements[i])) mask |= 1 << i;
        return mask;
    }
}

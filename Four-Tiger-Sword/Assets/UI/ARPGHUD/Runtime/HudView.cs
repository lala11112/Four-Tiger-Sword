using TMPro;
using UnityEngine;

namespace ArpgHud
{
    [DisallowMultipleComponent]
    public sealed class HudView : MonoBehaviour
    {
        [Tooltip("Optional component implementing IHudStateSource. May also be bound from code.")]
        [SerializeField] MonoBehaviour stateSource;
        [SerializeField] UnityEngine.UI.Image hpFill;
        [SerializeField] UnityEngine.UI.Image auxiliaryFill;
        [SerializeField] UnityEngine.UI.Image ultimateChargeFill;
        [SerializeField] TMP_Text ultimateChargeText;
        [SerializeField] HudEnergyImage ultimateEnergy;
        [SerializeField] GameObject[] selectedOverlays = new GameObject[5];
        [SerializeField] UnityEngine.UI.Image[] formArtwork;
        [SerializeField] HudSkillView skillE;
        [SerializeField] HudSkillView skillR;
        [SerializeField] HudElementTheme elementTheme;
        IHudStateSource source;
        IHudStateSource subscribed;
        bool warnedInvalid;
        int previousChargePercent = -1;

        public HudSkillView SkillE => skillE;
        public HudSkillView SkillR => skillR;
        public float HpRatio => hpFill != null ? hpFill.fillAmount : 0;
        public float AuxiliaryRatio => auxiliaryFill != null ? auxiliaryFill.fillAmount : 0;
        public float UltimateChargeRatio => ultimateEnergy != null ? ultimateEnergy.Charge
            : ultimateChargeFill != null ? ultimateChargeFill.fillAmount : 0;

        public void Configure(UnityEngine.UI.Image hp, UnityEngine.UI.Image aux, GameObject[] overlays,
            HudSkillView e, HudSkillView r)
        { hpFill = hp; auxiliaryFill = aux; selectedOverlays = overlays; skillE = e; skillR = r; }

        public void ConfigureFormArtwork(UnityEngine.UI.Image[] images) { formArtwork=images; }
        public void ConfigureElementTheme(HudElementTheme theme) { elementTheme=theme; }
        public void ConfigureUltimateCharge(UnityEngine.UI.Image fill, TMP_Text text)
        { ultimateChargeFill = fill; ultimateChargeText = text; previousChargePercent = -1; }
        public void ConfigureUltimateEnergy(HudEnergyImage energy) { ultimateEnergy = energy; }

        void Awake()
        {
            if (source == null && stateSource != null) source = stateSource as IHudStateSource;
            if (stateSource != null && source == null)
                Debug.LogError("HUD stateSource must implement IHudStateSource.", this);
        }
        void OnEnable() { Attach(); }
        void OnDisable() { Detach(); }
        void OnDestroy() { Detach(); }

        public void Bind(IHudStateSource value)
        { Detach(); source = value; if (isActiveAndEnabled) Attach(); }

        void Attach()
        {
            if (source == null || subscribed != null) return;
            subscribed = source;
            subscribed.StateChanged += Refresh;
            Refresh();
        }
        void Detach()
        {
            if (subscribed != null) subscribed.StateChanged -= Refresh;
            subscribed = null;
        }
        public void Refresh() { if (source != null) Render(source.CurrentState); }

        /// <summary>For push-based integration call this with a complete authoritative snapshot.</summary>
        public void Render(HudState value)
        {
            bool invalid = !HudMath.IsFinite(value.Hp.Current) || !HudMath.IsFinite(value.Hp.Maximum)
                || !HudMath.IsFinite(value.Auxiliary.Current) || !HudMath.IsFinite(value.Auxiliary.Maximum)
                || !HudMath.IsFinite(value.UltimateCharge.Current) || !HudMath.IsFinite(value.UltimateCharge.Maximum)
                || !HudMath.IsFinite(value.E.Remaining) || !HudMath.IsFinite(value.E.Duration)
                || !HudMath.IsFinite(value.R.Remaining) || !HudMath.IsFinite(value.R.Duration);
            if (invalid && !warnedInvalid) Debug.LogWarning("HUD received non-finite state; safe display values used.", this);
            warnedInvalid = invalid;
            if (hpFill != null) hpFill.fillAmount = HudMath.Ratio(value.Hp);
            if (auxiliaryFill != null) auxiliaryFill.fillAmount = HudMath.Ratio(value.Auxiliary);
            float chargeRatio = HudMath.Ratio(value.UltimateCharge);
            if (ultimateChargeFill != null) ultimateChargeFill.fillAmount = chargeRatio;
            if (ultimateChargeText != null)
            {
                int percent = Mathf.FloorToInt(chargeRatio * 100);
                if (percent != previousChargePercent)
                {
                    ultimateChargeText.SetText(percent.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%");
                    previousChargePercent = percent;
                }
            }
            if (selectedOverlays != null)
                for (int i = 0; i < selectedOverlays.Length; i++)
                    if (selectedOverlays[i] != null)
                    {
                        bool selected = value.ActiveFormIndex == i + 1;
                        if (selectedOverlays[i].activeSelf != selected) selectedOverlays[i].SetActive(selected);
                    }
            if (formArtwork != null)
                for (int i=0;i<formArtwork.Length;i++)
                    if (formArtwork[i] != null) formArtwork[i].color=value.ActiveFormIndex==i+1 ? Color.white : new Color(.66f,.66f,.66f,1);
            Color skillColor=Color.red;
            // Resolve presentation only: switching forms never changes the supplied timing.
            if(elementTheme!=null && elementTheme.TryGet(value.ActiveFormIndex,out var element)) {
                value.E.ColorIcon=value.E.GrayIcon=element.E;
                value.R.ColorIcon=value.R.GrayIcon=element.R;
                skillColor=element.Color;
            }
            if (skillE != null) { skillE.SetElementColor(skillColor); skillE.Render(value.E); }
            if (skillR != null) { skillR.SetElementColor(skillColor); skillR.Render(value.R, chargeRatio); }
            if (ultimateEnergy != null) ultimateEnergy.SetCharge(chargeRatio, skillColor);
            if (ultimateChargeFill != null) ultimateChargeFill.color = skillColor;
            if (ultimateChargeText != null) ultimateChargeText.color = skillColor;
        }
    }
}

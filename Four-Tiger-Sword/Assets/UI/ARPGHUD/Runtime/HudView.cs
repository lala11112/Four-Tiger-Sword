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
        [SerializeField] TMP_Text hpText;
        [SerializeField] TMP_Text auxiliaryText;
        [SerializeField] HudEnergyImage ultimateEnergy;
        [SerializeField] GameObject[] selectedOverlays = new GameObject[5];
        [SerializeField] UnityEngine.UI.Image[] formArtwork;
        [SerializeField] GameObject[] formLockLabels;
        [SerializeField] GameObject[] formKeyLabels;
        [SerializeField] HudSkillView skillE;
        [SerializeField] HudSkillView skillR;
        [SerializeField] HudElementTheme elementTheme;
        [SerializeField] GameObject chainSkills;
        [SerializeField] TMP_Text fireChainText;
        [SerializeField] TMP_Text waterChainText;
        [SerializeField] GameObject fireChainSelected;
        [SerializeField] GameObject waterChainSelected;
        [SerializeField] TMP_Text chainActionHint;
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
        public void ConfigureFormLocks(GameObject[] locks, GameObject[] keys)
        { formLockLabels = locks; formKeyLabels = keys; }
        public void ConfigureElementTheme(HudElementTheme theme) { elementTheme=theme; }
        public void ConfigureUltimateCharge(UnityEngine.UI.Image fill, TMP_Text text)
        { ultimateChargeFill = fill; ultimateChargeText = text; previousChargePercent = -1; }
        public void ConfigureUltimateEnergy(HudEnergyImage energy) { ultimateEnergy = energy; }
        public void ConfigureVitalsText(TMP_Text hp, TMP_Text auxiliary)
        { hpText = hp; auxiliaryText = auxiliary; }
        public void ConfigureChainSkills(GameObject root, TMP_Text fire, TMP_Text water,
            GameObject fireSelected, GameObject waterSelected, TMP_Text hint)
        {
            chainSkills = root;
            fireChainText = fire;
            waterChainText = water;
            fireChainSelected = fireSelected;
            waterChainSelected = waterSelected;
            chainActionHint = hint;
        }

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
                || !HudMath.IsFinite(value.R.Remaining) || !HudMath.IsFinite(value.R.Duration)
                || !HudMath.IsFinite(value.FireChain.Remaining) || !HudMath.IsFinite(value.WaterChain.Remaining);
            if (invalid && !warnedInvalid) Debug.LogWarning("HUD received non-finite state; safe display values used.", this);
            warnedInvalid = invalid;
            if (hpFill != null) hpFill.fillAmount = HudMath.Ratio(value.Hp);
            if (auxiliaryFill != null) auxiliaryFill.fillAmount = HudMath.Ratio(value.Auxiliary);
            if (hpText != null)
                hpText.SetText("{0:0} / {1:0}", SafeNumber(value.Hp.Current), SafeNumber(value.Hp.Maximum));
            if (auxiliaryText != null)
                auxiliaryText.SetText("기력 {0:0} / {1:0}", SafeNumber(value.Auxiliary.Current), SafeNumber(value.Auxiliary.Maximum));
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
                        bool selected = value.ActiveFormIndex == i + 1 && !value.IsFormLocked(i + 1);
                        if (selectedOverlays[i].activeSelf != selected) selectedOverlays[i].SetActive(selected);
                    }
            if (formArtwork != null)
                for (int i=0;i<formArtwork.Length;i++)
                    if (formArtwork[i] != null)
                        formArtwork[i].color = value.IsFormLocked(i + 1) ? new Color(.28f, .28f, .28f, 1f)
                            : value.ActiveFormIndex == i + 1 ? Color.white : new Color(.66f, .66f, .66f, 1f);
            if (formLockLabels != null)
                for (int i = 0; i < formLockLabels.Length; i++)
                    SetActive(formLockLabels[i], value.IsFormLocked(i + 1));
            if (formKeyLabels != null)
                for (int i = 0; i < formKeyLabels.Length; i++)
                    SetActive(formKeyLabels[i], !value.IsFormLocked(i + 1));
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
            RenderChainSkills(value.IsFormLocked(1) ? default : value.FireChain,
                value.IsFormLocked(2) ? default : value.WaterChain);
        }

        private void RenderChainSkills(ChainSkillState fire, ChainSkillState water)
        {
            bool fireReady = HudMath.ChainRemaining(fire) > 0f;
            bool waterReady = HudMath.ChainRemaining(water) > 0f;
            SetActive(chainSkills, fireReady || waterReady);
            RenderChainSlot(fire, fireChainText, fireChainSelected, 1, "화 {0:1}초");
            RenderChainSlot(water, waterChainText, waterChainSelected, 2, "수 {0:1}초");
            if (chainActionHint != null)
            {
                int selected = fireReady && fire.Selected ? 1 : waterReady && water.Selected ? 2 : 0;
                SetActive(chainActionHint.gameObject, selected != 0);
                if (selected != 0)
                {
                    chainActionHint.text = selected == 1 ? "F  화 교체" : "F  수 교체";
                    chainActionHint.color = Color.Lerp(ChainColor(selected), Color.white, .55f);
                }
            }
        }

        private void RenderChainSlot(ChainSkillState state, TMP_Text label, GameObject selected,
            int formIndex, string format)
        {
            float remaining = HudMath.ChainRemaining(state);
            bool ready = remaining > 0f;
            if (label != null)
            {
                SetActive(label.transform.parent.gameObject, ready);
                if (ready)
                {
                    label.SetText(format, remaining);
                    Color color = ChainColor(formIndex);
                    label.color = Color.Lerp(color, Color.white, state.Selected ? .85f : .45f);
                }
            }
            SetActive(selected, ready && state.Selected);
        }

        private Color ChainColor(int formIndex)
            => elementTheme != null && elementTheme.TryGet(formIndex, out var element)
                ? element.Color : formIndex == 1 ? new Color(1f, .4f, .25f) : new Color(.3f, .75f, 1f);

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active) target.SetActive(active);
        }

        private static float SafeNumber(float value) => HudMath.IsFinite(value) ? Mathf.Max(0f, Mathf.Ceil(value)) : 0f;
    }
}

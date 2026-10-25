using TMPro;
using UnityEngine;

namespace ArpgHud
{
    [DisallowMultipleComponent]
    public sealed class HudSkillView : MonoBehaviour
    {
        [SerializeField] UnityEngine.UI.Image iconGray;
        [SerializeField] UnityEngine.UI.Image iconColorReveal;
        [SerializeField] UnityEngine.UI.Image ringFill;
        [SerializeField] UnityEngine.UI.Image ringTrack;
        [SerializeField] TMP_Text cooldownNumber;
        [SerializeField] bool useChargeReveal;
        bool warnedMissing;
        string previousTime;
        Color elementColor = Color.red;
        bool unavailable, active;

        public float Progress => iconColorReveal != null ? iconColorReveal.fillAmount : 0;
        public bool NumberVisible => cooldownNumber != null && cooldownNumber.enabled;

        public void Configure(UnityEngine.UI.Image gray, UnityEngine.UI.Image color,
            UnityEngine.UI.Image ring, TMP_Text number)
        { iconGray = gray; iconColorReveal = color; ringFill = ring; cooldownNumber = number; }

        public void ConfigureRingTrack(UnityEngine.UI.Image track) { ringTrack=track; }
        public void ConfigureChargeReveal(bool value)
        {
            useChargeReveal = value;
            ConfigureRevealMode();
        }

        void ConfigureRevealMode()
        {
            if (iconColorReveal == null) return;
            iconColorReveal.type = UnityEngine.UI.Image.Type.Filled;
            iconColorReveal.fillMethod = useChargeReveal
                ? UnityEngine.UI.Image.FillMethod.Vertical : UnityEngine.UI.Image.FillMethod.Radial360;
            iconColorReveal.fillOrigin = useChargeReveal
                ? (int)UnityEngine.UI.Image.OriginVertical.Bottom : (int)UnityEngine.UI.Image.Origin360.Top;
        }

        public void SetElementColor(Color color)
        {
            color.a=1;
            elementColor=color;
            ApplyRingColors();
        }

        void ApplyRingColors()
        {
            Color color = active ? Color.Lerp(elementColor, Color.white, .3f)
                : unavailable ? new Color(.32f, .32f, .32f, 1) : elementColor;
            float trackBrightness = active ? .65f : .32f;
            if (ringFill != null) ringFill.color = color;
            if (ringTrack != null) ringTrack.color = new Color(color.r * trackBrightness,
                color.g * trackBrightness, color.b * trackBrightness, 1);
        }

        public void Render(SkillState state) { Render(state, 0); }

        public void Render(SkillState state, float chargeRatio)
        {
            active = state.Active;
            unavailable = state.Unavailable && !active;
            float remaining = HudMath.Remaining(state);
            float progress = HudMath.Progress(state);
            ApplyRingColors();
            bool hasIcons = state.ColorIcon != null && state.GrayIcon != null;
            if (!hasIcons && !warnedMissing)
            { Debug.LogWarning(name + ": Assign matching color and grayscale sprites.", this); warnedMissing = true; }
            if (hasIcons) warnedMissing = false;
            if (iconGray != null)
            {
                iconGray.sprite = state.GrayIcon;
                iconGray.enabled = hasIcons;
                iconGray.color = unavailable ? new Color(.3f, .3f, .3f, 1) : new Color(.42f, .42f, .42f, 1);
            }
            if (iconColorReveal != null)
            {
                if (useChargeReveal) ConfigureRevealMode();
                float reveal = useChargeReveal
                    ? (HudMath.IsFinite(chargeRatio) ? Mathf.Clamp01(chargeRatio) : 0) : progress;
                float brightness = useChargeReveal ? .8f : .55f;
                iconColorReveal.sprite = state.ColorIcon;
                iconColorReveal.enabled = hasIcons && (useChargeReveal || !unavailable || remaining > 0);
                iconColorReveal.color = unavailable ? new Color(brightness, brightness, brightness, 1) : Color.white;
                iconColorReveal.fillAmount = active ? 1 : reveal;
            }
            if (ringFill != null) ringFill.fillAmount = progress;
            if (cooldownNumber == null) return;
            cooldownNumber.enabled = remaining > 0;
            string time = remaining.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            if (time != previousTime)
            { cooldownNumber.SetText(time); previousTime = time; }
        }
    }
}

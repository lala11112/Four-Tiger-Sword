using TMPro;
using UnityEngine;

namespace Penta.BossUI
{
    /// <summary>Displays boss health and accumulated groggy as independent bars.</summary>
    [DisallowMultipleComponent]
    public sealed class BossHealthGroggyUI : MonoBehaviour
    {
        [SerializeField] private UnityEngine.UI.Image healthFill;
        [SerializeField] private BossGroggyHPIntegration groggy;
        [SerializeField] private TMP_Text bossName;

        public UnityEngine.UI.Image HealthFill => healthFill;
        public BossGroggyHPIntegration Groggy => groggy;
        public TMP_Text BossName => bossName;

        public void SetHealth(float current, float maximum)
        {
            SetHealthNormalized(Normalize(current, maximum));
        }

        public void SetHealthNormalized(float fraction)
        {
            if (healthFill)
                healthFill.fillAmount = ClampFraction(fraction);
        }

        public void SetGroggy(float accumulated, float maximum)
        {
            SetGroggyNormalized(Normalize(accumulated, maximum));
        }

        public void SetGroggyNormalized(float fraction)
        {
            if (groggy)
                groggy.SetNormalized(ClampFraction(fraction));
        }

        public void SetBossName(string displayName)
        {
            if (bossName)
                bossName.text = displayName ?? string.Empty;
        }

        private static float Normalize(float current, float maximum)
        {
            if (maximum <= 0f || !IsFinite(maximum) || !IsFinite(current))
                return 0f;

            return Mathf.Clamp(current, 0f, maximum) / maximum;
        }

        private static float ClampFraction(float fraction) =>
            IsFinite(fraction) ? Mathf.Clamp01(fraction) : 0f;

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

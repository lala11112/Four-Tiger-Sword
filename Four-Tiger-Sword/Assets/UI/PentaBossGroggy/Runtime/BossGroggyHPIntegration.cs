using UnityEngine;

namespace Penta.BossUI
{
    /// <summary>Drives the remaining groggy strip inside an existing boss HP frame.</summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class BossGroggyHPIntegration : MonoBehaviour
    {
        [SerializeField, HideInInspector] private UnityEngine.UI.Image hpFill;
        [SerializeField, HideInInspector] private UnityEngine.UI.Image track;
        [SerializeField, HideInInspector] private UnityEngine.UI.Image fill;
        [SerializeField, Range(0f, 1f)] private float normalized;

        public UnityEngine.UI.Image HpFill => hpFill;
        public UnityEngine.UI.Image Track => track;
        public UnityEngine.UI.Image Fill => fill;
        public float Normalized => normalized;
        public bool IsBroken => normalized >= 1f;

        public void SetNormalized(float fraction)
        {
            normalized = float.IsNaN(fraction) ? 0f : Mathf.Clamp01(fraction);
            RefreshFill();
        }

        public void SetGroggy(float current, float maximum)
        {
            SetNormalized(maximum > 0f && !float.IsInfinity(maximum) ? current / maximum : 0f);
        }

        public void SetRemaining(float fraction) => SetNormalized(1f - fraction);

        public void ResetGauge() => SetNormalized(0f);

        private void OnEnable() => RefreshFill();

#if UNITY_EDITOR
        private void OnValidate() => SetNormalized(normalized);
#endif

        private void RefreshFill()
        {
            if (!fill) return;
            float remaining = 1f - normalized;
            fill.fillAmount = remaining;
            // A sprite-less uGUI Image draws a quad and ignores fillAmount.
            if (!fill.overrideSprite)
            {
                fill.rectTransform.anchorMax = new Vector2(remaining, 1f);
            }
        }
    }
}

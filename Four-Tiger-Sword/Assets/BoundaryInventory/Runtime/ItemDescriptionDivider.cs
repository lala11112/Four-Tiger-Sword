using System;
using System.Text;
using TMPro;
using UnityEngine;

namespace BoundaryZero.Inventory
{
    /// <summary>Renders the rule in the measured gap between the effect text and its synergy heading.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(TextMeshProUGUI))]
    public sealed class ItemDescriptionDivider : MonoBehaviour
    {
        public const string EffectHeading = "<color=#C6D9EB><b>개별 효과</b></color>\n";
        private const string Marker = "item-synergy-divider";
        private const string SynergyHeading = "<color=#F4B976><b>시너지</b></color>";
        private TextMeshProUGUI _text;
        private UnityEngine.UI.Image _line;
        private string _measuredText;
        private float _lineY;
        private bool _hasGap;

        public static void AppendSynergyHeading(StringBuilder body, bool afterEffect)
        {
            if (afterEffect)
                body.Append("\n\n<link=\"").Append(Marker).Append("\">").Append(SynergyHeading).Append("</link>\n");
            else
                body.Append(SynergyHeading).Append('\n');
        }

        public static void Attach(TMP_Text text)
        {
            if (text is TextMeshProUGUI ui && ui != null && !ui.TryGetComponent<ItemDescriptionDivider>(out _))
                ui.gameObject.AddComponent<ItemDescriptionDivider>();
        }

        private void Awake()
        {
            _text = GetComponent<TextMeshProUGUI>();
            var node = new GameObject("SectionDivider", typeof(RectTransform), typeof(UnityEngine.UI.Image)) { layer = gameObject.layer };
            var rect = (RectTransform)node.transform;
            rect.SetParent(_text.rectTransform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.right;
            rect.pivot = new Vector2(.5f, .5f);
            _line = node.GetComponent<UnityEngine.UI.Image>();
            _line.raycastTarget = false;
            _line.color = new Color32(151, 175, 194, 176);
            _line.enabled = false;
        }

        private void OnEnable() => _text.OnPreRenderText += MeasureGap;

        private void OnDisable()
        {
            _text.OnPreRenderText -= MeasureGap;
            _line.enabled = false;
        }

        private void MeasureGap(TMP_TextInfo layout)
        {
            _measuredText = _text.text;
            _hasGap = false;
            for (int i = 0; i < layout.linkCount; i++)
            {
                var link = layout.linkInfo[i];
                if (link.GetLinkID() != Marker || link.linkTextLength == 0
                    || link.linkTextfirstCharacterIndex >= layout.characterCount) continue;
                int heading = layout.characterInfo[link.linkTextfirstCharacterIndex].lineNumber;
                if (heading < 2 || heading >= layout.lineCount) return;
                float effectBottom = layout.lineInfo[heading - 2].descender;
                float headingTop = layout.lineInfo[heading].ascender;
                if (effectBottom <= headingTop) return;
                _lineY = (effectBottom + headingTop) * .5f;
                _hasGap = true;
                return;
            }
        }

        private void LateUpdate()
        {
            // Update the separate graphic outside TMP's canvas rebuild callback.
            bool visible = _hasGap && string.Equals(_measuredText, _text.text, StringComparison.Ordinal);
            if (visible)
            {
                var margins = _text.margin;
                var rect = _line.rectTransform;
                var size = new Vector2(-margins.x - margins.z, 2f);
                var position = new Vector2((margins.x - margins.z) * .5f, _lineY - _text.rectTransform.rect.yMin);
                if (rect.sizeDelta != size) rect.sizeDelta = size;
                if (rect.anchoredPosition != position) rect.anchoredPosition = position;
            }
            if (_line.enabled != visible) _line.enabled = visible;
        }
    }
}

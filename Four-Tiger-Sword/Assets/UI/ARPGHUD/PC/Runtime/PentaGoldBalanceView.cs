using System.Globalization;
using TMPro;
using UnityEngine;

namespace ArpgHud.PC
{
    [AddComponentMenu("PENTA/PC HUD/Gold Balance")]
    public sealed class PentaGoldBalanceView : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField, Min(0)] private int displayedGold;
        public int DisplayedGold => displayedGold;

        public void Configure(TMP_Text value) { label = value; Refresh(); }
        public void SetGold(int balance) { displayedGold = Mathf.Max(0, balance); Refresh(); }
        private void OnEnable() { Refresh(); }
        private void OnValidate() { displayedGold = Mathf.Max(0, displayedGold); Refresh(); }
        private void Refresh()
        {
            if (label != null) label.text = "골드 " + displayedGold.ToString("N0", CultureInfo.InvariantCulture);
        }
    }
}

using System.Collections;
using TMPro;
using UnityEngine;

namespace BoundaryZeroUI
{
    /// <summary>Optional presentation-only sample. The game supplies real balances and rewards.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class BoundaryGoldHudDemo : MonoBehaviour
    {
        BoundaryUIController ui;
        Coroutine burst;
        GameObject help;
        bool paused;
        float previousScale;

        IEnumerator Start()
        {
            yield return null;
            ui = GetComponent<BoundaryUIController>();
            ResetSample();
            var viewport = ui.GetComponentInChildren<BoundaryDesignViewport>();
            var label = UIFactory.Label(viewport.transform, "G  골드 +20     B  연속 획득     V  골드 -30     P  일시정지     F2  초기화", 670, 42, 1240, 36, 20,
                new Color(.64f, .71f, .79f), TextAlignmentOptions.Center);
            help = label.gameObject;
        }

        void OnDisable()
        {
            if (burst != null) { StopCoroutine(burst); burst = null; }
            if (help) help.SetActive(false);
            if (paused) Time.timeScale = previousScale;
            paused = false;
        }

        public void ResetSample()
        {
            if (!ui) ui = GetComponent<BoundaryUIController>();
            if (burst != null) { StopCoroutine(burst); burst = null; }
            if (paused) Time.timeScale = previousScale;
            paused = false;
            ui.ShowScreen("hud");
            ui.SetHudData("1층 · 3 / 5", "", .76f, "구역 탐색", new[] { "E", "R" });
            ui.SetGold(1250); ui.GoldHud.Feed.Clear();
        }

        public void Gain(int amount)
        {
            if (!ui || amount <= 0) return;
            int actual = (int)System.Math.Min(amount, (long)int.MaxValue - ui.GoldHud.Balance.DisplayedGold);
            ui.SetGold(ui.GoldHud.Balance.DisplayedGold + actual);
            ui.ShowGoldGain(actual);
        }

        IEnumerator Burst()
        {
            Gain(20); yield return new WaitForSecondsRealtime(.12f);
            Gain(20); yield return new WaitForSecondsRealtime(.12f);
            Gain(20); burst = null;
        }

        void Update()
        {
            if (!ui) return;
            if (Input.GetKeyDown(KeyCode.F2)) ResetSample();
            if (ui.CurrentScreen != "hud") return;
            if (Input.GetKeyDown(KeyCode.G)) Gain(20);
            if (Input.GetKeyDown(KeyCode.B) && burst == null) burst = StartCoroutine(Burst());
            if (Input.GetKeyDown(KeyCode.V)) ui.SetGold(Mathf.Max(0, ui.GoldHud.Balance.DisplayedGold - 30));
            if (Input.GetKeyDown(KeyCode.P))
            {
                if (paused) Time.timeScale = previousScale;
                else { previousScale = Time.timeScale; Time.timeScale = 0; }
                paused = !paused;
            }
        }
    }
}

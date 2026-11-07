using UnityEngine;

namespace ArpgHud.PC
{
    [AddComponentMenu("PENTA/PC HUD/Gold Reward Feed")]
    public sealed class PentaGoldRewardFeed : MonoBehaviour
    {
        [SerializeField] private PentaGoldRewardRow[] rows = new PentaGoldRewardRow[0];
        [SerializeField, Min(0)] private float gap = 8f;
        [SerializeField, Min(0)] private float mergeWindow = .4f;
        [SerializeField, Min(0)] private float hold = 2f;
        [SerializeField, Min(0)] private float fade = .3f;
        [Header("Upward Motion")]
        [Tooltip("Minimum entrance rise. Stacked rows start farther below when needed to preserve spacing.")]
        [SerializeField, Min(0)] private float riseDistance = 28f;
        [SerializeField, Min(0)] private float enterDuration = .25f;
        [SerializeField, Min(0)] private float slideDuration = .28f;
        [SerializeField, Min(0)] private float exitDistance = 16f;
        private readonly PentaGoldRewardRow[] active = new PentaGoldRewardRow[3];
        public int ActiveRowCount { get; private set; }
        public PentaGoldRewardRow GetRow(int oldestFirstIndex)
        {
            return oldestFirstIndex >= 0 && oldestFirstIndex < ActiveRowCount ? active[oldestFirstIndex] : null;
        }

        public void Configure(PentaGoldRewardRow[] value, float gap = 8f, float mergeWindow = .4f, float hold = 2f, float fade = .3f)
        {
            Clear();
            rows = value ?? new PentaGoldRewardRow[0];
            this.gap = Mathf.Max(0, gap);
            this.mergeWindow = Mathf.Max(0, mergeWindow);
            this.hold = Mathf.Max(0, hold);
            this.fade = Mathf.Max(0, fade);
            Clear();
        }

        public void ConfigureMotion(float riseDistance = 28f, float enterDuration = .25f, float slideDuration = .28f, float exitDistance = 16f)
        {
            this.riseDistance = Mathf.Max(0, riseDistance);
            this.enterDuration = Mathf.Max(0, enterDuration);
            this.slideDuration = Mathf.Max(0, slideDuration);
            this.exitDistance = Mathf.Max(0, exitDistance);
        }

        public void ShowGoldGain(int amount)
        {
            if (amount <= 0) return;
            ShowGain(amount, null, null);
        }

        public void ShowItemGain(string displayName, Sprite icon)
        {
            ShowGain(0, string.IsNullOrWhiteSpace(displayName) ? "아이템" : displayName, icon);
        }

        private void ShowGain(int amount, string itemName, Sprite icon)
        {
            if (!isActiveAndEnabled) return;
            double now = Time.unscaledTimeAsDouble;
            Tick(now);
            var newest = GetRow(ActiveRowCount - 1);
            // Merge deadline is fixed to the first reward, never extended by subsequent merges.
            if (itemName == null && newest != null && newest.IsGold && now - newest.StartedAt < mergeWindow)
            {
                newest.Merge(amount, now);
                newest.TickVisual(now, hold, fade, exitDistance);
                return;
            }
            if (ActiveRowCount == active.Length) RemoveAt(0);
            var free = FindFreeRow();
            if (free == null) return;
            // Shift targets instead of compacting after expiry: surviving rows never settle downward.
            float step = (free.Rect != null ? free.Rect.rect.height : 0f) + gap;
            float entranceRise = riseDistance;
            float entranceSeconds = enterDuration;
            if (ActiveRowCount > 0)
            {
                var bottom = active[ActiveRowCount - 1];
                float bottomY = bottom.Rect != null ? bottom.Rect.anchoredPosition.y : 0f;
                entranceRise = Mathf.Max(entranceRise, step - bottomY);
                entranceSeconds = Mathf.Max(entranceSeconds, slideDuration);
            }
            for (int i = 0; i < ActiveRowCount; i++)
            {
                active[i].ShiftUp(step, now, slideDuration);
                active[i].TickVisual(now, hold, fade, exitDistance);
            }
            active[ActiveRowCount++] = free;
            if (itemName == null) free.Show(amount, now, entranceRise, entranceSeconds);
            else free.ShowItem(itemName, icon, now, entranceRise, entranceSeconds);
        }

        private PentaGoldRewardRow FindFreeRow()
        {
            foreach (var row in rows)
            {
                if (row == null) continue;
                bool used = false;
                for (int i = 0; i < ActiveRowCount; i++) if (active[i] == row) used = true;
                if (!used) return row;
            }
            return null;
        }

        private void Update() { Tick(Time.unscaledTimeAsDouble); }
        private void Tick(double now)
        {
            for (int i = ActiveRowCount - 1; i >= 0; i--)
            {
                var row = active[i];
                double fadingFor = now - row.LastMergedAt - hold;
                if (fadingFor >= 0 && (fade <= 0 || fadingFor >= fade)) RemoveAt(i);
                else row.TickVisual(now, hold, fade, exitDistance);
            }
        }

        private void RemoveAt(int index)
        {
            active[index].Hide();
            for (int i = index; i < ActiveRowCount - 1; i++) active[i] = active[i + 1];
            active[--ActiveRowCount] = null;
        }

        private void OnEnable() { Clear(); }
        private void OnDisable() { Clear(); }
        public void Clear()
        {
            foreach (var row in active) if (row != null) row.Hide();
            foreach (var row in rows) if (row != null) row.Hide();
            for (int i = 0; i < active.Length; i++) active[i] = null;
            ActiveRowCount = 0;
        }
    }
}



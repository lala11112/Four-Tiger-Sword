using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class EngravingNodeDefinition
{
    public string id, parentId, displayName;
    [TextArea] public string description;
    public int tier;
    public float amount, secondary, duration, threshold, range, cap;
    public int limit;
    public EngravingNodeDefinition Copy() => (EngravingNodeDefinition)MemberwiseClone();
}

/// <summary>All engraving tuning. Percentages are ratios; backstab threshold is degrees.</summary>
[CreateAssetMenu(menuName = "Four Tiger Sword/Progression/Engravings", fileName = "EngravingSettings")]
public sealed class EngravingSettingsSO : ScriptableObject
{
    public int[] tierCosts = { 0, 10, 20, 40, 80 };
    public int baseRewardChoices = 3, emptyRewardGold = 10, baseRefreshCost = 20;
    public EngravingNodeDefinition[] nodes = CreateNodes();
    private static EngravingSettingsSO _defaults;
    public static EngravingSettingsSO Default
    {
        get
        {
            var installed = Resources.Load<EngravingSettingsSO>("EngravingSettings");
            if (installed != null) return installed;
            if (_defaults == null) { _defaults = CreateInstance<EngravingSettingsSO>(); _defaults.hideFlags = HideFlags.HideAndDontSave; }
            return _defaults;
        }
    }
    public EngravingNodeDefinition Find(string id)
    {
        if (nodes != null) foreach (var node in nodes) if (node != null && node.id == id) return node;
        return null;
    }
    public int Cost(string id) => Find(id) is { } node && tierCosts != null && node.tier >= 0 && node.tier < tierCosts.Length ? tierCosts[node.tier] : 0;
    public bool Validate(out string error)
    {
        error = null;
        if (tierCosts == null || tierCosts.Length != 5 || tierCosts[0] != 0 || nodes == null || nodes.Length != 38
            || baseRewardChoices < 1 || emptyRewardGold < 0 || baseRefreshCost < 0)
        { error = "각인 설정 구성이 유효하지 않습니다."; return false; }
        for (int i = 1; i < tierCosts.Length; i++) if (tierCosts[i] <= 0) { error = "각인 비용이 유효하지 않습니다."; return false; }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.id) || !ids.Add(node.id) || node.tier < 0 || node.tier > 4
                || string.IsNullOrWhiteSpace(node.displayName) || node.limit < 0)
            { error = "각인 노드가 중복되거나 유효하지 않습니다."; return false; }
            foreach (float value in new[] { node.amount, node.secondary, node.duration, node.threshold, node.range, node.cap })
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                { error = "각인 수치가 유효하지 않습니다."; return false; }
        }
        foreach (var node in nodes)
        {
            if (node.id == "origin") { if (node.tier != 0 || !string.IsNullOrEmpty(node.parentId)) { error = "시작 각인이 유효하지 않습니다."; return false; } continue; }
            var parent = Find(node.parentId);
            if (parent == null || parent.tier != node.tier - 1 && !(parent.id == "origin" && node.tier == 2)) { error = "각인 선행 관계가 유효하지 않습니다: " + node.id; return false; }
        }
        if (Find("origin") == null) { error = "시작 각인이 없습니다."; return false; }
        return true;
    }

    public string Describe(string id)
    {
        var n = Find(id); if (n == null) return string.Empty;
        string P(float v) => (v * 100f).ToString("0.#");
        return id switch
        {
            "atk" => $"기본 공격력 +{P(n.amount)}%",
            "hp" => $"기본 최대 체력 +{P(n.amount)}%",
            "crit" => $"치명타 확률 +{P(n.amount)}%p",
            "severe" => $"치명타 피해 배율 +{P(n.amount)}%p",
            "speed" => $"공격속도 +{P(n.amount)}%",
            "footwork" => $"이동속도 +{P(n.amount)}% (대시 거리 제외)",
            "bossdmg" => $"보스에게 주는 직접 피해 +{P(n.amount)}%",
            "pity" => $"비치명 직접 적중마다 치명타 확률 +{P(n.amount)}%p, 최대 +{P(n.cap)}%p. 치명타 발생 시 초기화",
            "critheal" => $"직접 치명타 처치 시 최대 체력의 {P(n.amount)}% 회복",
            "reach" => $"일반 근접공격 판정 범위 +{P(n.amount)}%",
            "opener" => $"만피 적의 첫 직접 타격 피해 +{P(n.amount)}%",
            "combo" => $"동일 대상 연속 직접 적중마다 피해 +{P(n.amount)}%, 최대 +{P(n.cap)}%. 첫 타격은 기본 피해, 대상 변경 또는 {n.duration:0.#}초 경과 시 초기화",
            "finisher" => $"체력 {P(n.threshold)}% 이하 적에게 직접 피해 +{P(n.amount)}%",
            "heal" => $"일반 회복량 +{P(n.amount)}% (부활 제외)",
            "guard" => $"피격 직전 체력 {P(n.threshold)}% 이하이면 받는 피해 -{P(n.amount)}%",
            "bossguard" => $"보스 개체별 첫 유효 피격 피해 -{P(n.amount)}%. 페이즈 전환으로 재충전되지 않음",
            "armor" => $"받는 피해 -{P(n.amount)}%",
            "shield" => $"초과 회복의 {P(n.amount)}%를 {n.duration:0.#}초 방어막으로 전환. 최종 각인 방어막은 최대 체력 {P(n.cap)}% 상한",
            "roomheal" => $"정상 전투방 최초 클리어 시 최대 체력의 {P(n.amount)}% 회복",
            "revive" => $"런당 {n.limit}회 치명 피해에서 체력 {P(n.amount)}% 복원, {n.duration:0.#}초 무적, 주변 {n.range:0.#}m 적 밀쳐내기",
            "crowdguard" => $"{n.range:0.#}m 안에 살아 있는 적 {n.limit}명 이상이면 받는 피해 -{P(n.amount)}%",
            "counter" => $"실제 공격 회피 후 {n.duration:0.#}초 안의 다음 직접 유효 적중 피해 +{P(n.amount)}%",
            "backstab" => $"적 후방 {n.threshold:0.#}° 범위에서 직접 피해 +{P(n.amount)}%",
            "pursuit" => $"직접 처치 후 {n.duration:0.#}초간 이동속도 +{P(n.amount)}%",
            "escape" => $"유효 피격 후 {n.duration:0.#}초간 이동속도 +{P(n.amount)}%",
            "haste" => $"직접 처치 후 {n.duration:0.#}초간 공격속도 +{P(n.amount)}%",
            "gold" => $"출격 골드 +{n.amount:0}",
            "firstblood" => $"일반 적의 원래 기본 처치 골드 +{P(n.amount)}%. 소수는 런 안에서 이월",
            "discount" => $"상점 아이템 구매 가격 -{P(n.amount)}%",
            "sell" => $"매각 가격이 기준가의 {P(n.amount)}%로 증가",
            "refresh" => $"상품 새로고침 비용 -{P(n.amount)}%",
            "stock" => $"서로 다른 아이템 상품 수 +{n.amount:0}",
            "reroll" => $"일반 전투 보상 재선택을 런당 총 {n.limit}회 제공",
            "hold" => $"재선택 시 후보 {n.limit}개를 보관하고 나머지만 교체",
            "choices" => $"일반 전투 보상 후보 +{n.amount:0}개 (선택한 1개만 획득)",
            "synergy" => $"다음 시너지 단계를 완성할 미보유 아이템의 일반 보상 가중치 ×{n.amount:0.#}",
            "fusion" => $"기존 합성 재료로 생성한 결과 후보 최대 {n.limit}개 중 1개 선택",
            _ => n.description ?? string.Empty
        };
    }
    public void RefreshDescriptions() { foreach (var n in nodes ?? Array.Empty<EngravingNodeDefinition>()) if (n != null) n.description = Describe(n.id); }
    private void OnValidate() => RefreshDescriptions();

    private static EngravingNodeDefinition[] CreateNodes()
    {
        var list = new List<EngravingNodeDefinition>();
        void N(string id, string parent, int tier, string name, float amount = 0, float duration = 0,
            float threshold = 0, float cap = 0, float range = 0, int limit = 0, float secondary = 0)
            => list.Add(new EngravingNodeDefinition { id = id, parentId = parent, tier = tier, displayName = name,
                amount = amount, duration = duration, threshold = threshold, cap = cap, range = range, limit = limit, secondary = secondary });
        N("origin", null, 0, "첫 각인"); list[0].description = "이미 열린 무료 시작점. 원하는 성장 가지를 선택하세요.";
        N("atk", "origin", 1, "공격력", .08f); N("hp", "origin", 1, "최대 체력", .10f);
        N("footwork", "origin", 1, "발놀림", .08f); N("gold", "origin", 1, "출격 자금", 30);
        N("crit", "atk", 2, "치명타 확률", .05f); N("bossdmg", "atk", 2, "거물 사냥", .10f); N("speed", "atk", 2, "공격 속도", .06f);
        N("heal", "hp", 2, "회복 증폭", .15f); N("guard", "hp", 2, "위기 방어", .20f, threshold: .30f);
        N("bossguard", "hp", 2, "거물 방어", .50f); N("armor", "hp", 2, "단단한 각인", .05f);
        N("counter", "footwork", 2, "회피 반격", .20f, duration: 5); N("pursuit", "footwork", 2, "추격", .15f, duration: 3); N("escape", "footwork", 2, "탈출", .15f, duration: 2);
        N("firstblood", "gold", 2, "전리품", .20f); N("discount", "gold", 2, "상점 할인", .10f); N("sell", "gold", 2, "매각 수완", .60f);
        N("reroll", "origin", 2, "보상 재선택", limit: 3); N("choices", "origin", 2, "넓은 선택", 1);
        N("severe", "crit", 3, "치명타 피해", .20f); N("pity", "crit", 3, "실패의 축적", .02f, cap: .20f);
        N("reach", "speed", 3, "긴 사거리", .15f); N("combo", "speed", 3, "연속 적중", .05f, duration: 3, cap: .15f);
        N("shield", "heal", 3, "넘치는 생명", .50f, duration: 10, cap: .10f); N("roomheal", "heal", 3, "전투의 숨", .03f);
        N("revive", "guard", 3, "다시 일어서기", .30f, duration: 2, range: 4, limit: 1, secondary: 8);
        N("crowdguard", "armor", 3, "포위 방어", .10f, range: 5, limit: 3); N("backstab", "counter", 3, "후방 공략", .20f, threshold: 120);
        N("haste", "pursuit", 3, "전투 가속", .10f, duration: 3); N("refresh", "discount", 3, "새로고침 할인", .25f);
        N("hold", "reroll", 3, "후보 보관", limit: 1); N("synergy", "choices", 3, "조합의 실마리", 3);
        N("critheal", "severe", 4, "치명 회복", .02f); N("opener", "reach", 4, "선제공격", .25f);
        N("finisher", "combo", 4, "마무리 일격", .15f, threshold: .30f); N("stock", "refresh", 4, "풍성한 진열", 1); N("fusion", "synergy", 4, "합성 선택", limit: 3);
        return list.ToArray();
    }
}

/// <summary>Run-owned immutable copy. Modifying assets or inventory cannot change a running build.</summary>
public sealed class EngravingSnapshot
{
    private readonly Dictionary<string, EngravingNodeDefinition> _nodes = new(StringComparer.Ordinal);
    public static EngravingSnapshot Empty { get; } = new EngravingSnapshot();
    public readonly int BaseRewardChoices, EmptyRewardGold, BaseRefreshCost;
    // Used by MonoBehaviour field initializers, which may run on Unity's loading thread.
    private EngravingSnapshot() { BaseRewardChoices = 3; EmptyRewardGold = 10; BaseRefreshCost = 20; }
    public EngravingSnapshot(EngravingSettingsSO settings, IEnumerable<string> owned)
    {
        settings ??= EngravingSettingsSO.Default;
        BaseRewardChoices = settings.baseRewardChoices; EmptyRewardGold = settings.emptyRewardGold; BaseRefreshCost = settings.baseRefreshCost;
        if (owned != null) foreach (string id in owned) { var node = settings.Find(id); if (node != null) _nodes[id] = node.Copy(); }
    }
    public bool Has(string id) => id == "origin" || id != null && _nodes.ContainsKey(id);
    private EngravingNodeDefinition Get(string id) => id != null && _nodes.TryGetValue(id, out var n) ? n : null;
    public float Amount(string id) => Get(id)?.amount ?? 0f;
    public float Secondary(string id) => Get(id)?.secondary ?? 0f;
    public float Duration(string id) => Get(id)?.duration ?? 0f;
    public float Threshold(string id) => Get(id)?.threshold ?? 0f;
    public float Range(string id) => Get(id)?.range ?? 0f;
    public float Cap(string id) => Get(id)?.cap ?? 0f;
    public int Limit(string id) => Get(id)?.limit ?? 0;
}

public readonly struct EngravingPurchaseState
{
    public readonly string Id, Error;
    public readonly bool IsUnlocked, CanPurchase;
    public readonly int Cost;
    public readonly EngravingNodeDefinition Definition;
    public EngravingPurchaseState(string id, EngravingNodeDefinition definition, bool unlocked, int cost, string error)
    { Id = id; Definition = definition; IsUnlocked = unlocked; Cost = cost; Error = error; CanPurchase = !unlocked && definition != null && error == null; }
}

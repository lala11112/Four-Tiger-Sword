using System;
using System.Collections.Generic;

namespace Penta.Upgrade
{
    /// <summary>Unconfirmed preview content and candidate relationships; no gameplay balance or unlock rules.</summary>
    public sealed class PentaUpgradeNode
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public string Family { get; }
        public IReadOnlyList<string> ParentIds { get; }
        /// <summary>Candidate relationship depth from the origin, not the vertical screen row.</summary>
        public int Tier { get; }
        /// <summary>Legacy concept ordering within a relationship depth; layout uses PentaUpgradeVerticalLayout.</summary>
        public int Row { get; }

        public PentaUpgradeNode(string id, string name, string description, string family,
            string[] parentIds, int tier, int row)
        {
            Id = id;
            Name = name;
            Description = description;
            Family = family;
            ParentIds = Array.AsReadOnly((string[])parentIds.Clone());
            Tier = tier;
            Row = row;
        }
    }

    public static class PentaUpgradeCatalog
    {
        public static IReadOnlyList<PentaUpgradeNode> Nodes { get; } = Array.AsReadOnly(new[]
        {
            new PentaUpgradeNode("origin", "첫 각인", "이미 열린 무료 시작점. 원하는 성장 가지를 선택해 출격 준비를 시작한다.", "시작점", new string[0], 0, 0),
            new PentaUpgradeNode("atk", "공격력", "기본 공격력이 증가한다. 공격 방식에 관계없이 적을 쓰러뜨리는 힘을 보탠다.", "검술", new[] { "origin" }, 1, 0),
            new PentaUpgradeNode("hp", "최대 체력", "최대 체력이 증가한다. 공격을 버틸 여유를 늘려 도전을 이어 간다.", "체력", new[] { "origin" }, 1, 1),
            new PentaUpgradeNode("footwork", "가벼운 발걸음", "이동 속도가 증가한다. 적의 공격 범위를 벗어나고 유리한 자리를 잡기 쉬워진다.", "회피", new[] { "origin" }, 1, 2),
            new PentaUpgradeNode("gold", "시작 골드", "출격할 때 받는 골드가 늘어난다. 첫 상점에서 필요한 물건을 준비할 여유를 만든다.", "상점", new[] { "origin" }, 1, 3),
            new PentaUpgradeNode("heirloom", "출격 유품", "출격할 때 무작위 아이템 후보 중에서 골라 받는다. 첫 아이템을 바탕으로 조합을 시작한다.", "전리품", new[] { "origin" }, 1, 4),
            new PentaUpgradeNode("crit", "치명타 확률", "공격의 치명타 확률이 증가한다. 강한 일격이 나올 기회를 늘린다.", "치명타", new[] { "atk" }, 2, 0),
            new PentaUpgradeNode("bossdmg", "대물 사냥", "보스에게 주는 피해가 증가한다. 보스의 빈틈에 가하는 공격을 더 강하게 만든다.", "보스 사냥", new[] { "atk" }, 2, 1),
            new PentaUpgradeNode("speed", "공격 속도", "공격 속도가 증가한다. 적의 빈틈에 공격을 이어 넣기 쉬워진다.", "검술", new[] { "atk" }, 2, 2),
            new PentaUpgradeNode("heal", "회복 증폭", "체력을 회복할 때 회복량이 증가한다. 같은 회복 기회로 다음 전투를 더 든든하게 준비한다.", "체력", new[] { "hp" }, 2, 3),
            new PentaUpgradeNode("guard", "위기 방어", "체력이 낮을 때 받는 피해가 감소한다. 위기에서 회복하거나 빠져나갈 여유를 만든다.", "부활", new[] { "hp" }, 2, 4),
            new PentaUpgradeNode("bossguard", "결전 대비", "보스전마다 처음 받는 공격 피해가 감소한다. 첫 실수를 견디고 전투를 이어 간다.", "보스 사냥", new[] { "hp" }, 2, 5),
            new PentaUpgradeNode("armor", "피해 경감", "적의 공격으로 받는 피해가 감소한다. 교전 중 체력 손실을 줄인다.", "체력", new[] { "hp" }, 2, 6),
            new PentaUpgradeNode("counter", "회피 반격", "적의 공격을 회피한 직후 다음 공격이 강화된다. 피한 빈틈을 반격 기회로 바꾼다.", "회피", new[] { "footwork" }, 2, 7),
            new PentaUpgradeNode("pursuit", "몰아치기", "적을 처치하면 잠시 이동 속도가 증가한다. 다음 적을 쫓거나 위험한 자리에서 빠져나온다.", "처치", new[] { "footwork" }, 2, 8),
            new PentaUpgradeNode("escape", "위기 탈출", "공격에 맞으면 잠시 이동 속도가 증가한다. 위험한 자리에서 빠져나올 틈을 얻는다.", "회피", new[] { "footwork" }, 2, 9),
            new PentaUpgradeNode("firstblood", "처치 골드", "일반 적을 처치할 때 얻는 골드가 증가한다. 전투를 통해 상점에서 쓸 자금을 모은다.", "처치", new[] { "gold" }, 2, 10),
            new PentaUpgradeNode("discount", "단골 할인", "상점의 아이템 구매 가격이 낮아진다. 모은 골드로 필요한 조합을 마련하기 쉬워진다.", "상점", new[] { "gold" }, 2, 11),
            new PentaUpgradeNode("sell", "매각 협상", "아이템을 팔 때 받는 골드가 증가한다. 쓰지 않는 물건을 다음 구매의 밑천으로 바꾼다.", "탐색", new[] { "gold" }, 2, 12),
            new PentaUpgradeNode("reroll", "보상 재선택", "보상 후보를 새로 뽑을 기회를 얻는다. 현재 조합에 맞는 아이템을 다시 찾아본다.", "탐색", new[] { "heirloom" }, 2, 13),
            new PentaUpgradeNode("starterelement", "유품의 계통: 시너지 선택", "1등급 출격 유품이 있는 시너지 계통을 선택한다. 해당 계통의 후보 중 하나를 받는다.", "전리품", new[] { "heirloom" }, 2, 14),
            new PentaUpgradeNode("choices", "넓어진 선택", "일반 전투 보상의 아이템 후보가 늘어난다. 받을 아이템을 고를 폭을 넓힌다.", "전리품", new[] { "heirloom" }, 2, 15),
            new PentaUpgradeNode("severe", "치명타 피해", "치명타로 주는 피해가 증가한다. 치명타가 적중했을 때 적의 체력을 더 크게 깎는다.", "치명타", new[] { "crit" }, 3, 0),
            new PentaUpgradeNode("pity", "예리한 감각", "치명타가 아닌 공격이 적중하면 치명타 확률이 쌓인다. 치명타가 적중하면 쌓인 효과가 사라진다.", "치명타", new[] { "crit" }, 3, 1),
            new PentaUpgradeNode("reach", "검격 범위", "근접 기본 공격이 닿는 범위가 늘어난다. 조금 떨어진 적도 검격으로 압박한다.", "검술", new[] { "speed" }, 3, 2),
            new PentaUpgradeNode("combo", "집중 공격", "같은 적을 직접 연속 공격하면 피해가 증가한다. 대상을 바꾸거나 공격을 멈추면 초기화된다.", "검술", new[] { "speed" }, 3, 3),
            new PentaUpgradeNode("shield", "온전한 수호", "회복량이 최대 체력을 넘으면 넘친 일부가 보호막이 된다. 남는 회복을 다음 피해에 대비해 쓴다.", "체력", new[] { "heal" }, 3, 4),
            new PentaUpgradeNode("roomheal", "전투 후 정비", "전투 방을 처음 클리어하면 체력을 회복한다. 다음 전투를 준비할 여유를 얻는다.", "체력", new[] { "heal" }, 3, 5),
            new PentaUpgradeNode("revive", "부활", "쓰러지면 부활 기회를 소모해 다시 일어난다. 체력을 일부 회복하고 잠시 무적이 되어 주변 적을 밀쳐낸다.", "부활", new[] { "guard" }, 3, 6),
            new PentaUpgradeNode("crowdguard", "포위 대응", "가까이에 여러 적이 있으면 받는 공격 피해가 감소한다. 포위된 상황에서 버틸 틈을 만든다.", "체력", new[] { "armor" }, 3, 7),
            new PentaUpgradeNode("backstab", "배후 공략", "적의 뒤에서 공격하면 주는 피해가 증가한다. 위치를 잡아 적의 빈틈을 공략한다.", "회피", new[] { "counter" }, 3, 8),
            new PentaUpgradeNode("haste", "처치의 기세", "적을 처치하면 잠시 공격 속도가 증가한다. 처치한 기세로 다음 적을 몰아붙인다.", "처치", new[] { "pursuit" }, 3, 9),
            new PentaUpgradeNode("refresh", "상점 재탐색", "상점의 상품 새로고침 비용이 낮아진다. 원하는 아이템을 찾아볼 여유를 늘린다.", "상점", new[] { "discount" }, 3, 10),
            new PentaUpgradeNode("hold", "후보 보관", "보상을 다시 뽑을 때 원하는 후보를 보관하고 나머지를 바꾼다. 좋은 선택지를 지키며 탐색한다.", "탐색", new[] { "reroll" }, 3, 11),
            new PentaUpgradeNode("synergy", "조합의 실마리", "아이템 보상에서 보유 조합을 완성할 미보유 아이템이 더 잘 등장한다. 조합 완성을 돕는다.", "전리품", new[] { "choices" }, 3, 12),
            new PentaUpgradeNode("critheal", "치명적 회수", "직접 공격의 치명타로 적을 처치하면 체력을 회복한다. 결정적인 일격으로 전투를 이어 간다.", "치명타", new[] { "severe" }, 4, 0),
            new PentaUpgradeNode("opener", "선제 타격", "체력이 가득 찬 적에게 가하는 첫 직접 공격의 피해가 증가한다. 선제공격을 강화한다.", "검술", new[] { "reach" }, 4, 1),
            new PentaUpgradeNode("finisher", "마무리 일격", "체력이 낮은 적에게 주는 직접 공격 피해가 증가한다. 약해진 적을 빠르게 마무리한다.", "검술", new[] { "combo" }, 4, 2),
            new PentaUpgradeNode("stock", "추가 진열", "상점에 서로 다른 상품이 더 진열된다. 구매할 아이템을 고를 폭을 넓힌다.", "상점", new[] { "refresh" }, 4, 3),
            new PentaUpgradeNode("fusion", "합성 선별", "기존 재료로 아이템을 합성할 때 결과 후보 중에서 선택한다. 조합에 맞는 결과를 고른다.", "전리품", new[] { "synergy" }, 4, 4),
        });

        /// <summary>Returns the concept node, or null if the id is unknown.</summary>
        public static PentaUpgradeNode Find(string id)
        {
            for (int i = 0; i < Nodes.Count; i++)
            {
                if (string.Equals(Nodes[i].Id, id, StringComparison.Ordinal))
                    return Nodes[i];
            }
            return null;
        }
    }
}

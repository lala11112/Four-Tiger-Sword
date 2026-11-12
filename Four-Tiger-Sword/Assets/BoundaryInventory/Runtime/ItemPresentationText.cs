using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace BoundaryZero.Inventory
{
    /// <summary>Shared rarity and tier formatting for inventory and gameplay item views.</summary>
    public static class ItemPresentationText
    {
        public const string ActiveColorHex = "F4B976";
        public const string InactiveColorHex = "8C99A8";
        private static readonly Regex Number = new Regex(@"\d+(?:\.\d+)?", RegexOptions.CultureInvariant);

        public static string Stars(int grade) => grade > 0 ? new string('★', Math.Min(grade, 4)) : "—";

        public static string TierCounts(IEnumerable<SynergyTier> source)
            => string.Join(" / ", Ordered(source).Select(t => t.count)) + "세트";

        public static string TierCounts(IEnumerable<SynergyTier> source, int ownedCount)
        {
            var tiers = Ordered(source);
            int active = ActiveIndex(tiers, ownedCount);
            return "<color=#" + InactiveColorHex + ">"
                + string.Join(" / ", tiers.Select((tier, index) => TierValue(tier.count.ToString(), index == active)))
                + "세트</color>";
        }

        public static string CompactSynergy(IEnumerable<SynergyTier> source)
            => CompactSynergy(source, null);

        /// <summary>Highlights only the highest reached tier; lower tiers do not stack.</summary>
        public static string CompactSynergy(IEnumerable<SynergyTier> source, int ownedCount)
            => CompactSynergy(source, (int?)ownedCount);

        public static string StateText(string text, bool active)
            => ColorText(Literal(text), active);

        private static string CompactSynergy(IEnumerable<SynergyTier> source, int? ownedCount)
        {
            var tiers = Ordered(source);
            if (tiers.Length == 0) return string.Empty;
            int active = ownedCount.HasValue ? ActiveIndex(tiers, ownedCount.Value) : -1;
            var lines = tiers.Select(t => Normalize(t.text).Split(new[] { " · " }, StringSplitOptions.None)
                .Select(x => x.Trim()).ToArray()).ToArray();
            if (lines.Any(x => x.Length != lines[0].Length)) return Expanded(tiers, ownedCount, active);
            var result = new List<string>();
            for (int clause = 0; clause < lines[0].Length; clause++)
            {
                var phrases = lines.Select(x => x[clause]).ToArray();
                var pattern = Number.Replace(phrases[0], "#");
                if (phrases.Any(x => Number.Replace(x, "#") != pattern)) return Expanded(tiers, ownedCount, active);
                var matches = phrases.Select(x => Number.Matches(x).Cast<Match>().ToArray()).ToArray();
                var text = new StringBuilder();
                int offset = 0;
                for (int index = 0; index < matches[0].Length; index++)
                {
                    var match = matches[0][index];
                    var prefix = phrases[0].Substring(offset, match.Index - offset);
                    text.Append(ownedCount.HasValue ? Literal(prefix) : prefix);
                    var values = matches.Select(x => x[index].Value).ToArray();
                    text.Append(values.Distinct().Count() == 1 ? values[0] : "(" + string.Join(" / ",
                        values.Select((value, tier) => ownedCount.HasValue ? TierValue(value, tier == active) : value)) + ")");
                    offset = match.Index + match.Length;
                }
                var suffix = phrases[0].Substring(offset);
                text.Append(ownedCount.HasValue ? Literal(suffix) : suffix);
                result.Add(text.ToString());
            }
            var compact = string.Join("\n", result);
            return ownedCount.HasValue ? ColorText(compact, active >= 0) : compact;
        }

        private static SynergyTier[] Ordered(IEnumerable<SynergyTier> source)
            => (source ?? Array.Empty<SynergyTier>()).Where(t => t != null).OrderBy(t => t.count).ToArray();

        private static int ActiveIndex(SynergyTier[] tiers, int ownedCount)
            => Array.FindLastIndex(tiers, tier => tier.count > 0 && tier.count <= ownedCount);

        private static string TierValue(string value, bool active)
            => active ? "<color=#" + ActiveColorHex + "><b>" + value + "</b></color>"
                : "<color=#" + InactiveColorHex + ">" + value + "</color>";

        private static string ColorText(string value, bool active)
            => "<color=#" + (active ? ActiveColorHex : InactiveColorHex) + ">" + value + "</color>";

        private static string Literal(string text)
            => (text ?? string.Empty).Replace("<", "<noparse><</noparse>");

        private static string Expanded(SynergyTier[] tiers, int? ownedCount, int active)
            => string.Join("\n", tiers.Select((tier, index) => {
                var text = tier.count + "세트 · " + Normalize(tier.text);
                return ownedCount.HasValue ? StateText(text, index == active) : text;
            }));

        private static string Normalize(string text) => (text ?? string.Empty)
            .Replace("평타 공격속도", "기본 공격 속도").Replace("평타", "기본 공격")
            .Replace("공격속도", "기본 공격 속도").Replace("치명타 피해 배율", "치명타 피해")
            .Replace("기본 공격·스킬 피해 계산 시 양수 유효 방어력의", "기본 공격·스킬: 방어력")
            .Replace("HP를 잃은 직후", "HP 손실 시").Replace("재사용 대기시간", "쿨타임")
            .Replace("적 처치 기본 골드 보너스", "처치 기본 골드 +");
    }
}

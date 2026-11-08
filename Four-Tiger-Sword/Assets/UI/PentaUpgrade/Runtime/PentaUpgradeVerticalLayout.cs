using System;
using System.Collections.Generic;
using UnityEngine;

namespace Penta.Upgrade
{
    /// <summary>Selected vertical layout geometry only; node semantics are not finalized.</summary>
    public static class PentaUpgradeVerticalLayout
    {
        public static readonly Vector2 Size = new Vector2(1178f, 5276f);
        private const float Scale = 1.6f;

        // Raw approved preview coordinates. Public coordinates use a top-left origin and positive Y down.
        private static readonly Dictionary<string, Vector2> Positions =
            new Dictionary<string, Vector2>(StringComparer.Ordinal)
            {
                { "origin", new Vector2(0f, 0f) },
                { "atk", new Vector2(-140f, 160f) },
                { "hp", new Vector2(140f, 820f) },
                { "footwork", new Vector2(-140f, 1320f) },
                { "gold", new Vector2(140f, 1820f) },
                { "heirloom", new Vector2(-140f, 2480f) },
                { "crit", new Vector2(-280f, 320f) },
                { "bossdmg", new Vector2(0f, 320f) },
                { "speed", new Vector2(280f, 320f) },
                { "heal", new Vector2(-280f, 980f) },
                { "guard", new Vector2(-140f, 980f) },
                { "bossguard", new Vector2(140f, 980f) },
                { "armor", new Vector2(280f, 980f) },
                { "counter", new Vector2(-280f, 1480f) },
                { "pursuit", new Vector2(0f, 1480f) },
                { "escape", new Vector2(280f, 1480f) },
                { "firstblood", new Vector2(-280f, 1980f) },
                { "discount", new Vector2(0f, 1980f) },
                { "sell", new Vector2(280f, 1980f) },
                { "reroll", new Vector2(-280f, 2640f) },
                { "starterelement", new Vector2(0f, 2640f) },
                { "choices", new Vector2(280f, 2640f) },
                { "severe", new Vector2(-280f, 480f) },
                { "pity", new Vector2(-140f, 480f) },
                { "reach", new Vector2(140f, 480f) },
                { "combo", new Vector2(280f, 480f) },
                { "shield", new Vector2(-280f, 1140f) },
                { "roomheal", new Vector2(-140f, 1140f) },
                { "revive", new Vector2(0f, 1140f) },
                { "crowdguard", new Vector2(280f, 1140f) },
                { "backstab", new Vector2(-280f, 1640f) },
                { "haste", new Vector2(0f, 1640f) },
                { "refresh", new Vector2(0f, 2140f) },
                { "hold", new Vector2(-280f, 2800f) },
                { "synergy", new Vector2(280f, 2800f) },
                { "critheal", new Vector2(-280f, 640f) },
                { "opener", new Vector2(140f, 640f) },
                { "finisher", new Vector2(280f, 640f) },
                { "stock", new Vector2(0f, 2300f) },
                { "fusion", new Vector2(280f, 2960f) },
            };

        private static readonly Dictionary<string, Vector2[]> Routes =
            new Dictionary<string, Vector2[]>(StringComparer.Ordinal)
            {
                { "atk", new[] { new Vector2(0f, 0f), new Vector2(70f, 70f), new Vector2(70f, 90f), new Vector2(-70f, 90f), new Vector2(-140f, 160f) } },
                { "hp", new[] { new Vector2(0f, 0f), new Vector2(70f, 70f), new Vector2(70f, 750f), new Vector2(140f, 820f) } },
                { "footwork", new[] { new Vector2(0f, 0f), new Vector2(70f, 70f), new Vector2(70f, 1250f), new Vector2(-70f, 1250f), new Vector2(-140f, 1320f) } },
                { "gold", new[] { new Vector2(0f, 0f), new Vector2(70f, 70f), new Vector2(70f, 1750f), new Vector2(140f, 1820f) } },
                { "heirloom", new[] { new Vector2(0f, 0f), new Vector2(70f, 70f), new Vector2(70f, 2410f), new Vector2(-70f, 2410f), new Vector2(-140f, 2480f) } },
                { "crit", new[] { new Vector2(-140f, 160f), new Vector2(-280f, 300f), new Vector2(-280f, 320f) } },
                { "bossdmg", new[] { new Vector2(-140f, 160f), new Vector2(0f, 300f), new Vector2(0f, 320f) } },
                { "speed", new[] { new Vector2(-140f, 160f), new Vector2(120f, 160f), new Vector2(280f, 320f) } },
                { "heal", new[] { new Vector2(140f, 820f), new Vector2(-120f, 820f), new Vector2(-280f, 980f) } },
                { "guard", new[] { new Vector2(140f, 820f), new Vector2(-20f, 980f), new Vector2(-140f, 980f) } },
                { "bossguard", new[] { new Vector2(140f, 820f), new Vector2(140f, 980f) } },
                { "armor", new[] { new Vector2(140f, 820f), new Vector2(280f, 960f), new Vector2(280f, 980f) } },
                { "counter", new[] { new Vector2(-140f, 1320f), new Vector2(-280f, 1460f), new Vector2(-280f, 1480f) } },
                { "pursuit", new[] { new Vector2(-140f, 1320f), new Vector2(0f, 1460f), new Vector2(0f, 1480f) } },
                { "escape", new[] { new Vector2(-140f, 1320f), new Vector2(120f, 1320f), new Vector2(280f, 1480f) } },
                { "firstblood", new[] { new Vector2(140f, 1820f), new Vector2(-120f, 1820f), new Vector2(-280f, 1980f) } },
                { "discount", new[] { new Vector2(140f, 1820f), new Vector2(0f, 1960f), new Vector2(0f, 1980f) } },
                { "sell", new[] { new Vector2(140f, 1820f), new Vector2(280f, 1960f), new Vector2(280f, 1980f) } },
                { "reroll", new[] { new Vector2(-140f, 2480f), new Vector2(-280f, 2620f), new Vector2(-280f, 2640f) } },
                { "starterelement", new[] { new Vector2(-140f, 2480f), new Vector2(0f, 2620f), new Vector2(0f, 2640f) } },
                { "choices", new[] { new Vector2(-140f, 2480f), new Vector2(120f, 2480f), new Vector2(280f, 2640f) } },
                { "severe", new[] { new Vector2(-280f, 320f), new Vector2(-280f, 480f) } },
                { "pity", new[] { new Vector2(-280f, 320f), new Vector2(-140f, 460f), new Vector2(-140f, 480f) } },
                { "reach", new[] { new Vector2(280f, 320f), new Vector2(140f, 460f), new Vector2(140f, 480f) } },
                { "combo", new[] { new Vector2(280f, 320f), new Vector2(280f, 480f) } },
                { "shield", new[] { new Vector2(-280f, 980f), new Vector2(-280f, 1140f) } },
                { "roomheal", new[] { new Vector2(-280f, 980f), new Vector2(-140f, 1120f), new Vector2(-140f, 1140f) } },
                { "revive", new[] { new Vector2(-140f, 980f), new Vector2(0f, 1120f), new Vector2(0f, 1140f) } },
                { "crowdguard", new[] { new Vector2(280f, 980f), new Vector2(280f, 1140f) } },
                { "backstab", new[] { new Vector2(-280f, 1480f), new Vector2(-280f, 1640f) } },
                { "haste", new[] { new Vector2(0f, 1480f), new Vector2(0f, 1640f) } },
                { "refresh", new[] { new Vector2(0f, 1980f), new Vector2(0f, 2140f) } },
                { "hold", new[] { new Vector2(-280f, 2640f), new Vector2(-280f, 2800f) } },
                { "synergy", new[] { new Vector2(280f, 2640f), new Vector2(280f, 2800f) } },
                { "critheal", new[] { new Vector2(-280f, 480f), new Vector2(-280f, 640f) } },
                { "opener", new[] { new Vector2(140f, 480f), new Vector2(140f, 640f) } },
                { "finisher", new[] { new Vector2(280f, 480f), new Vector2(280f, 640f) } },
                { "stock", new[] { new Vector2(0f, 2140f), new Vector2(0f, 2300f) } },
                { "fusion", new[] { new Vector2(280f, 2800f), new Vector2(280f, 2960f) } },
            };

        public static bool HasNode(string id)
        {
            return id != null && Positions.ContainsKey(id);
        }

        public static Vector2 Position(string id)
        {
            Vector2 raw;
            if (id == null || !Positions.TryGetValue(id, out raw))
                throw new ArgumentException("Unknown vertical layout node ID: " + (id ?? "<null>"), nameof(id));
            return Transform(raw);
        }

        /// <summary>Returns a fresh center-to-center route; callers may trim its render ports.</summary>
        public static Vector2[] Route(string childId)
        {
            Vector2[] raw;
            if (childId == null || !Routes.TryGetValue(childId, out raw))
                throw new ArgumentException("Unknown vertical layout child route ID: " + (childId ?? "<null>"), nameof(childId));
            var result = new Vector2[raw.Length];
            for (int i = 0; i < raw.Length; i++)
                result[i] = Transform(raw[i]);
            return result;
        }

        private static Vector2 Transform(Vector2 raw)
        {
            return new Vector2(589f + raw.x * Scale, 280f + raw.y * Scale);
        }
    }
}

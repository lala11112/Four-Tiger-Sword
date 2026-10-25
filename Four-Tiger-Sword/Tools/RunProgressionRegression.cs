using System;
using System.Linq;
using StudioBaekho;
using UnityEditor;
using UnityEngine;

public static class RunProgressionRegression
{
    private static int checks;
    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run in Edit Mode.");
        checks = 0;
        var definition = AssetDatabase.LoadAssetAtPath<RunDefinitionSO>("Assets/_Game/RunProgression/DefaultRun.asset");
        Check(definition != null && definition.Validate(out _), "Installed run is invalid.");
        Check(definition.TotalStages == definition.chapters.Sum(c => c.stages.Length), "Total count differs from list.");
        int c = 0, s = 0, visited = 1;
        while (definition.TryGetNext(c, s, out int nc, out int ns)) { c = nc; s = ns; visited++; }
        Check(visited == definition.TotalStages && c == definition.chapters.Length - 1, "Chapter traversal/termination is wrong.");
        Check(RunDefinitionSO.GetStageSeed(123, 0, 0) == RunDefinitionSO.GetStageSeed(123, 0, 0), "Seed is unstable.");
        Check(RunDefinitionSO.GetStageSeed(123, 0, 0) != RunDefinitionSO.GetStageSeed(123, 1, 0), "Chapter seeds collide.");
        foreach (bool boss in new[] { false, true })
        {
            var original = definition.chapters[0].stages[boss ? definition.chapters[0].stages.Length - 1 : 0];
            // Retain the original no-shop checks as well as exercising the installed shop capability.
            foreach (bool withShop in original.dungeonSettings.shopRoom != null ? new[] { false, true } : new[] { false })
            {
                var settings = UnityEngine.Object.Instantiate(original.dungeonSettings);
                try
                {
                    if (!withShop) settings.shopRoom = null;
                    int required = withShop ? (boss ? 5 : 4) : (boss ? 3 : 2);
                    int baseline = withShop ? required + 1 : 4;
                    int minimum = Math.Max(required, baseline - 1), maximum = baseline + 1;
                    var stage = new StageDefinition { dungeonSettings = settings, enemyCatalog = original.enemyCatalog,
                        mapCountVariation = 1, bossPrefab = original.bossPrefab };
                    settings.mapCount = baseline;
                    var counts = new int[maximum - minimum + 1];
                    for (int seed = 0; seed < 600; seed++)
                    {
                        int count = stage.ResolveMapCount(seed, boss);
                        Check(count >= minimum && count <= maximum, "Room count left its role-aware range.");
                        Check(count == stage.ResolveMapCount(seed, boss), "Same seed changed room count.");
                        counts[count - minimum]++;
                    }
                    Check(counts.All(n => n > 130 && n < 270), "Room range is not plausibly uniform.");
                    stage.mapCountVariation = 0;
                    Check(stage.ResolveMapCount(7, boss) == baseline, "Zero variation is not fixed.");
                    stage.mapCountVariation = 1;
                    settings.mapCount = required;
                    for (int seed = 0; seed < 25; seed++) Check(stage.ResolveMapCount(seed, boss) >= required, "Missing required role slots.");
                    settings.mapCount--;
                    Check(!stage.Validate(boss, out _), "Invalid base count was silently repaired.");
                    settings.mapCount = baseline;
                    int assetCount = original.dungeonSettings.mapCount;
                    var copy = stage.CreateRuntimeSettings(123, boss);
                    Check(copy != settings && settings.mapCount == baseline && original.dungeonSettings.mapCount == assetCount,
                        "Runtime configuration mutated authored settings.");
                    UnityEngine.Object.DestroyImmediate(copy);
                    if (boss)
                    {
                        var saved = stage.bossPrefab;
                        stage.bossPrefab = null;
                        Check(!stage.Validate(true, out _), "Missing boss passed validation.");
                        stage.bossPrefab = saved;
                    }
                    foreach (int count in withShop ? new[] { required, required + 1, required + 2 } : new[] { 3, 4, 5 })
                    {
                        settings.mapCount = count;
                        Check(BaekhoDungeonPlanner.TryPlan(settings, 12345, out var first, out var error), error);
                        Check(BaekhoDungeonPlanner.TryPlan(settings, 12345, out var second, out error), error);
                        Check(first.rooms.Count == count && Signature(first) == Signature(second), "Planner count or deterministic layout changed.");
                        if (withShop)
                        {
                            Check(first.rooms.Count(room => room.role == BaekhoRoomRole.Shop) == 1
                                && first.links.Count(link => link.fromMapId == first.shopMapId || link.toMapId == first.shopMapId) == 1,
                                "Shop is missing, repeated or no longer a terminal branch.");
                        }
                        else Check(first.shopMapId == -1, "A legacy stage unexpectedly generated a shop.");
                        if (boss)
                        {
                            var exitLinks = first.links.Where(l => l.fromMapId == first.endMapId || l.toMapId == first.endMapId).ToArray();
                            Check(exitLinks.Length == 1 && (exitLinks[0].fromMapId == first.bossMapId || exitLinks[0].toMapId == first.bossMapId), "Exit can bypass boss.");
                        }
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(settings); }
            }
        }
        return $"Run definition / room count / planner: {checks} checks passed.";
    }

    private static string Signature(BaekhoDungeonPlan plan) => string.Join("|", plan.rooms.Select(r => r.id + ":" + r.prefab.name + ":" + r.position + ":" + r.quarterTurns));
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
}

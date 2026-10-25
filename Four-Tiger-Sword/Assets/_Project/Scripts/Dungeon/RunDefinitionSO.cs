using System;
using StudioBaekho;
using UnityEngine;

[Serializable]
public sealed class StageDefinition
{
    public string displayName = "Stage";
    public BaekhoDungeonSettings dungeonSettings;
    public DungeonEnemyCatalog enemyCatalog;
    [Min(0), Tooltip("Random room count around Dungeon Settings > Map Count. Zero keeps it fixed.")]
    public int mapCountVariation = 1;
    [Tooltip("Required only on the last stage of a chapter.")]
    public GameObject bossPrefab;

    public bool Validate(bool bossStage, out string error)
    {
        error = null;
        int minimumCount = MinimumMapCount(bossStage);
        if (dungeonSettings == null) error = "Dungeon Settings is missing.";
        else if (enemyCatalog == null) error = "Enemy Catalog is missing.";
        else if (mapCountVariation < 0) error = "Room count variation must not be negative.";
        else if (dungeonSettings.mapCount < minimumCount)
            error = $"A {(bossStage ? "boss stage" : "stage")} needs base Map Count >= {minimumCount}.";
        else if ((long)dungeonSettings.mapCount + mapCountVariation >= int.MaxValue) error = "Room count range is too large.";
        else if (bossStage && (dungeonSettings.bossRoom == null || bossPrefab == null))
            error = "The last stage of each chapter requires a Boss Room and Boss Prefab.";
        else if (!bossStage && (dungeonSettings.bossRoom != null || bossPrefab != null))
            error = "Only the last stage of a chapter may contain a boss.";
        else if (!ValidateAnchor(dungeonSettings.startRoom, 0, out error)) { }
        else if (!ValidateAnchor(dungeonSettings.endRoom, 1, out error)) { }
        else if (bossStage && !ValidateAnchor(dungeonSettings.bossRoom, 2, out error)) { }
        else if (dungeonSettings.shopRoom != null && !ValidateAnchor(dungeonSettings.shopRoom, 3, out error)) { }
        else if (dungeonSettings.shopRoom != null && (dungeonSettings.shopRoom == dungeonSettings.startRoom
            || dungeonSettings.shopRoom == dungeonSettings.endRoom || dungeonSettings.shopRoom == dungeonSettings.bossRoom))
            error = "Shop Room must use a separate prefab from start, end and boss rooms.";
        else if (bossStage && (bossPrefab.GetComponent<Enemy>() == null || bossPrefab.GetComponent<UnityEngine.AI.NavMeshAgent>() == null))
            error = "Boss Prefab needs Enemy and NavMeshAgent on its root.";
        return error == null;
    }

    private static bool ValidateAnchor(BaekhoMapModule room, int role, out string error)
    {
        string label = role == 0 ? "Start Room / Player Spawn" : role == 1 ? "End Room / Exit Point"
            : role == 2 ? "Boss Room / Boss Spawn" : "Shop Room / Shop Point";
        var anchors = room != null ? room.GetComponent<StageMapAnchors>() : null;
        var point = anchors == null ? null : role == 0 ? anchors.playerSpawn : role == 1 ? anchors.exitPoint
            : role == 2 ? anchors.bossSpawn : anchors.shopPoint;
        error = point == null || !point.IsChildOf(room.transform) ? label + " anchor is missing or outside its room prefab." : null;
        if (error == null && role == 1 && (anchors.exitSize.x <= 0 || anchors.exitSize.y <= 0 || anchors.exitSize.z <= 0))
            error = "Exit Size must be positive.";
        if (error == null && role == 3)
        {
            Vector3 local = room.transform.InverseTransformPoint(point.position);
            if (!IsFinite(local.x) || !IsFinite(local.y) || !IsFinite(local.z) || !room.localBounds.Contains(local))
                error = "Shop Point must be finite and inside the shop room geometry bounds.";
        }
        return error == null;
    }

    private int MinimumMapCount(bool bossStage)
        => dungeonSettings != null && dungeonSettings.shopRoom != null ? (bossStage ? 5 : 4) : (bossStage ? 3 : 2);

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    public int ResolveMapCount(int stageSeed, bool bossStage)
    {
        if (!Validate(bossStage, out string error)) throw new InvalidOperationException(error);
        int minimum = Math.Max(MinimumMapCount(bossStage), dungeonSettings.mapCount - mapCountVariation);
        int maximum = dungeonSettings.mapCount + mapCountVariation;
        return new System.Random(unchecked(stageSeed ^ 0x6B43A9D1)).Next(minimum, maximum + 1);
    }

    public BaekhoDungeonSettings CreateRuntimeSettings(int stageSeed, bool bossStage)
    {
        int count = ResolveMapCount(stageSeed, bossStage);
        var copy = UnityEngine.Object.Instantiate(dungeonSettings);
        copy.name = dungeonSettings.name + " (Runtime)";
        copy.hideFlags = HideFlags.DontSave;
        copy.mapCount = count;
        copy.randomizeSeed = false;
        copy.seed = stageSeed;
        return copy;
    }
}

[Serializable]
public sealed class ChapterDefinition
{
    public string displayName = "Chapter";
    public StageDefinition[] stages = Array.Empty<StageDefinition>();
}

[CreateAssetMenu(menuName = "Four Tiger Sword/Run Definition", fileName = "RunDefinition")]
public sealed class RunDefinitionSO : ScriptableObject
{
    public ChapterDefinition[] chapters = Array.Empty<ChapterDefinition>();
    public int TotalStages
    {
        get
        {
            int count = 0;
            if (chapters != null) foreach (var chapter in chapters) count += chapter?.stages?.Length ?? 0;
            return count;
        }
    }

    public bool Validate(out string error)
    {
        error = null;
        if (chapters == null || chapters.Length == 0) { error = "Run Definition needs at least one chapter."; return false; }
        for (int c = 0; c < chapters.Length; c++)
        {
            var stages = chapters[c]?.stages;
            if (stages == null || stages.Length == 0) { error = $"Chapter {c + 1} has no stages."; return false; }
            for (int s = 0; s < stages.Length; s++)
            {
                if (stages[s] == null) { error = $"Stage {c + 1}-{s + 1} is missing."; return false; }
                if (!stages[s].Validate(s == stages.Length - 1, out string reason))
                { error = $"Stage {c + 1}-{s + 1}: {reason}"; return false; }
            }
        }
        return true;
    }

    public bool TryGetNext(int chapter, int stage, out int nextChapter, out int nextStage)
    {
        nextChapter = chapter;
        nextStage = stage + 1;
        if (chapters == null || chapter < 0 || chapter >= chapters.Length || stage < 0 || stage >= chapters[chapter].stages.Length) return false;
        if (nextStage < chapters[chapter].stages.Length) return true;
        nextChapter++;
        nextStage = 0;
        return nextChapter < chapters.Length;
    }

    public static int GetStageSeed(int runSeed, int chapter, int stage)
    {
        unchecked
        {
            uint value = (uint)runSeed ^ ((uint)(chapter + 1) * 0x9E3779B9u) ^ ((uint)(stage + 1) * 0x85EBCA6Bu);
            value ^= value >> 16; value *= 0x7FEB352Du; value ^= value >> 15; value *= 0x846CA68Bu; value ^= value >> 16;
            return (int)value;
        }
    }
}

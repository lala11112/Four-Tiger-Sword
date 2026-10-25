using System;
using System.Collections.Generic;
using StudioBaekho;
using UnityEditor;
using UnityEngine;

/// <summary>One-time, idempotent wiring of a dedicated shop room into an existing run.</summary>
public static class ShopRoomSetup
{
    public const string ShopRoomPath = "Assets/_Game/RunProgression/Rooms/RunShop.prefab";
    private const string SourceRoomPath = "Assets/_Game/RunProgression/Rooms/RunStart.prefab";

    public static string Install(RunDefinitionSO definition)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before installing the shop room.");
        if (definition == null || definition.chapters == null)
            throw new ArgumentException("An existing run definition is required.", nameof(definition));

        var settingsToConfigure = new Dictionary<BaekhoDungeonSettings, bool>();
        foreach (var chapter in definition.chapters)
        {
            if (chapter == null || chapter.stages == null || chapter.stages.Length == 0)
                throw new InvalidOperationException("Every chapter needs an existing stage list.");
            for (int index = 0; index < chapter.stages.Length; index++)
            {
                var stage = chapter.stages[index];
                if (stage == null || stage.dungeonSettings == null)
                    throw new InvalidOperationException("Every stage needs existing dungeon settings.");
                var settings = stage.dungeonSettings;
                bool boss = index == chapter.stages.Length - 1 || settings.bossRoom != null;
                settingsToConfigure.TryGetValue(settings, out bool alreadyBoss);
                settingsToConfigure[settings] = alreadyBoss || boss;
                if (settings.shopRoom == null && settings.mapCount == int.MaxValue)
                    throw new InvalidOperationException("Cannot add a shop slot to Map Count at its integer limit.");
            }
        }

        var shop = GetOrCreateShopRoom();
        int changed = 0;
        foreach (var pair in settingsToConfigure)
        {
            var settings = pair.Key;
            // Existing shop references and counts belong to the author, including on repeated installs.
            if (settings.shopRoom != null) continue;
            settings.shopRoom = shop;
            settings.mapCount = Math.Max(pair.Value ? 5 : 4, settings.mapCount + 1);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
            changed++;
        }
        return $"Shop room ready: {ShopRoomPath}. Added one shop slot to {changed} dungeon settings; existing shop settings were preserved.";
    }

    private static BaekhoMapModule GetOrCreateShopRoom()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(ShopRoomPath);
        if (existing != null)
        {
            var module = existing.GetComponent<BaekhoMapModule>();
            if (module == null)
                throw new InvalidOperationException("The authored RunShop prefab has no BaekhoMapModule.");
            return module;
        }
        if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(ShopRoomPath)))
            throw new InvalidOperationException("RunShop already exists but is not a readable prefab; it was preserved.");

        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourceRoomPath);
        var sourceModule = source != null ? source.GetComponent<BaekhoMapModule>() : null;
        var pointPosition = new Vector3(0f, 0.1f, 2f);
        if (sourceModule == null || sourceModule.connections == null || sourceModule.connections.Length == 0
            || sourceModule.connections[0] == null || !sourceModule.localBounds.Contains(pointPosition))
            throw new InvalidOperationException("RunStart must have prepared room geometry and a doorway before copying the shop.");
        if (!AssetDatabase.CopyAsset(SourceRoomPath, ShopRoomPath))
            throw new InvalidOperationException("Could not create RunShop from the current RunStart prefab.");

        var root = PrefabUtility.LoadPrefabContents(ShopRoomPath);
        try
        {
            root.name = "RunShop";
            var module = root.GetComponent<BaekhoMapModule>();
            module.encounterBudget = 0;
            foreach (var marker in root.GetComponentsInChildren<BaekhoGameplayMarker>(true))
                UnityEngine.Object.DestroyImmediate(marker);

            var anchors = root.GetComponent<StageMapAnchors>() ?? root.AddComponent<StageMapAnchors>();
            var point = anchors.playerSpawn;
            anchors.playerSpawn = null;
            anchors.exitPoint = null;
            anchors.bossSpawn = null;
            if (point == null || point == root.transform)
                point = new GameObject("ShopPoint").transform;
            point.name = "ShopPoint";
            point.SetParent(root.transform, false);
            point.localPosition = pointPosition;
            Vector3 facing = module.connections[0].transform.position - point.position;
            facing.y = 0f;
            point.rotation = facing.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(facing.normalized, Vector3.up) : root.transform.rotation;
            anchors.shopPoint = point;
            if (PrefabUtility.SaveAsPrefabAsset(root, ShopRoomPath) == null)
                throw new InvalidOperationException("Could not save the new RunShop prefab.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return AssetDatabase.LoadAssetAtPath<GameObject>(ShopRoomPath).GetComponent<BaekhoMapModule>();
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using StudioBaekho;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Isolated combat-source checks; does not generate a dungeon or edit scene assets.</summary>
public static class FormSpEncounterRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Studio Baekho/Dungeon/Run Form SP Encounter Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run form SP encounter checks in Edit Mode.");
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }
        try
        {
            SceneManager.SetActiveScene(scene);
            var player = new GameObject("Form SP player").AddComponent<PlayerController>();
            var other = new GameObject("Unrelated player").AddComponent<PlayerController>();
            var director = new GameObject("Form SP director").AddComponent<DungeonEncounterDirector>();
            var generator = director.GetComponent<BaekhoDungeonGenerator>();
            generator.generateOnStart = false;
            var root = new GameObject("Current dungeon");
            var result = new BaekhoDungeonResult { success = true, root = root };
            Set(director, "_generator", generator);
            Set(director, "player", player.GetComponent<CharacterController>());
            Set(director, "_root", root);
            Property(director, "IsReady", true);
            Property(generator, "CurrentRoot", root);
            Property(generator, "LastResult", result);
            Invoke(director, "FindPlayer");
            Check(!player.IsInRoomCombat, "A dungeon with no fighting rooms counted as combat.");

            var room = new GameObject("Combat room").AddComponent<DungeonRoomEncounter>();
            room.transform.SetParent(root.transform, false);
            Set(room, "_director", director);
            var member = new GameObject("Tracked encounter member").AddComponent<DungeonEncounterMember>();
            member.transform.SetParent(room.transform, false);
            // Only room state is under test; enemy activation/native NavMesh is covered by encounter regressions.
            var alive = (HashSet<DungeonEncounterMember>)Get(room, "_alive");
            alive.Add(member);
            var rooms = (List<DungeonRoomEncounter>)Get(director, "_rooms");
            rooms.Add(room);
            foreach (var state in new[] { DungeonEncounterState.Preparing, DungeonEncounterState.Waiting,
                DungeonEncounterState.Cleared, DungeonEncounterState.Failed })
            {
                Property(room, "State", state);
                Check(!player.IsInRoomCombat, state + " counted as combat.");
            }
            Property(room, "State", DungeonEncounterState.Fighting);
            Check(room.IsCombatActive && player.IsInRoomCombat, "A fighting room did not activate room combat.");
            Check(!director.IsPlayerInCombat(other) && !other.IsInRoomCombat, "Combat leaked to an unrelated player.");
            Set(room, "_isBoss", true);
            Check(player.IsInRoomCombat, "Boss combat was excluded.");
            Set(room, "_isBoss", false);
            alive.Clear();
            Check(!room.IsCombatActive && !player.IsInRoomCombat, "A fighting room without members still counted.");
            alive.Add(member);

            Set(room, "_suspended", true);
            Check(!player.IsInRoomCombat, "A suspended room still counted as combat.");
            Set(room, "_suspended", false);
            Set(room, "_disposed", true);
            Check(!player.IsInRoomCombat, "A disposed room still counted as combat.");
            Set(room, "_disposed", false);
            room.enabled = false;
            Check(!player.IsInRoomCombat, "A disabled room still counted as combat.");
            room.enabled = true;
            // Restore flags if the Editor invoked OnDisable for this fixture.
            Set(room, "_disposed", false);
            alive.Add(member);

            Set(director, "_suspended", true);
            Check(!player.IsInRoomCombat, "A suspended director still counted as combat.");
            Set(director, "_suspended", false);
            Property(director, "IsReady", false);
            Check(!player.IsInRoomCombat, "An unprepared dungeon still counted as combat.");
            Property(director, "IsReady", true);
            Property(generator, "IsGenerating", true);
            Check(!player.IsInRoomCombat, "Dungeon regeneration still counted old combat.");
            Property(generator, "IsGenerating", false);
            root.SetActive(false);
            Check(!player.IsInRoomCombat, "An inactive generated root still counted as combat.");
            root.SetActive(true);
            Set(room, "_disposed", false);
            alive.Add(member);
            var replacement = new GameObject("Replacement dungeon");
            Property(generator, "CurrentRoot", replacement);
            Check(!player.IsInRoomCombat, "A stale generated root still counted as combat.");
            Property(generator, "CurrentRoot", root);
            Property(generator, "LastResult", new BaekhoDungeonResult { success = false, root = root });
            Check(!player.IsInRoomCombat, "Failed generation still counted old combat.");
            Property(generator, "LastResult", result);
            Set(director, "_preparationError", "fixture preparation failure");
            Check(!player.IsInRoomCombat, "Preparation failure still counted as combat.");
            Set(director, "_preparationError", null);

            var failed = new GameObject("Failed room").AddComponent<DungeonRoomEncounter>();
            failed.transform.SetParent(root.transform, false);
            Property(failed, "State", DungeonEncounterState.Failed);
            rooms.Add(failed);
            Check(!player.IsInRoomCombat, "A dungeon room failure did not stop combat reporting.");
            rooms.Remove(failed);
            Check(player.IsInRoomCombat, "Restoring the valid current dungeon did not restore combat reporting.");
            room.NotifyEnemyRemoved(member);
            Check(room.State == DungeonEncounterState.Cleared && !player.IsInRoomCombat,
                "Removing the final member did not end room combat immediately.");

            Property(room, "State", DungeonEncounterState.Fighting);
            alive.Add(member);
            Invoke(director, "OnDisable");
            Check(!player.IsInRoomCombat && !director.IsPlayerInCombat(player), "Director disable retained its player binding.");
            Invoke(director, "FindPlayer");
            Check(player.IsInRoomCombat, "Explicit player rebind did not restore current combat.");
            typeof(PlayerController).GetMethod("BindEncounterDirector", Hidden).Invoke(player, new object[] { null });
            Check(!player.IsInRoomCombat, "Removing the combat source did not clear the player query.");
            return $"Form SP encounter: {checks} checks passed (room states, normal/boss combat, owner binding, "
                + "clear, suspension, failure, disable and stale/generated roots).";
        }
        finally
        {
            SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static object Get(object target, string name) => target.GetType().GetField(name, Hidden).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
    private static void Property(object target, string name, object value) => target.GetType().GetProperty(name).SetValue(target, value);
    private static void Invoke(object target, string name) => target.GetType().GetMethod(name, Hidden).Invoke(target, null);
}

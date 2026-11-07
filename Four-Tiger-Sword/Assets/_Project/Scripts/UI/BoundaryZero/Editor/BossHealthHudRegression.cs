using System;
using System.Collections.Generic;
using System.Reflection;
using StudioBaekho;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Exercises boss-room entry visibility, damage and replacement-stage cleanup.</summary>
public static class BossHealthHudRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string ChapterBossPath = "Assets/_Game/RunProgression/Prefabs/ChapterBoss.prefab";
    private const string HealthArtRoot = "Assets/_Project/Sprites/UI/BoundaryZERO/EnemyHealth/";
    private static int _checks;

    [MenuItem("Tools/Studio Baekho/UI/Run Boss Health HUD Regression")]
    private static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run boss HUD fixtures in Edit Mode.");
        _checks = 0;
        using (var fixture = new Fixture())
        {
            Exercise(fixture);
            ExerciseNameBinding(fixture);
            ExercisePhaseRevival(fixture);
        }
        return $"Boss health HUD: {_checks} checks passed (waiting-room suppression, real ChapterBoss entry, smooth fill, "
            + "delayed/repeated-hit trail, damage, max HP, "
            + "shielded ConfiguredBoss, HP-zero down, revival refill, final death, ordinary/preparing/terminal stages, replacement, "
            + "disable/reactivation, names/fallbacks, removed actors and input pass-through).";
    }

    public static string ValidateInstalledScene()
    {
        var scene = SceneManager.GetActiveScene();
        BossHealthHud hud = null;
        DungeonMinimapPresenter minimap = null;
        int count = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var candidate in root.GetComponentsInChildren<BossHealthHud>(true))
            { hud = candidate; count++; }
            foreach (var candidate in root.GetComponentsInChildren<DungeonMinimapPresenter>(true)) minimap = candidate;
        }
        Require(count == 1 && hud != null, "Expected exactly one installed boss HUD.");
        var serialized = new SerializedObject(hud);
        var run = serialized.FindProperty("run").objectReferenceValue as RunManager;
        var visibility = serialized.FindProperty("visibility").objectReferenceValue as CanvasGroup;
        var fill = serialized.FindProperty("fill").objectReferenceValue as UnityEngine.UI.Image;
        var bossName = serialized.FindProperty("bossName").objectReferenceValue as TMPro.TMP_Text;
        Require(run != null && run.gameObject.scene == scene && visibility != null && fill != null,
            "The installed boss HUD lost its explicit run/visibility/fill bindings.");
        Require(bossName != null && bossName.transform.IsChildOf(visibility.transform),
            "The installed boss HUD lost its name binding under the visible bar.");
        Require(hud.gameObject.activeInHierarchy && visibility.transform != hud.transform,
            "Hiding the bar would disable its own presenter.");
        foreach (var graphic in visibility.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
            Require(!graphic.raycastTarget, "A boss health graphic intercepts gameplay input.");
        var overlay = hud.GetComponent<BoundaryZeroRunOverlay>();
        Require(overlay != null && !overlay.StageLabel.gameObject.activeSelf,
            "The old top-center stage label is still enabled.");
        Require(minimap != null && minimap.View != null && minimap.View.StageLabel != null
            && minimap.Run == run, "The minimap lacks the run-bound progress label.");
        var label = minimap.View.StageLabel;
        Require(label.transform.parent == minimap.View.transform.Find("Panel") && !label.raycastTarget,
            "Stage progress is not a fixed, input-transparent child of the minimap panel.");
        return "Installed boss HUD and minimap progress: explicit bindings, hidden legacy label, fixed label parent and input pass-through verified.";
    }

    public static string ValidateArtwork()
    {
        int firstCheck = _checks;
        var monster = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/HpBar.prefab");
        Check(monster != null, "The source monster health prefab is missing.");
        var monsterController = monster.GetComponentInChildren<EnemyHpBar>(true);
        Check(monsterController != null, "The source monster bar lost its health controller.");
        var monsterSettings = new SerializedObject(monsterController);
        var monsterFill = monsterSettings.FindProperty("_hpFill").objectReferenceValue as UnityEngine.UI.Image;
        var monsterTrail = monsterSettings.FindProperty("_hpTrail").objectReferenceValue as UnityEngine.UI.Image;
        var monsterBackground = NamedImage(monster, "HpBarBackground");
        var monsterFrame = NamedImage(monster, "HpBarFrame");
        Artwork(monsterBackground, "monster_background.png");
        Artwork(monsterTrail, "monster_trail.png");
        Artwork(monsterFill, "monster_fill.png");
        Artwork(monsterFrame, "monster_frame.png");
        Layers(monsterBackground, monsterTrail, monsterFill, monsterFrame);
        Check((monsterBackground.rectTransform.sizeDelta - new Vector2(2f, .4375f)).sqrMagnitude < .000001f,
            "The monster health artwork differs from the supplied frame's aspect ratio.");
        Check((monsterFill.rectTransform.anchoredPosition - new Vector2(.171875f, -.1875f)).sqrMagnitude < .000001f
            && (monsterFill.rectTransform.sizeDelta - new Vector2(1.703125f, .078125f)).sqrMagnitude < .000001f,
            "The monster fill does not align with the supplied frame's empty slot.");
        var monsterTrack = NamedImage(monster, "HpBarTrack");
        Check(!monsterTrack.gameObject.activeSelf, "The obsolete monster track still covers the new artwork.");
        FillLayout(monsterFill, monsterTrail);

        var overlay = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/BoundaryZeroRunOverlay.prefab");
        Check(overlay != null, "The authored boss overlay prefab is missing.");
        var hud = overlay.GetComponent<BossHealthHud>();
        Check(hud != null, "The boss overlay lost its health presenter.");
        var bar = overlay.transform.Find("BossHealthBar") as RectTransform;
        Check(bar != null, "The boss bar root is missing.");
        Check((bar.anchorMin - new Vector2(.5f, 1f)).sqrMagnitude < .001f
            && (bar.anchorMax - new Vector2(.5f, 1f)).sqrMagnitude < .001f
            && (bar.pivot - new Vector2(.5f, 1f)).sqrMagnitude < .001f,
            "The boss artwork is not anchored at the top center.");
        Check((bar.anchoredPosition - new Vector2(0f, -48f)).sqrMagnitude < .001f
            && (bar.sizeDelta - new Vector2(720f, 72f)).sqrMagnitude < .001f,
            "The boss artwork differs from the approved display size or offset.");
        var settings = new SerializedObject(hud);
        var fill = settings.FindProperty("fill").objectReferenceValue as UnityEngine.UI.Image;
        var trail = settings.FindProperty("trail").objectReferenceValue as UnityEngine.UI.Image;
        var frame = bar.Find("Frame")?.GetComponent<UnityEngine.UI.Image>();
        var background = bar.GetComponent<UnityEngine.UI.Image>();
        var bossName = settings.FindProperty("bossName").objectReferenceValue as TMPro.TMP_Text;
        Artwork(background, "boss_background.png");
        Artwork(trail, "boss_trail.png");
        Artwork(fill, "boss_fill.png");
        Artwork(frame, "boss_frame.png");
        Check(bossName != null && bossName.transform.parent == bar && !bossName.raycastTarget
            && bossName.transform.GetSiblingIndex() > frame.transform.GetSiblingIndex(),
            "The boss name is not bound above the frame or intercepts input.");
        Check((bossName.rectTransform.anchoredPosition - Vector2.zero).sqrMagnitude < .001f
            && (bossName.rectTransform.sizeDelta - new Vector2(720f, 30f)).sqrMagnitude < .001f
            && (bossName.rectTransform.anchorMin - new Vector2(0f, 1f)).sqrMagnitude < .001f
            && (bossName.rectTransform.anchorMax - new Vector2(0f, 1f)).sqrMagnitude < .001f
            && (bossName.rectTransform.pivot - new Vector2(0f, 1f)).sqrMagnitude < .001f,
            "The boss name differs from the supplied top-left 720 by 30 layout.");
        Near(bossName.fontSize, 24f, "The boss name differs from the supplied font size.");
        Check(bossName.color == (Color)new Color32(232, 238, 245, 255)
            && bossName.alignment == TMPro.TextAlignmentOptions.Center && !bossName.enableAutoSizing,
            "The boss name lost its fixed size, centered alignment or supplied #E8EEF5 color.");
        ValidateNameRendering(bossName);
        Check(fill.transform.parent == bar && trail.transform.parent == bar && frame.transform.parent == bar,
            "Boss artwork layers are not direct children of the fixed bar.");
        Check(trail.transform.GetSiblingIndex() < fill.transform.GetSiblingIndex()
            && fill.transform.GetSiblingIndex() < frame.transform.GetSiblingIndex(),
            "Boss damage trail, fill and frame draw in the wrong order.");
        var oldTrack = bar.Find("Track");
        Check(oldTrack != null && !oldTrack.gameObject.activeSelf, "The obsolete boss track was not disabled.");
        Check((fill.rectTransform.anchoredPosition - new Vector2(16f, -44f)).sqrMagnitude < .001f
            && (fill.rectTransform.sizeDelta - new Vector2(688f, 12f)).sqrMagnitude < .001f,
            "The boss fill does not align with the supplied frame's empty slot.");
        FillLayout(fill, trail);
        return $"Health artwork: {_checks - firstCheck} checks passed (provided monster/boss sprites, layer order, "
            + "disabled legacy tracks, aligned horizontal fill/trail, input pass-through and boss placement).";
    }

    private static void ValidateNameRendering(TMPro.TMP_Text source)
    {
        var preview = EditorSceneManager.NewPreviewScene();
        GameObject canvas = null;
        try
        {
            canvas = new GameObject("BossNameRendering", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvas, preview);
            var label = Object.Instantiate(source, canvas.transform);
            foreach (string name in new[] { "Test_Boss", "경계의 수호자" })
            {
                label.text = name;
                label.ForceMeshUpdate(true, true);
                bool visible = false;
                for (int i = 0; i < label.textInfo.characterCount; i++)
                    visible |= label.textInfo.characterInfo[i].isVisible;
                Check(visible && !label.isTextTruncated,
                    "The authored boss name box suppresses its font's glyphs: " + name);
            }
        }
        finally
        {
            if (canvas != null) Object.DestroyImmediate(canvas);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static UnityEngine.UI.Image NamedImage(GameObject root, string name)
    {
        foreach (var image in root.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            if (image.name == name) return image;
        throw new InvalidOperationException("Boss HUD: missing authored health layer " + name);
    }

    private static void Artwork(UnityEngine.UI.Image image, string filename)
    {
        Check(image != null && image.sprite != null && AssetDatabase.GetAssetPath(image.sprite) == HealthArtRoot + filename,
            "A health layer does not use the supplied artwork: " + filename);
        Check(!image.raycastTarget && image.GetComponent<CanvasRenderer>() != null,
            "A health artwork layer blocks input or lacks its renderer: " + filename);
    }

    private static void Layers(UnityEngine.UI.Image background, UnityEngine.UI.Image trail,
        UnityEngine.UI.Image fill, UnityEngine.UI.Image frame)
    {
        Check(background.transform == trail.transform.parent && trail.transform.parent == fill.transform.parent
            && fill.transform.parent == frame.transform.parent, "Monster health layers do not share the same layout.");
        Check(trail.transform.GetSiblingIndex() < fill.transform.GetSiblingIndex()
            && fill.transform.GetSiblingIndex() < frame.transform.GetSiblingIndex(),
            "Monster health background, trail, fill and frame draw in the wrong order.");
    }

    private static void FillLayout(UnityEngine.UI.Image fill, UnityEngine.UI.Image trail)
    {
        foreach (var image in new[] { fill, trail })
            Check(image.type == UnityEngine.UI.Image.Type.Filled
                && image.fillMethod == UnityEngine.UI.Image.FillMethod.Horizontal
                && image.fillOrigin == (int)UnityEngine.UI.Image.OriginHorizontal.Left,
                "A health fill/trail does not drain horizontally from the right.");
        Check((fill.rectTransform.anchoredPosition - trail.rectTransform.anchoredPosition).sqrMagnitude < .001f
            && (fill.rectTransform.sizeDelta - trail.rectTransform.sizeDelta).sqrMagnitude < .001f,
            "Health fill and delayed trail occupy different slots.");
    }

    private static void Exercise(Fixture f)
    {
        var ordinary = f.Result(false, false, out _, out _);
        f.Install(ordinary);
        f.Hud.Configure(f.Run, f.Visibility, f.Fill);
        Check(!f.Hud.IsVisible && f.Fill.fillAmount == 0f, "An ordinary stage displayed a boss bar.");

        var first = f.Result(true, false, out var room, out var enemy);
        var stats = enemy.GetComponent<EnemyStat>();
        Check(enemy.GetType() == typeof(Enemy), "The production ChapterBoss fixture no longer exercises plain Enemy.");
        Check(!enemy.enabled && enemy.EnemyStat == null && !room.Members[0].IsAwake,
            "The fixture must represent an initialized boss before Enemy.Start and room entry.");
        f.Install(first);
        f.Tick();
        Check(!f.Hud.IsVisible && f.Visibility.alpha == 0f, "A waiting boss appeared before boss-room entry.");
        f.Tick();
        f.Tick();
        Check(!f.Hud.IsVisible, "Repeated waiting-room updates prematurely revealed the boss bar.");
        Check(!f.Visibility.blocksRaycasts && !f.Visibility.interactable && !f.Fill.raycastTarget,
            "The health bar intercepts gameplay input.");

        Property(room, "State", DungeonEncounterState.Fighting);
        f.Tick();
        Check(!f.Hud.IsVisible, "Combat state without confirmed room entry revealed the boss bar.");
        Fight(room, enemy);
        f.Tick();
        Check(f.Hud.IsVisible, "Entering the boss room did not reveal its previously waiting boss.");
        Near(f.Hud.FillAmount, 1f, "The boss did not display full initial HP on room entry.");
        Near(f.Hud.TrailAmount, 1f, "The boss trail did not start at its actual initial HP.");
        float max = stats.MaxHp;
        var hit = enemy.TakeDamage(max * .25f, poiseDamage: 0f);
        f.Tick();
        Check(hit.Applied && !hit.Killed, "The plain Enemy fixture did not receive a nonlethal hit.");
        Near(f.Hud.FillAmount, 1f, "Damage skipped the smooth health-fill animation.");
        f.Animate(.1f);
        Check(f.Hud.FillAmount > .75f && f.Hud.FillAmount < 1f,
            "The health fill did not move smoothly toward the damaged HP.");
        Near(f.Hud.TrailAmount, 1f, "The damage trail moved before its hold elapsed.");
        f.Animate(.2f);
        Near(f.Hud.FillAmount, .75f, "Actual Enemy.TakeDamage did not update the health fill.");
        Near(f.Hud.TrailAmount, 1f, "The damage trail did not hold for the configured delay.");
        enemy.TakeDamage(max * .1f, poiseDamage: 0f);
        f.Tick();
        f.Animate(.34f);
        Near(f.Hud.TrailAmount, 1f, "Repeated damage did not restart the trail hold.");
        f.Animate(.11f);
        Check(f.Hud.TrailAmount < 1f && f.Hud.TrailAmount > f.Hud.FillAmount,
            "The delayed trail did not catch up gradually after its hold.");
        f.Settle();
        Near(f.Hud.FillAmount, .65f, "Repeated damage retained a stale health fill.");
        Near(f.Hud.TrailAmount, .65f, "The delayed trail did not settle at the damaged HP.");
        stats.AddModifier(EnemyStatType.HP, max, 0f);
        f.Tick();
        f.Settle();
        Near(f.Hud.FillAmount, .325f, "The health fill ignored a changed maximum HP.");

        f.State(RunState.Preparing);
        Check(!f.Hud.IsVisible && f.Visibility.alpha == 0f, "Preparation did not promptly clear the previous boss.");
        f.State(RunState.Playing);
        Check(f.Hud.IsVisible, "Returning to a ready stage did not rebind its boss.");
        f.Hud.enabled = false;
        Invoke(f.Hud, "OnDisable");
        Check(!f.Hud.IsVisible && f.Fill.fillAmount == 0f, "Disabling the HUD retained its boss display.");
        f.Hud.enabled = true;
        Invoke(f.Hud, "OnEnable");
        Check(f.Hud.IsVisible, "Reactivating the HUD failed to restore a live boss.");

        var replacement = f.Result(true, true, out var nextRoom, out var nextEnemy);
        f.Install(replacement);
        f.Tick();
        Check(!f.Hud.IsVisible, "Replacing the stage revealed its boss before room entry.");
        Fight(nextRoom, nextEnemy);
        f.Tick();
        Check(f.Hud.IsVisible, "Entering a replacement boss room did not reveal its health bar.");
        Near(f.Hud.FillAmount, 1f, "Replacing the stage retained the previous boss HP.");
        enemy.TakeDamage(stats.CurrentHp, poiseDamage: 0f);
        Check(f.Hud.IsVisible && Mathf.Approximately(f.Hud.FillAmount, 1f),
            "A previous generation's death callback changed the current health bar.");

        var boss = nextEnemy as ConfiguredBoss;
        Check(boss != null, "The replacement fixture must exercise a real Boss subclass.");
        boss.GetComponent<BossShield>().SetAbsorption(30f);
        var absorbed = boss.TakeDamage(20f, poiseDamage: 0f);
        f.Tick();
        Near(absorbed.HealthDamage, 0f, "The shield fixture did not absorb a full hit.");
        Near(f.Hud.FillAmount, 1f, "Shield-only damage incorrectly reduced the HP bar.");
        Near(f.Hud.TrailAmount, 1f, "Shield-only damage incorrectly reduced the HP trail.");
        var overflow = boss.TakeDamage(30f, poiseDamage: 0f);
        f.Tick();
        Near(overflow.HealthDamage, 20f, "The shield fixture did not pass its overflow to HP.");
        f.Settle();
        Near(f.Hud.FillAmount, .9f, "The HP bar did not display shield overflow correctly.");
        var lethal = boss.TakeDamage(1000f, poiseDamage: 0f);
        Check(lethal.Killed && !f.Hud.IsVisible && f.Fill.fillAmount == 0f,
            "Confirmed boss death did not hide the health bar immediately.");

        var third = f.Result(true, false, out var thirdRoom, out var thirdEnemy);
        f.Install(third);
        f.Tick();
        Check(!f.Hud.IsVisible, "A new stage after boss death revealed its waiting boss.");
        Fight(thirdRoom, thirdEnemy);
        f.Tick();
        Check(f.Hud.IsVisible, "Entering a new boss room after boss death did not restore the health bar.");
        foreach (var state in new[] { RunState.Error, RunState.Won, RunState.Lost })
        {
            f.State(state);
            Check(!f.Hud.IsVisible, "A terminal/error run state retained its boss bar: " + state);
            f.State(RunState.Playing);
            Check(f.Hud.IsVisible, "A ready run failed to rebind after state cleanup: " + state);
        }
        Property(f.Stage, "IsReady", false);
        f.Tick();
        Check(!f.Hud.IsVisible, "A suspended stage retained its boss bar.");
        Property(f.Stage, "IsReady", true);
        f.Tick();
        Check(f.Hud.IsVisible, "A ready stage failed to recover after suspension.");
        thirdRoom.SuspendEncounter();
        f.Tick();
        Check(!f.Hud.IsVisible, "A suspended boss encounter retained its health bar.");
        Set(thirdRoom, "_suspended", false);
        Fight(thirdRoom, thirdEnemy);
        f.Tick();
        Check(f.Hud.IsVisible, "An active boss encounter failed to recover after suspension.");
        thirdRoom.Members[0].Release();
        Object.DestroyImmediate(thirdEnemy.gameObject);
        f.Tick();
        Check(!f.Hud.IsVisible && f.Fill.fillAmount == 0f, "A removed boss retained stale health.");
        f.Install(ordinary);
        f.Tick();
        Check(!f.Hud.IsVisible, "Returning to an ordinary stage retained the last boss bar.");
    }

    private static void Fight(DungeonRoomEncounter room, Enemy enemy)
    {
        Property(room, "State", DungeonEncounterState.Fighting);
        Property(room, "WasEntered", true);
        enemy.EnemyStat = enemy.GetComponent<EnemyStat>();
        enemy.enabled = true;
        enemy.GetComponent<PoiseHandler>().ResetPoise();
    }

    private static void ExercisePhaseRevival(Fixture f)
    {
        var result = f.Result(true, true, out var room, out var enemy);
        var boss = (ConfiguredBoss)enemy;
        Fight(room, enemy);
        f.ConfigurePhases(boss);
        f.Install(result);
        f.Tick();
        Near(f.Hud.FillAmount, 1f, "The phased boss did not reveal its first HP bar.");
        Check(f.Name.text == "기상 보스", "The phased boss did not display its authored name.");

        var hit = boss.TakeDamage(1000f, poiseDamage: 0f);
        Check(hit.Applied && !hit.Killed && boss.IsTransitioning && !boss.IsDead,
            "Depleting the first phase incorrectly confirmed final death.");
        Check(f.Hud.IsVisible && !room.Members[0].IsDead && !room.Members[0].HasDied,
            "The HP-zero boss lost its HUD binding or encounter membership.");
        Near(f.Hud.FillAmount, 0f, "The depletion event did not empty the fill immediately.");
        Near(f.Hud.TrailAmount, 0f, "The previous phase's delayed damage trail survived depletion.");
        Check(f.Name.text == "기상 보스", "Depletion cleared the undefeated boss's name.");
        Invoke(room, "PruneRemoved");
        Check(room.State == DungeonEncounterState.Fighting && room.AliveCount == 1 && !room.BossDefeated,
            "The encounter removed or defeated a boss during its down state.");
        f.Tick();
        Check(f.Hud.IsVisible && ReferenceEquals(Field(f.Hud, "_enemy"), boss),
            "Refreshing the HP-zero state released the current boss.");

        TickTransition(boss, .5f);
        f.Tick();
        Near(f.Hud.FillAmount, 0f, "The fill rose during the down/hold interval.");
        TickTransition(boss, .25f);
        f.Tick();
        Near(boss.ReviveProgress, .25f, "The fixture did not reach a quarter of the rise interval.");
        Near(f.Hud.FillAmount, .25f, "The fill did not follow the revival progress directly.");
        Near(f.Hud.TrailAmount, .25f, "The revival trail lagged behind the refill.");
        Check(f.Name.text == "기상 보스", "The name changed during the rise animation.");
        f.Animate(.01f);
        Near(f.Hud.FillAmount, .25f, "Ordinary damage interpolation changed the revival display.");
        // Decimal input durations must also complete exactly at the cached transition boundary.
        TickTransition(boss, .75f);
        f.Tick();
        Check(!boss.IsTransitioning && boss.PhaseIndex == 1,
            $"The phase fixture did not finish revival (transitioning={boss.IsTransitioning}, phase={boss.PhaseIndex}).");
        Check(f.Hud.IsVisible,
            $"The revived phase lost its bar (HP={boss.EnemyStat.CurrentHp}/{boss.EnemyStat.MaxHp}, "
            + $"room={room.State}, alive={room.AliveCount}, dead={boss.IsDead}, defeated={room.BossDefeated}).");
        Near(f.Hud.FillAmount, 1f, "The next phase did not finish refilling its HP bar.");
        Near(boss.EnemyStat.MaxHp, 120f, "The bar retained the previous phase's maximum HP.");
        Check(f.Name.text == "기상 보스", "Revival lost the same boss's name.");

        var final = boss.TakeDamage(1000f, poiseDamage: 0f);
        Check(final.Killed && room.BossDefeated && !f.Hud.IsVisible,
            "The final phase did not confirm defeat and hide the HUD.");
        Check(f.Name.text == string.Empty, "Final defeat retained the previous boss's name.");
    }

    private static void ExerciseNameBinding(Fixture f)
    {
        var result = f.Result(true, true, out var room, out var enemy);
        enemy.name = "Fallback Actor(Clone)";
        var data = (BossData)enemy.GetComponent<EnemyStat>().Data;
        data.bossName = " \t";
        Fight(room, enemy);
        f.Install(result);
        f.Tick();
        Check(f.Hud.IsVisible && f.Name.text == "Fallback Actor",
            "A blank data name did not fall back to the actor name without its Clone suffix.");
        data.bossName = "  명명된 보스  ";
        f.Tick();
        Check(f.Name.text == "명명된 보스", "The authored boss name did not take priority over the actor name.");
        f.Hud.ConfigureName(null);
        f.Tick();
        Check(f.Hud.IsVisible && f.Name.text == string.Empty,
            "Removing the optional name binding hid the health bar or left stale text.");
        f.Hud.ConfigureName(f.Name);
        Check(f.Name.text == "명명된 보스", "Rebinding the optional name did not show the current boss.");
        f.Hud.Bind(null);
        Check(f.Name.text == string.Empty && !f.Hud.IsVisible, "Unbinding the run retained a boss name.");
        f.Hud.Bind(f.Run);
        Check(f.Hud.IsVisible && f.Name.text == "명명된 보스", "Rebinding the run did not restore the current name.");
    }

    private static void TickTransition(Boss boss, float deltaTime)
        => typeof(Boss).GetMethod("TickPhaseTransition", Hidden).Invoke(boss, new object[] { deltaTime });

    private static void Property(object owner, string name, object value)
        => owner.GetType().GetProperty(name).SetValue(owner, value);
    private static object Field(object owner, string name) => owner.GetType().GetField(name, Hidden).GetValue(owner);
    private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Hidden).SetValue(owner, value);
    private static object Invoke(object owner, string method) => owner.GetType().GetMethod(method, Hidden).Invoke(owner, null);
    private static void Near(float actual, float expected, string message)
        => Check(Mathf.Abs(actual - expected) <= .001f, message + $" Actual: {actual}; expected: {expected}.");
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Boss HUD: " + message); }
    private static void Check(bool condition, string message) { Require(condition, message); _checks++; }

    private sealed class Fixture : IDisposable
    {
        private readonly Scene _scene = EditorSceneManager.NewPreviewScene();
        private readonly List<Object> _assets = new List<Object>();
        private readonly List<DungeonEncounterMember> _members = new List<DungeonEncounterMember>();
        private readonly int _frameRate = Application.targetFrameRate;
        private readonly bool _cursorVisible = Cursor.visible;
        private readonly CursorLockMode _cursorLock = Cursor.lockState;
        public readonly RunManager Run;
        public readonly StageManager Stage;
        public readonly BaekhoDungeonGenerator Generator;
        public readonly BossHealthHud Hud;
        public readonly CanvasGroup Visibility;
        public readonly UnityEngine.UI.Image Fill;
        public readonly UnityEngine.UI.Image Trail;
        public readonly TMPro.TMP_Text Name;

        public Fixture()
        {
            var host = Make("Boss HUD run fixture");
            Run = host.AddComponent<RunManager>();
            Stage = host.AddComponent<StageManager>();
            Generator = Make("Boss HUD generator fixture").AddComponent<BaekhoDungeonGenerator>();
            Stage.Configure(Generator, null);
            Run.stageManager = Stage;
            Property(Run, "State", RunState.Playing);
            var canvas = Make("Boss HUD canvas fixture", typeof(RectTransform), typeof(Canvas));
            Hud = canvas.AddComponent<BossHealthHud>();
            var bar = Make("Boss health", typeof(RectTransform), typeof(CanvasGroup));
            bar.transform.SetParent(canvas.transform, false);
            Visibility = bar.GetComponent<CanvasGroup>();
            Fill = Make("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image))
                .GetComponent<UnityEngine.UI.Image>();
            Fill.transform.SetParent(bar.transform, false);
            Fill.raycastTarget = false;
            Fill.type = UnityEngine.UI.Image.Type.Filled;
            Fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            Trail = Make("Trail", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image))
                .GetComponent<UnityEngine.UI.Image>();
            Trail.transform.SetParent(bar.transform, false);
            Trail.raycastTarget = false;
            Trail.type = UnityEngine.UI.Image.Type.Filled;
            Trail.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            Hud.ConfigureVisuals(Trail, 8f, .35f, .6f);
            Name = Make("Name", typeof(RectTransform), typeof(CanvasRenderer), typeof(TMPro.TextMeshProUGUI))
                .GetComponent<TMPro.TMP_Text>();
            Name.transform.SetParent(bar.transform, false);
            Name.raycastTarget = false;
            Hud.ConfigureName(Name);
        }

        public BaekhoDungeonResult Result(bool hasBoss, bool configured, out DungeonRoomEncounter room, out Enemy enemy)
        {
            var root = Make("Boss HUD generated stage");
            var result = new BaekhoDungeonResult { root = root, success = true, plan = new BaekhoDungeonPlan() };
            room = null; enemy = null;
            if (!hasBoss) return result;
            var module = Make("Boss HUD room").AddComponent<BaekhoMapModule>();
            module.transform.SetParent(root.transform, false);
            room = module.gameObject.AddComponent<DungeonRoomEncounter>();
            result.bossRoom = module;
            result.rooms.Add(module);
            GameObject actor;
            if (configured)
            {
                actor = Make("Configured boss HUD actor");
                actor.SetActive(false);
                actor.AddComponent<NavMeshAgent>().enabled = false;
                enemy = actor.AddComponent<ConfiguredBoss>();
                var data = ScriptableObject.CreateInstance<BossData>();
                _assets.Add(data);
                data.maxHp = 200;
                Set(enemy.GetComponent<EnemyStat>(), "baseData", data);
            }
            else
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChapterBossPath);
                Check(prefab != null, "The production ChapterBoss prefab is missing.");
                actor = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _scene);
                actor.SetActive(false);
                enemy = actor.GetComponent<Enemy>();
                actor.GetComponent<NavMeshAgent>().enabled = false;
            }
            actor.transform.SetParent(room.transform, false);
            var stats = actor.GetComponent<EnemyStat>();
            Invoke(stats, "InitializeFromData");
            Check(stats.MaxHp > 0f, "The boss fixture has no initialized maximum HP.");
            enemy.EnemyStat = null;
            var member = actor.AddComponent<DungeonEncounterMember>();
            member.Initialize(room);
            _members.Add(member);
            ((List<DungeonEncounterMember>)Field(room, "_members")).Add(member);
            ((HashSet<DungeonEncounterMember>)Field(room, "_alive")).Add(member);
            Set(room, "_isBoss", true);
            Set(room, "_bossMember", member);
            Property(room, "State", DungeonEncounterState.Waiting);
            actor.SetActive(true);
            return result;
        }

        public void Install(BaekhoDungeonResult result)
        {
            Property(Stage, "Result", result);
            Property(Stage, "IsReady", true);
            Property(Generator, "CurrentRoot", result.root);
        }

        public void ConfigurePhases(ConfiguredBoss boss)
        {
            var data = (BossData)boss.GetComponent<EnemyStat>().Data;
            data.phaseDownSeconds = .2f;
            data.phaseHoldSeconds = .3f;
            data.phaseReviveSeconds = 1f;
            data.bossName = "기상 보스";
            var first = ScriptableObject.CreateInstance<BossPhaseData>();
            var second = ScriptableObject.CreateInstance<BossPhaseData>();
            _assets.Add(first); _assets.Add(second);
            first.phaseMaxHp = 80f;
            second.phaseMaxHp = 120f;
            typeof(Boss).GetField("PhaseDatas", Hidden).SetValue(boss,
                new List<BossPhaseData> { first, second });
            // The presenter fixture uses the real damage/phase contract without starting AI navigation.
            typeof(Boss).GetMethod("CreateBrain", Hidden).Invoke(boss, null);
        }
        // Editor deltaTime is unrelated to this fixture; advance the animation explicitly below.
        public void Tick() => Invoke(Hud, "Refresh");
        public void Animate(float deltaTime)
            => typeof(BossHealthHud).GetMethod("UpdateFill", Hidden).Invoke(Hud, new object[] { deltaTime });
        public void Settle() => Animate(3f);
        public void State(RunState state)
        {
            Property(Run, "State", state);
            ((Action<RunState>)Field(Run, "StateChanged"))?.Invoke(state);
        }
        private GameObject Make(string name, params Type[] components)
        {
            var value = new GameObject(name, components);
            SceneManager.MoveGameObjectToScene(value, _scene);
            return value;
        }
        public void Dispose()
        {
            foreach (var member in _members) if (member != null) member.Release();
            EditorSceneManager.ClosePreviewScene(_scene);
            foreach (var asset in _assets) if (asset != null) Object.DestroyImmediate(asset);
            Application.targetFrameRate = _frameRate;
            Cursor.visible = _cursorVisible;
            Cursor.lockState = _cursorLock;
        }
    }
}

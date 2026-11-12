// Invoke Start in an already running Game Scene via Unity Pipeline run_script.
// Poll RunProgression.PlayModeRegression. No assets or scene files are saved.
// StartDeathAndRestart and StartReturnToTitle are optional, separate destructive-in-Play-Mode checks.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using EasternLab.GateAddon;
using StudioBaekho;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class RunProgressionPlayModeRegression
{
    public const string ResultKey = "RunProgression.PlayModeRegression";
    public const string DeathRestartKey = "RunProgression.DeathRestartRegression";
    public const string TitleKey = "RunProgression.TitleRegression";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string NormalSettings = "Assets/_Game/RunProgression/Settings/NormalStage.asset";
    private const string BossSettings = "Assets/_Game/RunProgression/Settings/BossStage.asset";
    private static bool running;
    private static int checks;
    private static int previousGeneration = -1;

    public static string Start()
    {
        Begin(ResultKey);
        _ = Run();
        return "Run progression Play Mode regression started; poll " + ResultKey + ".";
    }

    private static void Begin(string key)
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode in Game Scene first.");
        if (running) throw new InvalidOperationException("A run progression regression is already running.");
        running = true;
        checks = 0;
        previousGeneration = -1;
        SessionState.SetString(key, "RUNNING");
    }

    private static async Task Run()
    {
        RunManager run = null;
        RunDefinitionSO originalDefinition = null;
        ItemCatalog originalCatalog = null;
        string[] originalOwned = Array.Empty<string>();
        PlayerController player = null;
        IDisposable accountScope = null;
        PlayerUpgradeService previousRunService = null;
        bool modifierAdded = false, catalogReplaced = false;
        var temporary = new List<Object>();
        var pristine = new Dictionary<Object, string>();
        try
        {
            run = Object.FindFirstObjectByType<RunManager>();
            Check(run != null, "RunManager is not installed.");
            await Ready(run);
            Check(run.CurrentChapterIndex == 0 && run.CurrentStageIndex == 0,
                "Start this regression in a fresh run, before using the first exit.");
            previousRunService = GetField<PlayerUpgradeService>(run, "_upgradeService");
            var originalAccount = previousRunService ?? PlayerUpgradeService.Instance;
            Check(originalAccount.IsReady && originalAccount.Settings != null, "The live progression account is unavailable.");
            var profile = GetField<PlayerUpgradeProfile>(originalAccount, "_profile");
            var isolatedAccount = new PlayerUpgradeService(originalAccount.Settings, new MemoryUpgradeStore(profile),
                originalAccount.EngravingSettings);
            Check(isolatedAccount.IsReady, "The isolated progression account failed to load.");
            // All stage rewards, final gold conversion and supply-depot settlement use this copy.
            accountScope = PlayerUpgradeService.OverrideForTesting(isolatedAccount);
            SetField(run, "_upgradeService", isolatedAccount);
            player = run.player;
            Check(player != null && player.Controller != null && player.StatManager != null,
                "Run has no initialized player.");
            int frame = Time.frameCount;
            await Frames(2);
            Check(Time.frameCount > frame, "The real player loop did not advance.");
            originalDefinition = run.definition;
            Remember(pristine, originalDefinition);
            foreach (var chapter in originalDefinition.chapters)
                foreach (var stage in chapter.stages)
                {
                    Remember(pristine, stage.dungeonSettings);
                    Remember(pristine, stage.enemyCatalog);
                }

            // A fixed, disposable two-chapter run covers ordinary, chapter-ending, and final exits
            // even when the installed campaign only contains a single authored boss stage.
            var normalSettings = AssetDatabase.LoadAssetAtPath<BaekhoDungeonSettings>(NormalSettings);
            var bossSettings = AssetDatabase.LoadAssetAtPath<BaekhoDungeonSettings>(BossSettings);
            var installedBoss = originalDefinition.chapters.SelectMany(chapter => chapter.stages)
                .FirstOrDefault(stage => stage.bossPrefab != null);
            Check(normalSettings != null && bossSettings != null && installedBoss != null && installedBoss.enemyCatalog != null,
                "The authored normal/boss settings, boss prefab, or enemy catalog are missing.");
            Remember(pristine, normalSettings);
            Remember(pristine, bossSettings);
            var normal = new StageDefinition { displayName = "QA Normal", dungeonSettings = normalSettings,
                enemyCatalog = installedBoss.enemyCatalog, mapCountVariation = 0 };
            var bossFixture = new StageDefinition { displayName = "QA Boss", dungeonSettings = bossSettings,
                enemyCatalog = installedBoss.enemyCatalog, bossPrefab = installedBoss.bossPrefab, mapCountVariation = 0 };
            var chapters = new[]
            {
                new ChapterDefinition { displayName = "QA Chapter 1", stages = new[] { normal, bossFixture } },
                new ChapterDefinition { displayName = "QA Chapter 2", stages = new[] { normal, bossFixture } }
            };
            var definition = CloneDefinition(chapters, temporary);
            Check(definition.Validate(out string definitionError), definitionError);
            run.definition = definition;

            var installedRoot = run.stageManager.Result.root;
            typeof(RunManager).GetMethod("OnStageFailure", Hidden).Invoke(run, new object[] { "Preparing the regression fixture." });
            Check(run.State == RunState.Error, "The live run did not suspend for fixture preparation.");
            run.RetryPendingStage();
            await Ready(run);
            Check(run.CurrentChapterIndex == 0 && run.CurrentStageIndex == 0 && run.stageManager.Result.bossRoom == null
                && run.stageManager.Result.root != installedRoot, "Retry did not prepare the fixture's first normal stage.");
            CheckRoomCountSampling(definition.chapters[0].stages[0]);
            CheckCurrentStage(run);

            var items = player.ItemRuntime;
            Check(items != null, "PlayerItemRuntime is missing.");
            originalCatalog = items.Catalog;
            originalOwned = items.OwnedItems.Select(item => item.itemId).ToArray();
            if (originalCatalog != null) Remember(pristine, originalCatalog);
            var testCatalog = originalCatalog != null ? Object.Instantiate(originalCatalog) : ScriptableObject.CreateInstance<ItemCatalog>();
            testCatalog.name = "Run regression item catalog (runtime only)";
            testCatalog.hideFlags = HideFlags.DontSave;
            if (string.IsNullOrEmpty(testCatalog.projectId)) testCatalog.projectId = "RunRegression";
            temporary.Add(testCatalog);
            var testItem = ScriptableObject.CreateInstance<ItemDefinition>();
            temporary.Add(testItem);
            testItem.hideFlags = HideFlags.DontSave;
            testItem.projectId = testCatalog.projectId;
            testItem.itemId = "__run_transition_regression__";
            testItem.candidateKey = testItem.itemId;
            testItem.displayName = "Transition retention probe";
            testItem.effects = new[] { new ItemEffect { stat = ItemStat.NormalDamage, amount = 0.1f } };
            testCatalog.items = (testCatalog.items ?? Array.Empty<ItemDefinition>()).Concat(new[] { testItem }).ToArray();
            items.Catalog = testCatalog;
            catalogReplaced = true;
            foreach (string id in originalOwned) Check(items.TryAddItem(id), "Could not restore existing ownership into runtime catalog: " + id);
            Check(items.TryAddItem(testItem.itemId), "Could not grant the temporary regression item.");
            player.StatManager.AddModifier(StatType.ST_ATK, 3f, 0f);
            modifierAdded = true;
            player.StatManager.ConsumeHealth(Mathf.Min(17f, player.StatManager.CurrentHp * 0.25f));
            player.StatManager.AddUltimateGauge(23f);
            SetField(player, "_dashCooldownTimer", 7f);
            SetField(player, "_formChangeCooldownTimer", 9f);
            int formIndex = 0;
            foreach (var form in Forms(player))
            {
                SetField(form, "_skillCooldownTimer", 11f + formIndex);
                SetField(form, "_ultimateCooldownTimer", 17f + formIndex);
                formIndex++;
            }
            Check(formIndex > 0, "Player has no registered forms to preserve.");

            int completed = 0, failed = 0;
            Action<BaekhoDungeonResult> onCompleted = _ => completed++;
            Action<BaekhoDungeonResult> onFailed = _ => failed++;
            run.stageManager.Generator.Completed += onCompleted;
            run.stageManager.Generator.Failed += onFailed;
            try
            {
                bool testedFailure = false, testedAutomaticExit = false;
                int chaptersCrossed = 0, bossesDefeated = 0, transitions = 0;
                while (run.State == RunState.Playing)
                {
                    int chapter = run.CurrentChapterIndex;
                    int stage = run.CurrentStageIndex;
                    CheckCurrentStage(run);
                    bool boss = stage == definition.chapters[chapter].stages.Length - 1;
                    await CompleteObjective(run, boss);
                    if (boss) bossesDefeated++;
                    bool hasNext = definition.TryGetNext(chapter, stage, out int nextChapter, out int nextStage);
                    if (!hasNext)
                    {
                        await UseFinalExit(run);
                        break;
                    }
                    bool failThis = !testedFailure;
                    await Transition(run, nextChapter, nextStage, failThis);
                    testedFailure |= failThis;
                    testedAutomaticExit = true;
                    transitions++;
                    if (nextChapter != chapter) chaptersCrossed++;
                }
                Check(run.State == RunState.Won, "The last exit did not produce run victory.");
                Check(run.CurrentChapterIndex == definition.chapters.Length - 1
                    && run.CurrentStageIndex == definition.chapters.Last().stages.Length - 1,
                    "Victory committed an invalid progress index.");
                Check(transitions == definition.TotalStages - 1 && completed == transitions,
                    "An exit or retry generated an extra stage.");
                Check(failed == 1 && testedFailure, "The actual generator failure path was not exercised exactly once.");
                Check(testedAutomaticExit && chaptersCrossed == definition.chapters.Length - 1,
                    "Automatic exit or chapter progression was not exercised.");
                Check(bossesDefeated == definition.chapters.Length, "A chapter boss was skipped.");
                CheckFrozen(run);
                Check(Cursor.visible && Cursor.lockState == CursorLockMode.None, "Victory did not release the cursor.");
                run.RetryPendingStage();
                await Frames(3);
                Check(run.State == RunState.Won && completed == transitions, "Error retry restarted a completed run.");
                SessionState.SetString(ResultKey, "PASS: " + checks + " checks; " + definition.TotalStages
                    + " actual stages; optional rooms, arrival-opened portals, directional aperture traversal, fixed bosses, physical gate locks, real lethal damage, chapter advance, failure/retry, duplicate/stale exits, retained player/resources/items/all-form timers, old roots/NavMesh/portal/transient cleanup, final victory.");
            }
            finally
            {
                if (run != null && run.stageManager != null && run.stageManager.Generator != null)
                {
                    run.stageManager.Generator.Completed -= onCompleted;
                    run.stageManager.Generator.Failed -= onFailed;
                }
            }
        }
        catch (Exception exception)
        {
            SessionState.SetString(ResultKey, "FAIL: " + exception);
            if (run != null && originalDefinition != null && run.definition != originalDefinition && run.State == RunState.Preparing)
            {
                // Stop nested generation before finally destroys the runtime definitions and room templates.
                typeof(RunManager).GetMethod("ShowError", Hidden).Invoke(run, new object[] { "Regression aborted during stage preparation." });
                run.stageManager.Cancel();
            }
        }
        finally
        {
            try
            {
                if (run != null && originalDefinition != null) run.definition = originalDefinition;
                if (player != null && player.ItemRuntime != null)
                {
                    if (modifierAdded) player.StatManager.RemoveModifier(StatType.ST_ATK, 3f, 0f);
                    if (catalogReplaced)
                    {
                        player.ItemRuntime.Catalog = originalCatalog;
                        foreach (string id in originalOwned) player.ItemRuntime.TryAddItem(id);
                    }
                }
                foreach (var asset in pristine)
                    if (asset.Key != null && EditorJsonUtility.ToJson(asset.Key) != asset.Value)
                        throw new Exception("Regression changed a source asset: " + asset.Key.name);
            }
            catch (Exception exception) { SessionState.SetString(ResultKey, "FAIL during restoration: " + exception); }
            finally
            {
                if (run != null && accountScope != null) SetField(run, "_upgradeService", previousRunService);
                accountScope?.Dispose();
            }
            foreach (Object obj in temporary) if (obj != null) Object.Destroy(obj);
            running = false;
        }
    }

    private static RunDefinitionSO CloneDefinition(ChapterDefinition[] chapters, List<Object> temporary)
    {
        var copy = ScriptableObject.CreateInstance<RunDefinitionSO>();
        copy.hideFlags = HideFlags.DontSave;
        temporary.Add(copy);
        var templates = new GameObject("RunRegressionTemplates");
        templates.hideFlags = HideFlags.DontSave;
        templates.SetActive(false);
        temporary.Add(templates);
        copy.chapters = chapters.Select(chapter => new ChapterDefinition
        {
            displayName = chapter.displayName,
            stages = chapter.stages.Select(stage =>
            {
                var settings = Object.Instantiate(stage.dungeonSettings);
                settings.hideFlags = HideFlags.DontSave;
                temporary.Add(settings);
                if (settings.bossRoom != null)
                {
                    var template = Object.Instantiate(settings.bossRoom.gameObject, templates.transform, false);
                    template.SetActive(true); // Parent remains inactive; generated clones remain active.
                    var module = template.GetComponent<BaekhoMapModule>();
                    module.encounterBudget = 29; // A positive budget must still produce only the fixed boss.
                    settings.bossRoom = module;
                }
                return new StageDefinition
                {
                    displayName = stage.displayName, dungeonSettings = settings,
                    enemyCatalog = stage.enemyCatalog, mapCountVariation = stage.mapCountVariation,
                    bossPrefab = stage.bossPrefab
                };
            }).ToArray()
        }).ToArray();
        return copy;
    }

    private static void CheckRoomCountSampling(StageDefinition definition)
    {
        int baseline = definition.dungeonSettings.mapCount;
        int variation = definition.mapCountVariation;
        int required = definition.dungeonSettings.shopRoom != null ? 4 : 2;
        int minimum = Math.Max(required, baseline - variation), maximum = baseline + variation;
        var seen = new HashSet<int>();
        for (int seed = 0; seed < 256; seed++)
        {
            int count = definition.ResolveMapCount(seed, false);
            Check(count >= minimum && count <= maximum, "Room count is outside the configured range.");
            Check(count == definition.ResolveMapCount(seed, false), "Room count is not reproducible by seed.");
            seen.Add(count);
        }
        if (maximum - minimum < 10) Check(seen.Count == maximum - minimum + 1, "The sampler did not exercise every configured room count.");
        Check(definition.dungeonSettings.mapCount == baseline, "Room count sampling mutated the settings.");
    }

    private static void CheckCurrentStage(RunManager run)
    {
        var stage = run.stageManager;
        Check(run.State == RunState.Playing && stage.IsReady && stage.Encounters.IsReady, "Stage committed before encounters were ready.");
        Check(stage.Result != null && stage.Result.success && stage.Result.root == stage.Generator.CurrentRoot,
            "Committed stage has no matching successful generation.");
        Check(stage.Encounters.Rooms.Count == stage.Result.rooms.Count, "Some generated rooms have no prepared encounter.");
        foreach (var room in stage.Encounters.Rooms)
            Check(room.State != DungeonEncounterState.Failed, "Prepared encounter failed: " + room.Error);
        var definition = run.definition.chapters[run.CurrentChapterIndex].stages[run.CurrentStageIndex];
        bool boss = run.CurrentStageIndex == run.definition.chapters[run.CurrentChapterIndex].stages.Length - 1;
        Check(stage.MapCount == definition.ResolveMapCount(run.CurrentStageSeed, boss), "Generated room count disagrees with the stage seed.");
        Check(stage.Generator.settings != definition.dungeonSettings, "Generation used the original settings instead of a runtime copy.");
        Check(run.player != null && !run.player.IsStageTransitioning && run.player.Controller.enabled,
            "Committed player remained frozen.");
        Check(!Cursor.visible && Cursor.lockState == CursorLockMode.Locked, "Playing did not restore the locked cursor.");
        Check(stage.Exit != null && stage.Exit.Activation != null && stage.Exit.Transit != null && stage.Exit.PortalView != null,
            "The generated stage has no complete hub-style portal.");
        Check(!stage.Exit.IsAvailable && stage.Exit.Activation.State == GateState.Closed && !stage.IsCleared,
            "The portal opened before actual end-room arrival.");
    }

    private static async Task CompleteObjective(RunManager run, bool bossStage)
    {
        var stage = run.stageManager;
        var end = stage.Result.endRoom.GetComponent<DungeonRoomEncounter>();
        Check(end.State == DungeonEncounterState.Cleared && end.Members.Count == 0,
            "This fixture expects the installed empty exit room.");
        Check(!stage.Exit.IsAvailable && !end.WasEntered, "The fresh end-room portal was already available.");
        Check(!stage.TryUseExit(run.player, Generation(stage.Exit)), "A direct request bypassed the unopened portal.");
        var optional = stage.Encounters.Rooms.Where(room => room.State == DungeonEncounterState.Waiting
            && room != stage.Result.bossRoom?.GetComponent<DungeonRoomEncounter>()).ToArray();
        if (!bossStage) Check(optional.Length > 0, "No untouched optional combat room was generated.");
        await Enter(end, run.player);
        Check(end.WasEntered && end.State == DungeonEncounterState.Cleared, "An empty exit did not record actual arrival.");
        if (bossStage)
        {
            var bossModule = stage.Result.bossRoom;
            Check(bossModule != null, "A chapter's last stage has no boss room.");
            var boss = bossModule.GetComponent<DungeonRoomEncounter>();
            Check(boss.Members.Count == 1 && boss.AliveCount == 1 && boss.SpentBudget == 0,
                "Boss encounter used the ordinary random budget or generated extra enemies.");
            Check(boss.RemainingBudget == bossModule.encounterBudget && bossModule.encounterBudget > 0,
                "The positive boss budget probe was not preserved.");
            var plan = stage.Result.plan;
            var endLinks = plan.links.Where(link => link.fromMapId == plan.endMapId || link.toMapId == plan.endMapId).ToArray();
            Check(endLinks.Length == 1, "The boss exit is not a leaf room.");
            var link = endLinks[0];
            Check(link.fromMapId == plan.bossMapId || link.toMapId == plan.bossMapId, "The exit bypasses its boss room.");
            int socket = link.fromMapId == plan.bossMapId ? link.fromSocketIndex : link.toSocketIndex;
            var downstream = boss.Gates.FirstOrDefault(gate => Vector3.Distance(gate.transform.position,
                bossModule.connections[socket].transform.position) < 0.05f);
            Check(downstream != null && downstream.IsLocked, "The boss downstream gate is open before combat.");
            Check(downstream.GetComponentsInChildren<Collider>(true).Any(collider => collider.enabled && !collider.isTrigger),
                "The locked boss gate has no physical collider.");
            Check(!stage.IsCleared && !stage.Exit.IsAvailable, "Entering the empty exit bypassed an undefeated boss.");
            MoveFeet(run.player, stage.Exit.transform.position);
            Check(!stage.TryUseExit(run.player, Generation(stage.Exit)), "The undefeated boss allowed exit use.");
            await Enter(boss, run.player);
            Check(boss.State == DungeonEncounterState.Fighting && boss.Gates.All(gate => gate.IsLocked),
                "Boss entry did not lock every door.");
            var member = boss.Members[0];
            Check(member.IsAwake && member.Enemy.enabled && member.GetComponent<NavMeshAgent>().isOnNavMesh,
                "The fixed boss did not activate on its native NavMesh.");
            var player = run.player;
            MoveCenter(player, stage.PlayerSpawnPosition + player.transform.TransformVector(player.Controller.center));
            stage.Encounters.ResetPlayerMotionHistory();
            await DefeatBoss(run, boss, member);
            Check(boss.Gates.All(gate => !gate.IsLocked), "Defeating the boss left a route locked.");
            await Frames(2);
        }
        else await Frames(2);
        Check(stage.IsCleared && stage.Exit.Activation.State == GateState.Opening && !stage.Exit.IsAvailable,
            "End-room arrival did not start the portal opening while keeping traversal blocked.");
        Check(stage.Exit.Activation.apertureBlocker != null && stage.Exit.Activation.apertureBlocker.enabled,
            "The portal opening animation left its aperture physically unblocked.");
        Check(!stage.TryUseExit(run.player, Generation(stage.Exit)), "A direct request bypassed portal opening.");
        await Wait(() => stage.Exit.IsAvailable, "The portal did not finish its opening animation.", 10);
        Check(stage.Exit.Activation.State == GateState.Open && !stage.Exit.Activation.apertureBlocker.enabled,
            "The opened portal still blocks its aperture.");
        foreach (var room in optional) Check(room.State == DungeonEncounterState.Waiting,
            "Optional room " + room.name + " was entered by the fixture: " + room.State + "; target=" + end.name);
        Check(run.State == RunState.Playing, "Clearing a room advanced without using the exit.");
        await RejectInvalidPortalPassages(run);
    }

    private static async Task DefeatBoss(RunManager run, DungeonRoomEncounter room, DungeonEncounterMember member)
    {
        var enemy = member.Enemy;
        var phased = enemy as Boss;
        var stats = enemy.GetComponent<EnemyStat>();
        var shield = enemy.GetComponent<BossShield>();
        int hitLimit = Mathf.Max(1, phased?.PhaseConfiguration.Count ?? 1) + 4;
        for (int attempt = 0; attempt < hitLimit && !member.HasDied; attempt++)
        {
            await Wait(() => run.State != RunState.Playing || (!enemy.IsSpawning && stats.CurrentHp > 0f
                && (phased == null || !phased.IsInvulnerable)), "The actual boss never became vulnerable.", 30);
            Check(run.State == RunState.Playing && room.State == DungeonEncounterState.Fighting,
                "The run or boss encounter ended before a confirmed defeat.");
            int phase = phased != null ? phased.PhaseIndex : -1;
            float hitSize = 2f * (stats.CurrentHp + (shield != null ? shield.CurrentAbsorption : 0f)) + 1000f;
            var hit = enemy.TakeDamage(hitSize, poiseDamage: 0f);
            Check(hit.Applied || hit.ShieldDamage > 0f, "A vulnerable boss ignored the actual damage call.");
            if (member.HasDied)
            {
                Check(hit.Killed, "The final hit did not report the actual boss death.");
                break;
            }
            Check(!hit.Killed && !room.BossDefeated && room.State == DungeonEncounterState.Fighting
                && !run.stageManager.Exit.IsAvailable, "An intermediate boss health bar unlocked the portal.");
            if (phased != null && phased.IsTransitioning)
            {
                await Wait(() => run.State != RunState.Playing || (!phased.IsTransitioning && phased.PhaseIndex > phase),
                    "The actual boss did not finish its phase revival.", 30);
                Check(run.State == RunState.Playing && phased.PhaseIndex == phase + 1,
                    "The boss did not revive into the next authored phase.");
            }
        }
        Check(member.HasDied && room.BossDefeated && room.State == DungeonEncounterState.Cleared,
            "Real lethal Enemy.TakeDamage did not confirm final boss defeat.");
    }

    private static async Task Transition(RunManager run, int nextChapter, int nextStage, bool injectFailure)
    {
        var stage = run.stageManager;
        var oldRoot = stage.Generator.CurrentRoot;
        var oldExit = stage.Exit;
        var oldTransit = oldExit.Transit;
        var oldRooms = stage.Encounters.Rooms.ToArray();
        var oldEnemies = oldRoot.GetComponentsInChildren<Enemy>(true);
        var oldNavigation = OwnedNavigation(oldRooms);
        int generation = Generation(oldExit);
        int chapter = run.CurrentChapterIndex, index = run.CurrentStageIndex;
        var transient = StageTransientObject.Track(new GameObject("RegressionOldProjectile"));
        var nextDefinition = run.definition.chapters[nextChapter].stages[nextStage];
        var savedStyle = nextDefinition.dungeonSettings.style;
        Snapshot resumed = null, preparing = null;
        int traversalsAtFreeze = 0;
        Action<RunState> onState = state =>
        {
            if (state == RunState.Playing) resumed = Snapshot.Capture(run.player);
            if (state == RunState.Preparing)
            {
                preparing = Snapshot.Capture(run.player);
                if (oldTransit != null) traversalsAtFreeze = oldTransit.TraversalCount;
            }
        };
        run.StateChanged += onState;
        try
        {
            if (previousGeneration >= 0)
                Check(!stage.TryUseExit(run.player, previousGeneration), "A previous map's late exit request was accepted.");
            Check(!stage.TryUseExit(run.player, generation), "A direct request bypassed verified portal passage.");
            if (injectFailure) nextDefinition.dungeonSettings.style = null;
            await WalkPortal(run, oldExit, true, fastCrossing: chapter == 0 && index == 1);
            await Wait(() => run.State != RunState.Playing || run.CurrentChapterIndex != chapter || run.CurrentStageIndex != index,
                "Physical portal passage did not request stage progression.");
            Check(traversalsAtFreeze == 1, "The actual portal did not dispatch exactly one walking traversal.");
            Check(!stage.TryUseExit(run.player, generation), "A duplicate exit request was accepted.");
            Check(transient == null || !transient.activeSelf, "Transition did not immediately deactivate the old projectile.");
            if (injectFailure)
            {
                await Wait(() => run.State == RunState.Error, "Actual generation failure did not produce an error state.");
                Check(!string.IsNullOrEmpty(run.LastError), "The generation error had no readable message.");
                Check(run.CurrentChapterIndex == chapter && run.CurrentStageIndex == index,
                    "Failed generation advanced the committed stage index.");
                Check(stage.Generator.CurrentRoot == oldRoot, "A failed layout discarded the previous valid map.");
                CheckFrozen(run);
                var frozen = Snapshot.Capture(run.player);
                Vector3 frozenPosition = run.player.transform.position;
                await Frames(5);
                frozen.AssertSame(Snapshot.Capture(run.player));
                Check((run.player.transform.position - frozenPosition).sqrMagnitude < 0.0001f, "Player moved while an error was displayed.");
                Check(!run.player.TakeDamage(1000f, poiseDamage: 0f).Applied, "A frozen player received damage.");
                nextDefinition.dungeonSettings.style = savedStyle;
                run.RetryPendingStage();
                run.RetryPendingStage(); // The second request must not start a second coroutine.
            }
            await Ready(run);
            Check(run.CurrentChapterIndex == nextChapter && run.CurrentStageIndex == nextStage,
                "Transition committed the wrong stage or advanced twice.");
            Check(resumed != null, "No Playing event accompanied the successful transition.");
            // Real walking and portal evaluation consume playing time before the freeze.
            preparing.AssertSame(resumed);
            Check(run.CurrentStageSeed == RunDefinitionSO.GetStageSeed(run.RunSeed, nextChapter, nextStage),
                "Transition committed the wrong deterministic stage seed.");
            await Frames(3);
            Check(oldRoot == null && oldExit == null && oldRooms.All(room => room == null)
                && oldEnemies.All(enemy => enemy == null), "An old root, exit, encounter, or enemy survived the transition.");
            Check(oldNavigation.All(data => data == null), "Old owned NavMeshData survived the transition.");
            Check(transient == null, "A tracked old projectile survived the transition.");
            Check(stage.Generator.CurrentRoot != null && stage.Generator.transform.Cast<Transform>()
                .Count(child => child.name.StartsWith("GeneratedDungeon_", StringComparison.Ordinal)) == 1,
                "Multiple generated map roots survived a successful transition.");
            previousGeneration = generation;
        }
        finally
        {
            nextDefinition.dungeonSettings.style = savedStyle;
            if (run != null) run.StateChanged -= onState;
        }
    }

    private static async Task UseFinalExit(RunManager run)
    {
        var stage = run.stageManager;
        int generation = Generation(stage.Exit);
        if (previousGeneration >= 0)
            Check(!stage.TryUseExit(run.player, previousGeneration), "A stale final-stage request was accepted.");
        Check(!stage.TryUseExit(run.player, generation), "A direct request bypassed the final portal.");
        await WalkPortal(run, stage.Exit, true);
        await Wait(() => run.State != RunState.Playing, "The final physical portal passage was rejected.");
        Check(!stage.TryUseExit(run.player, generation), "The final exit was accepted twice.");
        await Frames(2);
    }

    private static async Task RejectInvalidPortalPassages(RunManager run)
    {
        var exit = run.stageManager.Exit;
        var transit = exit.Transit;
        int traversals = transit.TraversalCount;
        await WalkPortal(run, exit, false);
        Check(run.State == RunState.Playing && transit.TraversalCount == traversals,
            "Walking backwards through the portal advanced the run.");
        float outsideAperture = transit.apertureRadius + 2f;
        PositionAtPortal(run.player, exit, true, outsideAperture);
        Check(!transit.FitsAperture(run.player.Controller), "The off-center portal probe still fits the aperture.");
        await Frames(3);
        await MoveThroughPortal(run, transit, true);
        Check(run.State == RunState.Playing && transit.TraversalCount == traversals,
            "A body outside the circular portal aperture advanced the run.");

        PositionAtPortal(run.player, exit, true, 0f);
        await Frames(3);
        Check(run.TryOpenInventory(exit), "Could not open the real inventory for the portal modal guard.");
        try
        {
            await MoveThroughPortal(run, transit, true);
            Check(exit.ContainsPlayer(run.player), "The modal portal probe did not enter the physical aperture.");
            Check(run.State == RunState.Playing && run.IsInventoryOpen && transit.TraversalCount == traversals,
                "A gameplay modal allowed or consumed a portal traversal.");
            Check(!run.stageManager.TryUseExit(run.player, Generation(exit)), "A modal allowed a direct exit request.");
        }
        finally { run.CloseInventory(); }
        Check(exit.IsAvailable, "Closing the modal left the completed portal unavailable.");
        Check(!run.stageManager.TryUseExit(run.player, Generation(exit)),
            "Occupying the open aperture without a verified walk-through triggered progression.");
    }

    private static async Task WalkPortal(RunManager run, StageExit exit, bool forward, bool fastCrossing = false)
    {
        var transit = exit.Transit;
        PositionAtPortal(run.player, exit, forward, 0f);
        await Frames(3); // Let the real CharacterController register with the portal trigger.
        if (!fastCrossing)
        {
            await MoveThroughPortal(run, transit, forward);
            return;
        }

        float fixedTimeAtApproach = Time.fixedTime;
        await Wait(() => Time.fixedTime > fixedTimeAtApproach, "The portal approach did not receive a physics step.", 5);
        var player = run.player;
        var trigger = transit.GetComponent<BoxCollider>();
        float startZ = transit.transform.InverseTransformPoint(player.transform.position).z;
        float pastRearZ = trigger.center.z - trigger.size.z * .5f - player.Controller.radius;
        player.Controller.Move(transit.transform.TransformVector(new Vector3(0f, 0f, pastRearZ - startZ - .25f)));
        Check(transit.transform.InverseTransformPoint(player.transform.position).z < pastRearZ,
            "The fast portal probe did not cross the entire trigger in one movement.");
        await Frames(2);
    }

    private static void PositionAtPortal(PlayerController player, StageExit exit, bool forward, float horizontal)
    {
        var transit = exit.Transit;
        var floor = transit.transform.TransformPoint(new Vector3(horizontal, 0f,
            transit.crossingPlaneOffset + (forward ? 1f : -.55f)));
        floor.y = exit.transform.position.y;
        var facing = Quaternion.LookRotation((forward ? -1f : 1f) * transit.transform.forward, Vector3.up);
        GateTransit.Teleport(player.transform, player.Controller, null,
            DungeonFastTravelLanding.PlayerRootAtFloor(floor, facing, player.Controller), facing);
        player.VerticalVelocity = 0f;
        player.Controller.Move(Vector3.down * .01f);
    }

    private static async Task MoveThroughPortal(RunManager run, GateTransit transit, bool forward)
    {
        int chapter = run.CurrentChapterIndex, stage = run.CurrentStageIndex;
        var direction = (forward ? -1f : 1f) * transit.transform.forward;
        for (int step = 0; step < 18; step++)
        {
            if (run.State != RunState.Playing || run.CurrentChapterIndex != chapter || run.CurrentStageIndex != stage) return;
            Check(run.player.Controller.enabled, "The portal approach unexpectedly disabled the CharacterController.");
            run.player.Controller.Move(direction * .1f);
            await Frames(1);
        }
        await Frames(2);
    }

    private static void CheckFrozen(RunManager run)
    {
        var player = run.player;
        Check(player.IsStageTransitioning && !player.Controller.enabled && !player.Input.enabled && !player.Movement.enabled,
            "Error/result did not freeze player input, collision movement, and actions.");
    }

    private static async Task Enter(DungeonRoomEncounter room, PlayerController player)
    {
        var gate = room.Gates.FirstOrDefault(candidate => !candidate.IsLocked);
        Check(gate != null, "The fixture cannot find an unlocked room entrance.");
        // Reset every swept entrance history using the real transition freeze. Stay
        // close to this doorway: a distant outside point can enter an adjacent room.
        Vector3 outsideCenter = gate.transform.TransformPoint(new Vector3(0f, 1.2f, 0.5f));
        player.BeginStageTransition();
        await Frames(3);
        player.CompleteStageTransition(outsideCenter - player.transform.TransformVector(player.Controller.center), gate.transform.rotation);
        await Frames(2);
        MoveCenter(player, gate.transform.TransformPoint(new Vector3(0f, 1.2f, -3f)));
        await Frames(2);
    }

    private static void MoveFeet(PlayerController player, Vector3 floor)
    {
        var controller = player.Controller;
        float halfHeight = controller.height * Mathf.Abs(controller.transform.lossyScale.y) * 0.5f;
        MoveCenter(player, floor + Vector3.up * (halfHeight + controller.skinWidth + 0.05f));
    }

    private static void MoveCenter(PlayerController player, Vector3 center)
    {
        var controller = player.Controller;
        bool wasEnabled = controller.enabled;
        controller.enabled = false;
        player.transform.position = center - player.transform.TransformVector(controller.center);
        player.VerticalVelocity = 0f;
        controller.enabled = wasEnabled;
        Physics.SyncTransforms();
    }

    private static NavMeshData[] OwnedNavigation(IEnumerable<DungeonRoomEncounter> rooms)
        => rooms.Select(room => GetField<NavMeshData>(room, "_ownedNavigation")).Where(data => data != null).ToArray();

    private static IEnumerable<BaseForm> Forms(PlayerController player)
    {
        var forms = (System.Collections.IEnumerable)typeof(FormManager).GetField("_registeredForms", Hidden).GetValue(player.FormManager);
        foreach (var form in forms) if (form is BaseForm candidate) yield return candidate;
    }

    private static int Generation(StageExit exit) => GetField<int>(exit, "_generation");
    private static FieldInfo Field(object target, string name)
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Hidden);
            if (field != null) return field;
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }
    private static T GetField<T>(object target, string name) => (T)Field(target, name).GetValue(target);
    private static void SetField(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static void Remember(Dictionary<Object, string> assets, Object asset)
    { if (asset != null && !assets.ContainsKey(asset)) assets.Add(asset, EditorJsonUtility.ToJson(asset)); }

    private sealed class MemoryUpgradeStore : IPlayerUpgradeStore
    {
        private PlayerUpgradeProfile _profile;
        public MemoryUpgradeStore(PlayerUpgradeProfile profile) => _profile = profile.Copy();
        public bool TryLoad(out PlayerUpgradeProfile value, out string error)
        { value = _profile.Copy(); error = null; return true; }
        public bool TrySave(PlayerUpgradeProfile value, out string error)
        { _profile = value.Copy(); error = null; return true; }
    }

    private sealed class Snapshot
    {
        public PlayerController Player;
        private IForm form;
        private ItemDefinition[] items;
        private float hp, gauge, attack, maxHp, dash, formChange;
        private Dictionary<BaseForm, Vector2> timers;
        public static Snapshot Capture(PlayerController player) => new Snapshot
        {
            Player = player, form = player.FormManager.CurrentForm, items = player.ItemRuntime.OwnedItems.ToArray(),
            hp = player.StatManager.CurrentHp, gauge = player.StatManager.CurrentUltimateGauge,
            attack = player.StatManager.GetStat(StatType.ST_ATK), maxHp = player.StatManager.MaxHp,
            dash = GetField<float>(player, "_dashCooldownTimer"), formChange = GetField<float>(player, "_formChangeCooldownTimer"),
            timers = Forms(player).ToDictionary(candidate => candidate, candidate => new Vector2(candidate.SkillCooldownRemaining,
                GetField<float>(candidate, "_ultimateCooldownTimer")))
        };
        public void AssertSame(Snapshot other)
        {
            Check(other.Player == Player, "Transition replaced the live player instance.");
            Check(ReferenceEquals(other.form, form), "Transition reset the selected form.");
            Check(items.SequenceEqual(other.items), "Transition reset item ownership or item identities.");
            Near(hp, other.hp, "HP"); Near(gauge, other.gauge, "ultimate resource");
            Near(attack, other.attack, "growth modifier"); Near(maxHp, other.maxHp, "max HP");
            Near(dash, other.dash, "dash cooldown"); Near(formChange, other.formChange, "form cooldown");
            Check(timers.Count == other.timers.Count, "Transition replaced registered forms.");
            foreach (var timer in timers)
            {
                Check(other.timers.TryGetValue(timer.Key, out Vector2 value), "Transition replaced a registered form instance.");
                Near(timer.Value.x, value.x, "form skill cooldown"); Near(timer.Value.y, value.y, "form ultimate cooldown");
            }
        }
    }

    // Run these separately because they intentionally unload the current Play Mode scene.
    public static string StartDeathAndRestart()
    {
        Begin(DeathRestartKey);
        _ = DeathAndRestart();
        return "Death/restart check started; poll " + DeathRestartKey + ".";
    }

    private static async Task DeathAndRestart()
    {
        try
        {
            var run = Object.FindFirstObjectByType<RunManager>();
            Check(run != null, "RunManager is missing.");
            await Ready(run);
            var player = run.player;
            var oldRun = run;
            int chapter = run.CurrentChapterIndex, stage = run.CurrentStageIndex;
            player.StatManager.TakeDamage(float.MaxValue, ElementType.ELEMENT_NONE, false);
            await Wait(() => run.State == RunState.Lost, "Real lethal player damage did not produce run loss.");
            CheckFrozen(run);
            Check(run.CurrentChapterIndex == chapter && run.CurrentStageIndex == stage, "Death advanced progress.");
            Check(!run.stageManager.TryUseExit(player, Generation(run.stageManager.Exit)), "Death allowed exit use.");
            Check(Cursor.visible && Cursor.lockState == CursorLockMode.None, "Death result did not release the cursor.");
            run.RestartRun();
            await Wait(() => Object.FindFirstObjectByType<RunManager>() != null
                && Object.FindFirstObjectByType<RunManager>() != oldRun, "Retry did not reload the game scene.");
            run = Object.FindFirstObjectByType<RunManager>();
            await Ready(run);
            Check(run.CurrentChapterIndex == 0 && run.CurrentStageIndex == 0 && run.player != player,
                "Retry did not begin a fresh run from the first chapter.");
            Near(run.player.StatManager.CurrentHp, run.player.StatManager.MaxHp, "new run HP");
            CheckCurrentStage(run);
            SessionState.SetString(DeathRestartKey, "PASS: " + checks + " checks; real death, frozen result, first-chapter retry, fresh player and actual regenerated map.");
        }
        catch (Exception exception) { SessionState.SetString(DeathRestartKey, "FAIL: " + exception); }
        finally { running = false; }
    }

    public static string StartReturnToTitle()
    {
        Begin(TitleKey);
        _ = ReturnToTitle();
        return "Title navigation check started; poll " + TitleKey + ".";
    }

    private static async Task ReturnToTitle()
    {
        try
        {
            var run = Object.FindFirstObjectByType<RunManager>();
            Check(run != null, "RunManager is missing.");
            run.ReturnToTitle();
            await Wait(() => SceneManager.GetActiveScene().name == "TitleScene", "Title menu did not load TitleScene.");
            Check(Object.FindFirstObjectByType<RunManager>() == null, "Title retained the previous run manager.");
            Check(Time.timeScale == 1f, "Title navigation retained a paused time scale.");
            SessionState.SetString(TitleKey, "PASS: " + checks + " checks; title scene navigation and unloaded run.");
        }
        catch (Exception exception) { SessionState.SetString(TitleKey, "FAIL: " + exception); }
        finally { running = false; }
    }

    private static async Task Ready(RunManager run)
    {
        await Wait(() => run == null || run.State == RunState.Playing || run.State == RunState.Error,
            "Stage generation/encounter preparation did not complete.", 150);
        Check(run != null && run.State == RunState.Playing, "Stage preparation failed: " + (run != null ? run.LastError : "Run was destroyed."));
    }
    private static async Task Frames(int count)
    {
        int start = Time.frameCount;
        await Wait(() => Time.frameCount >= start + count, "The player loop stopped advancing.");
    }
    private static async Task Wait(Func<bool> predicate, string error, int seconds = 20)
    {
        var timeout = DateTime.UtcNow.AddSeconds(seconds);
        while (!predicate())
        {
            if (!Application.isPlaying || DateTime.UtcNow >= timeout) throw new Exception(error);
            await Task.Delay(20);
        }
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); checks++; }
    private static void Near(float expected, float actual, string label)
        => Check(Mathf.Abs(expected - actual) < 0.001f, label + " changed: " + expected + " -> " + actual);
}

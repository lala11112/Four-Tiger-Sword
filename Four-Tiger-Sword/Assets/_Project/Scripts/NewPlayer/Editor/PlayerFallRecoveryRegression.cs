using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using StudioBaekho;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Exercises fall detection, safe landing selection and the live recovery sequence.</summary>
public static class PlayerFallRecoveryRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string V3Pit =
        "Assets/ARPGRoomSetV3/Prefabs/Rooms/고통의_심연_관제실_2b4f6584.prefab";
    private const string RefreshPit =
        "Assets/ARPGMapRefresh/Prefabs/Rooms/normal_0211f42f761304940bb6edfc23620fea.prefab";
    private const string SourceMaterial = "Assets/WeaponsDuo/Materials/Futuristic_DualSword_65cm.mat";
    private static readonly int FadeAmount = Shader.PropertyToID("_FadeAmount");
    private static readonly int HologramColor = Shader.PropertyToID("_HologramColor");
    private static readonly int GlitchAmount = Shader.PropertyToID("_GlitchAmount");
    private static readonly int PreservedValue = Shader.PropertyToID("_FallRegressionOriginalValue");
    private static string _status = "Not started.";
    private static bool _running;
    private static double _deadline;
    private static int _checks;

    [MenuItem("Tools/Studio Baekho/Player/Run Fall Recovery Regression")]
    private static void RunMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run fall recovery geometry checks in Edit Mode.");

        _checks = 0;
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var fixture = new Fixture(scene, new Vector3(48f, 5f, -64f));
            var start = fixture.Room("Start", Vector3.zero);
            var combat = fixture.Room("Combat", Vector3.right * 30f);
            var corner = fixture.Room("Deep corner", Vector3.back * 30f + Vector3.down * 4f);
            corner.isCornerRoom = true;
            var spawn = start.GetComponent<StageMapAnchors>().teleportPoint;
            Vector3 zoneCenter = fixture.Root.transform.position + Vector3.up * 12f;
            var deadZone = MakeTrigger(fixture, "Stage dead zone", start.transform, zoneCenter, true);
            var otherTrigger = MakeTrigger(fixture, "Unrelated trigger", start.transform, zoneCenter + Vector3.right * 8f, false);
            var foreignZone = MakeTrigger(fixture, "Foreign dead zone", null, zoneCenter + Vector3.left * 8f, true);
            var service = fixture.Prepare(spawn, start, combat);
            CheckDeadZones(fixture, service, deadZone, otherTrigger, foreignZone);

            var player = fixture.Player;
            Vector3 stageOrigin = fixture.Root.transform.position;
            fixture.Place(fixture.Root.transform.TransformPoint(new Vector3(0f, -6.4f, 0f)));
            Check(!service.IsBelowVoid(.5f), "The generated corner's lower boundary was ignored.");
            fixture.Place(stageOrigin + Vector3.down * 6.7f);
            Check(service.IsBelowVoid(.5f), "The player's feet below the stage margin were not detected.");
            fixture.Place(corner.transform.position);
            Check(!service.IsBelowVoid(.5f), "A normal lower floor counted as a fall.");

            var safeFloor = start.transform.TransformPoint(new Vector3(-3f, 0f, 1f));
            fixture.Place(safeFloor);
            service.TickSafePosition(.1f);
            Check(ResolvesNear(service, spawn.position), "An unconfirmed landing was recorded before stable contact.");
            service.TickSafePosition(.1f);
            service.TickSafePosition(.1f);
            Vector3 recent = player.transform.position;
            Check(ResolvesNear(service, recent), "A supported position did not supersede the start fallback.");

            fixture.Place(start.transform.TransformPoint(new Vector3(8.87f, 0f, 0f)));
            service.TickSafePosition(.3f);
            Check(ResolvesNear(service, recent), "A position without support around the capsule replaced the safe position.");

            var doorFloor = start.transform.TransformPoint(Vector3.forward * 6f);
            var door = fixture.Host("Door exclusion", start.transform).AddComponent<BaekhoMapConnection>();
            door.transform.position = doorFloor;
            door.width = 3f;
            door.height = 3f;
            start.connections = new[] { door };
            fixture.Place(doorFloor);
            service.TickSafePosition(.3f);
            Check(ResolvesNear(service, recent), "A doorway was recorded as a safe position.");

            // A room locking after the initial noncombat candidate must constrain the next candidate check.
            fixture.Place(safeFloor);
            service.TickSafePosition(.3f);
            var encounter = combat.gameObject.AddComponent<DungeonRoomEncounter>();
            SetProperty(encounter, "State", DungeonEncounterState.Fighting);
            var member = fixture.Host("Alive encounter member", combat.transform).AddComponent<DungeonEncounterMember>();
            Field<HashSet<DungeonEncounterMember>>(encounter, "_alive").Add(member);
            Field<List<DungeonRoomEncounter>>(fixture.Director, "_rooms").Add(encounter);
            Check(player.IsInRoomCombat, "The combat fixture did not activate the room lock.");
            var combatAnchor = combat.GetComponent<StageMapAnchors>().teleportPoint;
            Check(ResolvesNear(service, combatAnchor.position), "Combat recovery selected a position outside the locked room.");
            Check(encounter.State == DungeonEncounterState.Fighting && encounter.AliveCount == 1,
                "Selecting the combat fallback changed the encounter.");

            service.ResetHistory();
            var combatFloor = combat.transform.TransformPoint(Vector3.right * 3f);
            fixture.Place(combatFloor);
            service.TickSafePosition(.3f);
            Vector3 combatRecent = player.transform.position;
            Check(ResolvesNear(service, combatRecent), "A confirmed in-room pose did not supersede the fallback anchor.");
            var blocker = fixture.Box("New obstacle", combat.transform, combatFloor + Vector3.up, Vector3.one);
            Physics.SyncTransforms();
            fixture.Place(stageOrigin + Vector3.down * 7f);
            Check(ResolvesNear(service, combatAnchor.position), "Recovery selected a recorded pose after an obstacle occupied it.");
            blocker.enabled = false;

            SetProperty(encounter, "State", DungeonEncounterState.Cleared);
            service.ResetHistory();
            Check(ResolvesNear(service, spawn.position), "Resetting history retained a pose from before the warp.");
            SetProperty(fixture.Stage, "IsReady", false);
            Check(!service.IsValid && !service.TryGetRecoveryPose(out _, out _),
                "A suspended stage still supplied fall recovery poses.");
            return $"Fall recovery geometry: {_checks} checks passed.";
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    [MenuItem("Tools/Studio Baekho/Player/Run Fall Recovery Play Mode Regression")]
    private static void PlayMenu() => Debug.Log(Start());

    public static string Status() => _status;

    public static string Start()
    {
        if (!Application.isPlaying || Time.timeScale <= 0f)
            throw new InvalidOperationException("Enter an unpaused Play Mode session first.");
        if (_running) return _status;

        _running = true;
        _checks = 0;
        _deadline = Time.realtimeSinceStartupAsDouble + 60d;
        _status = "RUNNING: V3 pit recovery.";
        _ = RunPlayMode();
        return _status;
    }

    private static BoxCollider MakeTrigger(Fixture fixture, string name, Transform parent, Vector3 center, bool deadZone)
    {
        var collider = fixture.Box(name, parent, center, new Vector3(.15f, 2f, 2f));
        collider.transform.rotation = Quaternion.identity;
        collider.isTrigger = true;
        if (deadZone) collider.gameObject.AddComponent<BaekhoMapAsset>().role = BaekhoMapAsset.AssetRole.Deadzone;
        return collider;
    }

    private static void CheckDeadZones(Fixture fixture, DungeonFallRecovery service, BoxCollider deadZone,
        BoxCollider otherTrigger, BoxCollider foreignZone)
    {
        var player = fixture.Player;
        void MoveCenter(Vector3 center) => MoveFixture(player, center - player.Controller.center, Quaternion.identity);
        bool Overlaps(Vector3 center)
        {
            service.ResetHistory();
            MoveCenter(center);
            return service.HasEnteredDeadZone();
        }

        Check(!Overlaps(otherTrigger.bounds.center) && !Overlaps(foreignZone.bounds.center),
            "An unrelated trigger or a dead zone outside this generation started a fall.");
        Vector3 zoneCenter = deadZone.bounds.center;
        Check(Overlaps(zoneCenter), "A current stage dead zone did not detect the player capsule on the first sample.");
        deadZone.enabled = false;
        Check(!Overlaps(zoneCenter), "A disabled dead zone still detected the player capsule.");
        deadZone.enabled = true;
        deadZone.gameObject.SetActive(false);
        Check(!Overlaps(zoneCenter), "An inactive dead zone still detected the player capsule.");
        deadZone.gameObject.SetActive(true);

        Vector3 before = zoneCenter + Vector3.left * 3f;
        Vector3 after = zoneCenter + Vector3.right * 3f;
        Check(!Overlaps(before), "The pre-crossing endpoint overlapped the thin dead zone.");
        MoveCenter(after);
        Check(service.HasEnteredDeadZone(), "A high-speed traversal tunneled through the thin dead zone.");
        Check(!Overlaps(before), "Resetting history retained a sweep across the preceding warp.");
        Check(!Overlaps(after), "A reset sweep treated a teleport across the dead zone as a fall.");
        service.ResetHistory();
    }

    private static async Task RunPlayMode()
    {
        int originalFrameRate = Application.targetFrameRate;
        int originalVsync = QualitySettings.vSyncCount;
        try
        {
            await RunPit(V3Pit, 100f, 90f, false);
            _status = "RUNNING: Refresh pit recovery and cancellation.";
            await RunPit(RefreshPit, 7f, 1f, true);
            _status = $"PASSED: {_checks} live checks across the V3 and Refresh pit rooms.";
        }
        catch (Exception exception)
        {
            _status = "FAILED: " + exception.Message;
            Debug.LogException(exception);
        }
        finally
        {
            Application.targetFrameRate = originalFrameRate;
            QualitySettings.vSyncCount = originalVsync;
            _running = false;
        }
    }

    private static async Task RunPit(string path, float initialHp, float expectedHp, bool testRepeatAndCancel)
    {
        var scene = SceneManager.CreateScene("Fall recovery regression");
        RevealFixture visual = null;
        try
        {
            var fixture = new Fixture(scene, new Vector3(4096f, 20f, 4096f));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Check(prefab != null, "Missing pit regression prefab: " + path);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.transform.SetParent(fixture.Root.transform, false);
            var room = instance.GetComponent<BaekhoMapModule>();
            int groundLayer = LayerMask.NameToLayer("Ground");
            foreach (var ground in room.groundObjects)
                if (ground != null) BaekhoDungeonGeometry.SetLayerRecursively(ground, groundLayer);
            var anchor = instance.GetComponent<StageMapAnchors>().teleportPoint;
            Check(anchor != null, instance.name + " is missing its authored safe pose.");
            var service = fixture.Prepare(anchor, room);
            var player = fixture.Player;
            Check(service.TryGetRecoveryPose(out var landing, out var landingRotation),
                instance.name + " has no clear fallback landing: " + service.LastError);
            MoveFixture(player, landing, landingRotation);
            visual = new RevealFixture(fixture);
            Invoke(player, "BindFallRecovery", service);
            var stats = player.StatManager;
            var engravings = PlayerEngravingRuntime.Ensure(player);
            engravings.InitializeRun(new EngravingSnapshot(EngravingSettingsSO.Default, new[] { "revive", "armor" }));
            Check(engravings.Snapshot.Has("revive"), "The fixture is missing its unused revive effect.");
            Check(Mathf.Approximately(stats.MaxHp, 100f), "Fixture maximum HP was not 100.");
            stats.ConsumeHealth(stats.MaxHp - initialHp);
            stats.GrantShield(30f, 20f);
            stats.AddUltimateGauge(35f);

            await WaitForGameTime(.3f);
            // Drop from above a supported floor first. Only the void crossing should recover.
            Drop(player, landing + Vector3.up * .65f);
            await WaitForGameTime(.55f);
            Check(!player.IsFallRecovering && Mathf.Approximately(stats.CurrentHp, initialHp)
                && Near(player.transform.position, landing, .2f),
                instance.name + " triggered recovery on a normal landing.");
            var lastGrounded = player.transform.position;

            var pit = FindOpenPit(instance, room.localBounds.min.y);
            Check(pit != null, "Could not find an open vertical path in the actual pit: " + path);
            Vector3 abovePit = pit.position + Vector3.up * 4f;
            Drop(player, abovePit);
            Check(!service.IsBelowVoid(.5f), "The pit trial started below the fall threshold.");
            await ObserveRecovery(fixture, visual, lastGrounded, landingRotation, expectedHp, true,
                expectedDeadZone: testRepeatAndCancel, verifyUnscaledTime: !testRepeatAndCancel);

            if (!testRepeatAndCancel) return;

            // Simulate a dash crossing the void between frames. HP already at 1 must stay there.
            MoveFixture(player, abovePit + Vector3.down * 12f, landingRotation);
            player.StateMachine.ChangeState(new PlayerDashState(player));
            Check(player.IsDashing, "The dash fixture did not enter its dash state.");
            await ObserveRecovery(fixture, visual, landing, landingRotation, 1f, false);

            // Drop beyond the outer room bounds and cancel before the drop delay reaches the fade.
            Vector3 outside = room.transform.TransformPoint(room.localBounds.max + new Vector3(4f, -2f, 4f));
            Drop(player, outside);
            await Wait(() => player.IsFallRecovering, "Outer-boundary fall detection");
            player.BeginStageTransition();
            await WaitForGameTime(.4f);
            Check(!player.IsFallRecovering && !player.IsFallProtected && Cover(scene) == null
                && player.IsStageTransitioning && !player.Input.enabled && stats.CurrentHp == 1f
                && !engravings.ReviveSpent && visual.IsRestored,
                "Starting a stage transition left a recovery cover, grace, HP charge or stale input ownership.");

            // A save-blocked advance may release the same stage without preparing a new dungeon.
            player.CompleteStageTransition(landing, landingRotation);
            Check(!player.IsStageTransitioning && player.Input.enabled && player.Controller.enabled,
                "Completing a same-stage transition failed to resume gameplay.");
            Drop(player, abovePit + Vector3.down * 12f);
            await ObserveRecovery(fixture, visual, landing, landingRotation, 1f, false);

            await CheckPresentationCancellation(fixture, visual, landing, landingRotation,
                abovePit + Vector3.down * 12f);
            stats.ConsumeHealth(stats.CurrentHp - 1f);
            Drop(player, abovePit + Vector3.down * 12f);
            await Wait(() => visual.Effect.IsRevealing, "Reveal before stage suspension");
            fixture.Stage.Suspend();
            Check(fixture.Stage.FallRecovery == null && !service.IsValid
                && !player.IsFallRecovering && !player.IsFallProtected && Cover(scene) == null
                && player.Input.enabled && player.Controller.enabled && stats.CurrentHp == 1f && visual.IsRestored,
                "Stage suspension retained recovery, protection, the cover, the hologram, or the player lock.");
        }
        finally
        {
            try
            {
                if (Application.isPlaying && scene.IsValid() && scene.isLoaded)
                {
                    var unload = SceneManager.UnloadSceneAsync(scene);
                    if (unload != null && !unload.isDone && Time.realtimeSinceStartupAsDouble < _deadline)
                        await Wait(() => unload.isDone, "Fixture scene cleanup");
                }
            }
            finally { visual?.Dispose(); }
        }
    }

    private static async Task CheckPresentationCancellation(Fixture fixture, RevealFixture visual, Vector3 landing,
        Quaternion rotation, Vector3 belowPit)
    {
        var player = fixture.Player;
        var stats = player.StatManager;
        var cases = new[]
        {
            (Phase: "FadingOut", Transition: false, Charged: false),
            (Phase: "FadingIn", Transition: true, Charged: true),
            (Phase: "EmptyHold", Transition: false, Charged: true),
            (Phase: "Revealing", Transition: true, Charged: true)
        };
        float originalDropDelay = Field<float>(player, "_fallDropDelaySeconds");
        SetField(player, "_fallDropDelaySeconds", .05f);
        try
        {
            foreach (var trial in cases)
            {
                stats.Heal(stats.MaxHp - stats.CurrentHp);
                Check(stats.CurrentHp == 100f, $"{trial.Phase} cancellation did not start at full HP.");
                int hpChanges = 0;
                void CountHpChange(float _, float __) => hpChanges++;
                stats.OnHpChanged += CountHpChange;
                try
                {
                    Drop(player, belowPit);
                    await Wait(() => Field<object>(player, "_fallPhase").ToString() == trial.Phase,
                        trial.Phase + " before cancellation");
                    if (trial.Transition) player.BeginStageTransition();
                    else player.enabled = false;

                    Check(!player.IsFallRecovering && !player.IsFallProtected && Cover(fixture.Scene) == null
                        && visual.IsRestored, $"{trial.Phase} cancellation retained the cover, protection, or renderer override.");
                    Check(trial.Transition
                        ? player.IsStageTransitioning && !player.Input.enabled && !player.Controller.enabled
                        : !player.enabled && player.Input.enabled && player.Controller.enabled,
                        $"{trial.Phase} cancellation left the wrong components enabled.");

                    if (trial.Transition) player.CompleteStageTransition(landing, rotation);
                    else
                    {
                        MoveFixture(player, landing, rotation);
                        player.enabled = true;
                    }
                    await WaitForGameTime(.35f);
                    int expectedEvents = trial.Charged ? 1 : 0;
                    float expectedHp = trial.Charged ? 90f : 100f;
                    Check(!player.IsStageTransitioning && !player.IsFallRecovering && !player.IsFallProtected
                        && player.isActiveAndEnabled && player.Input.enabled && player.Controller.enabled
                        && !player.IsGameplayModalPaused && Cover(fixture.Scene) == null && visual.IsRestored
                        && stats.CurrentHp == expectedHp && hpChanges == expectedEvents,
                        $"{trial.Phase} resumed with stale recovery state or a duplicate charge (HP {stats.CurrentHp}, events {hpChanges}).");
                }
                finally { stats.OnHpChanged -= CountHpChange; }
            }
        }
        finally { SetField(player, "_fallDropDelaySeconds", originalDropDelay); }
    }

    private static async Task ObserveRecovery(Fixture fixture, RevealFixture visual, Vector3 expectedLanding,
        Quaternion landingRotation, float expectedHp, bool expectCharge, bool? expectedDeadZone = null,
        bool verifyUnscaledTime = false)
    {
        var player = fixture.Player;
        var stats = player.StatManager;
        float originalScale = Time.timeScale;
        float originalHp = stats.CurrentHp;
        int hpChanges = 0;
        float alphaAtCharge = -1f;
        Vector3 positionAtCharge = default;
        bool hiddenAtCharge = false;
        float shieldBefore = stats.Shield;
        void OnHpChanged(float _, float __)
        {
            hpChanges++;
            var image = Cover(fixture.Scene);
            alphaAtCharge = image != null ? image.color.a : -1f;
            positionAtCharge = player.transform.position;
            hiddenAtCharge = visual.Body.forceRenderingOff;
        }
        stats.OnHpChanged += OnHpChanged;
        try
        {
            await Wait(() => player.IsFallRecovering, "Pit detection");
            Check(player.IsFallProtected && player.IsGameplayModalPaused && Time.timeScale > 0f,
                "Recovery did not protect and lock the player while world time continued.");
            if (expectedDeadZone.HasValue)
                Check(fixture.Stage.FallRecovery.IsBelowVoid(.5f) != expectedDeadZone.Value,
                    expectedDeadZone.Value ? "The actual Refresh trigger failed to start recovery above the stage boundary."
                        : "The triggerless V3 pit recovered above the stage boundary.");
            Check(!player.TryFastTravel(expectedLanding, landingRotation), "Fast travel was allowed during recovery.");
            Check(!player.TakeDamage(1f).Applied && !stats.TakeDamage(1f, ElementType.ELEMENT_NONE, false).Applied,
                "An ordinary hit reached the player during the drop delay.");

            double detectedAt = Time.realtimeSinceStartupAsDouble;
            float detectionHeight = player.transform.position.y;
            Check(Cover(fixture.Scene) == null && visual.IsRestored,
                "The cover or hologram appeared immediately on fall detection.");
            if (verifyUnscaledTime) Time.timeScale = .25f;
            bool dropStayedVisible = true;
            while (player.IsFallRecovering && Cover(fixture.Scene) == null)
            {
                dropStayedVisible &= visual.IsRestored && player.IsGameplayModalPaused && hpChanges == 0;
                await NextTick("Visible drop delay");
            }
            double fadeOutAt = Time.realtimeSinceStartupAsDouble;
            CheckDuration("Visible drop", fadeOutAt - detectedAt, .9, 1.6);
            Check(dropStayedVisible && Cover(fixture.Scene) != null && stats.CurrentHp == originalHp
                && detectionHeight - player.transform.position.y > .25f,
                "The player stopped descending, became hidden, or paid HP before the fade.");

            var positionBeforeWarp = player.transform.position;
            float alphaAtWarp = -1f;
            Vector3 positionAtWarp = default;
            double warpedAt = -1d, emptyAt = -1d, revealAt = -1d;
            bool keptControlsLocked = true;
            bool keptBodyHidden = true;
            bool showedCyanMidReveal = false;
            while (player.IsFallRecovering)
            {
                double now = Time.realtimeSinceStartupAsDouble;
                var image = Cover(fixture.Scene);
                keptControlsLocked &= player.IsGameplayModalPaused && player.ActionDeltaTime == 0f && !player.CanDash;
                if (warpedAt < 0d && player.transform.position.y > positionBeforeWarp.y + 1f)
                {
                    warpedAt = now;
                    alphaAtWarp = image != null ? image.color.a : -1f;
                    positionAtWarp = player.transform.position;
                }
                if (warpedAt >= 0d && !visual.Effect.IsRevealing)
                {
                    keptBodyHidden &= visual.Body.forceRenderingOff;
                    if (emptyAt < 0d && (image == null || image.color.a <= .001f)) emptyAt = now;
                }
                if (visual.Effect.IsRevealing)
                {
                    if (revealAt < 0d) revealAt = now;
                    showedCyanMidReveal |= visual.HasCyanTransition;
                }
                await NextTick("Recovery presentation");
            }
            double resumedAt = Time.realtimeSinceStartupAsDouble;
            float landingAlpha = expectCharge ? alphaAtCharge : alphaAtWarp;
            Vector3 actualLanding = expectCharge ? positionAtCharge : positionAtWarp;
            Check(landingAlpha >= .999f, $"The cover was not black at the landing (alpha={landingAlpha:F4}, HP events={hpChanges}).");
            Check(Near(actualLanding, expectedLanding, .2f),
                $"Recovery landed at {actualLanding:F3}, expected near the verified safe position {expectedLanding:F3}.");
            Check(Near(player.transform.position, expectedLanding, .2f) && player.Controller.enabled
                && player.Input.enabled && !player.IsGameplayModalPaused && Cover(fixture.Scene) == null,
                $"Reveal left the player at {player.transform.position:F3} or retained a control or cover lock.");
            Check(warpedAt >= 0d && emptyAt >= 0d && revealAt >= 0d && keptBodyHidden
                && (!expectCharge || hiddenAtCharge) && keptControlsLocked,
                "Recovery did not hold an empty landing or kept the player visible or actionable at the wrong time.");
            CheckDuration("Fade out", warpedAt - fadeOutAt, .1, .49);
            CheckDuration("Fade in", emptyAt - warpedAt, .15, .6);
            CheckDuration("Empty landing", revealAt - emptyAt, .18, .75);
            CheckDuration("Hologram reveal", resumedAt - revealAt, .9, 1.6);
            Check(showedCyanMidReveal && visual.IsRestored,
                "The reveal did not show cyan glitch progress or restore the renderer materials and property blocks.");
            Check(Mathf.Approximately(stats.CurrentHp, expectedHp) && hpChanges == (expectCharge ? 1 : 0)
                && Mathf.Approximately(stats.Shield, shieldBefore) && Mathf.Approximately(stats.CurrentUltimateGauge, 35f),
                "Recovery changed HP more than once, ignored the nonlethal floor, or spent another resource.");
            Check(player.IsFallProtected && !player.TakeDamage(1f).Applied
                && !stats.TakeDamage(1f, ElementType.ELEMENT_NONE, false).Applied,
                "Post-reveal grace did not protect both damage entry points.");

            double releasedAt = Time.realtimeSinceStartupAsDouble;
            await Wait(() => !player.IsFallProtected, "Recovery grace expiry");
            CheckDuration("Post-reveal grace", Time.realtimeSinceStartupAsDouble - releasedAt, .9, 1.6);
            Check(stats.TakeDamage(1f, ElementType.ELEMENT_NONE, false).ShieldDamage == 1f,
                "The expired grace period continued to block ordinary damage.");
        }
        finally
        {
            Time.timeScale = originalScale;
            stats.OnHpChanged -= OnHpChanged;
        }
    }

    private static void CheckDuration(string phase, double elapsed, double minimum, double maximum) =>
        Check(elapsed >= minimum && elapsed <= maximum,
            $"{phase} took {elapsed:F3} real seconds, expected between {minimum:F2} and {maximum:F2}.");

    private static Transform FindOpenPit(GameObject room, float localMin)
    {
        var physics = room.scene.GetPhysicsScene();
        foreach (var marker in room.GetComponentsInChildren<Transform>())
        {
            if (!marker.name.Contains("deadzone_pit-")) continue;
            Vector3 above = marker.position + Vector3.up * 4f;
            float depth = above.y - room.transform.TransformPoint(new Vector3(0f, localMin, 0f)).y + 1f;
            if (!physics.Raycast(above, Vector3.down, depth, ~0, QueryTriggerInteraction.Ignore)
                && !physics.SphereCast(above, .35f, Vector3.down, out _, depth, ~0, QueryTriggerInteraction.Ignore))
                return marker;
        }
        return null;
    }

    private static Image Cover(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == "FallRecoveryCover" && root.activeInHierarchy) return root.GetComponentInChildren<Image>();
        return null;
    }

    private static void Drop(PlayerController player, Vector3 position)
    {
        MoveFixture(player, position, player.transform.rotation);
        player.VerticalVelocity = -2f;
        player.StateMachine.ChangeState(new PlayerFallState(player));
    }

    private static void MoveFixture(PlayerController player, Vector3 position, Quaternion rotation)
    {
        player.Controller.enabled = false;
        player.transform.SetPositionAndRotation(position, rotation);
        player.Controller.enabled = true;
        Physics.SyncTransforms();
    }

    private static bool Near(Vector3 a, Vector3 b, float tolerance) => (a - b).sqrMagnitude <= tolerance * tolerance;
    private static bool ResolvesNear(DungeonFallRecovery service, Vector3 expected) =>
        service.TryGetRecoveryPose(out var position, out _) && Near(position, expected, .08f);

    private static async Task WaitForGameTime(float duration)
    {
        float finish = Time.time + duration;
        await Wait(() => Time.time >= finish, "Gameplay clock");
    }

    private static async Task Wait(Func<bool> condition, string operation)
    {
        while (!condition()) await NextTick(operation);
    }

    private static Task NextTick(string operation)
    {
        if (!Application.isPlaying || Time.realtimeSinceStartupAsDouble >= _deadline)
            throw new InvalidOperationException(operation + " did not finish within the Play Mode regression budget.");
        var completion = new TaskCompletionSource<bool>();
        void Tick() { EditorApplication.update -= Tick; completion.SetResult(true); }
        EditorApplication.update += Tick;
        return completion.Task;
    }

    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Hidden).GetValue(owner);
    private static void SetField(object owner, string name, object value) =>
        owner.GetType().GetField(name, Hidden).SetValue(owner, value);
    private static void SetProperty(object owner, string name, object value) =>
        owner.GetType().GetProperty(name, BindingFlags.Public | Hidden).SetValue(owner, value);
    private static void Invoke(object owner, string name, params object[] args) =>
        owner.GetType().GetMethod(name, Hidden).Invoke(owner, args);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    private sealed class RevealFixture : IDisposable
    {
        public readonly PlayerFallReveal Effect;
        public readonly MeshRenderer Body;
        private readonly Material _source, _hologram;
        private readonly MaterialPropertyBlock _block = new();

        public RevealFixture(Fixture fixture)
        {
            _source = AssetDatabase.LoadAssetAtPath<Material>(SourceMaterial);
            var shader = Shader.Find("AllIn13DShader/AllIn13DShader");
            Check(_source != null && shader != null, "The hologram regression needs the source sword material and AllIn13D shader.");
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mesh.name = "Fall regression body";
            SceneManager.MoveGameObjectToScene(mesh, fixture.Scene);
            mesh.transform.SetParent(fixture.Player.transform, false);
            mesh.GetComponent<Collider>().enabled = false;
            Body = mesh.GetComponent<MeshRenderer>();
            Body.sharedMaterials = new[] { _source, _source };
            _block.SetFloat(PreservedValue, .35f);
            Body.SetPropertyBlock(_block);
            _block.SetFloat(PreservedValue, .65f);
            Body.SetPropertyBlock(_block, 1);

            _hologram = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _hologram.EnableKeyword("_FADE_ON");
            _hologram.EnableKeyword("_HOLOGRAM_ON");
            _hologram.EnableKeyword("_GLITCH_ON");
            Effect = fixture.Player.gameObject.AddComponent<PlayerFallReveal>();
            using var serialized = new SerializedObject(Effect);
            var roots = serialized.FindProperty("_visualRoots");
            roots.arraySize = 1;
            roots.GetArrayElementAtIndex(0).objectReferenceValue = mesh.transform;
            var bindings = serialized.FindProperty("_materials");
            bindings.arraySize = 1;
            var binding = bindings.GetArrayElementAtIndex(0);
            binding.FindPropertyRelative("Source").objectReferenceValue = _source;
            binding.FindPropertyRelative("Reveal").objectReferenceValue = _hologram;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public bool HasCyanTransition
        {
            get
            {
                Body.GetPropertyBlock(_block, 0);
                float fade = _block.GetFloat(FadeAmount);
                Color tint = _block.GetColor(HologramColor);
                bool firstSlot = Effect.IsRevealing && !Body.forceRenderingOff && Body.sharedMaterial == _hologram
                    && fade is > .05f and < .95f && tint.b > tint.g && tint.g > tint.r
                    && _block.GetFloat(GlitchAmount) > 0f && Mathf.Approximately(_block.GetFloat(PreservedValue), .35f);
                Body.GetPropertyBlock(_block, 1);
                return firstSlot && Mathf.Approximately(_block.GetFloat(PreservedValue), .65f)
                    && Mathf.Approximately(_block.GetFloat(FadeAmount), fade);
            }
        }

        public bool IsRestored
        {
            get
            {
                if (Effect.IsRevealing || !Body.enabled || Body.forceRenderingOff) return false;
                var materials = Body.sharedMaterials;
                if (materials.Length != 2 || materials[0] != _source || materials[1] != _source) return false;
                Body.GetPropertyBlock(_block);
                if (!Mathf.Approximately(_block.GetFloat(PreservedValue), .35f)) return false;
                Body.GetPropertyBlock(_block, 0);
                if (!_block.isEmpty) return false;
                Body.GetPropertyBlock(_block, 1);
                return Mathf.Approximately(_block.GetFloat(PreservedValue), .65f) && !_block.HasFloat(FadeAmount);
            }
        }

        public void Dispose()
        {
            if (_hologram != null) UnityEngine.Object.Destroy(_hologram);
        }
    }

    private sealed class Fixture
    {
        public readonly Scene Scene;
        public readonly PlayerController Player;
        public readonly StageManager Stage;
        public readonly DungeonEncounterDirector Director;
        public readonly GameObject Root;
        private readonly BaekhoDungeonGenerator _generator;

        public Fixture(Scene scene, Vector3 origin)
        {
            Scene = scene;
            Root = Host("Generated dungeon fixture");
            Root.transform.SetPositionAndRotation(origin, Quaternion.Euler(0f, 90f, 0f));
            Player = Host("Fall recovery fixture player").AddComponent<PlayerController>();
            var controller = Player.GetComponent<CharacterController>();
            controller.height = 1.56f;
            controller.radius = .23f;
            controller.center = new Vector3(-.007f, -.215f, 0f);
            controller.skinWidth = .08f;
            controller.stepOffset = .45f;
            if (!Application.isPlaying)
            {
                SetProperty(Player, "Controller", controller);
                SetProperty(Player, "StatManager", Player.GetComponent<PlayerStatManager>());
            }

            var host = Host("Stage fixture");
            _generator = host.AddComponent<BaekhoDungeonGenerator>();
            _generator.generateOnStart = false;
            Director = host.AddComponent<DungeonEncounterDirector>();
            Stage = host.AddComponent<StageManager>();
            Stage.Configure(_generator, Director);
            SetField(Director, "_generator", _generator);
            SetField(Director, "_root", Root);
            SetField(Director, "_boundPlayer", Player);
            SetField(Director, "player", controller);
            SetField(Player, "_encounterDirector", Director);
        }

        public DungeonFallRecovery Prepare(Transform spawn, params BaekhoMapModule[] rooms)
        {
            var result = new BaekhoDungeonResult { success = true, root = Root,
                startRoom = rooms[0], endRoom = rooms[rooms.Length - 1] };
            result.rooms.AddRange(rooms);
            SetProperty(_generator, "CurrentRoot", Root);
            SetProperty(_generator, "LastResult", result);
            SetProperty(Director, "IsReady", true);
            SetProperty(Stage, "Result", result);
            SetProperty(Stage, "PlayerSpawnPosition", spawn.position);
            SetProperty(Stage, "PlayerSpawnRotation", spawn.rotation);
            SetField(Stage, "_player", Player);
            MoveFixture(Player, spawn.position, spawn.rotation);
            var service = new DungeonFallRecovery(Stage, Player);
            Check(service.TryPrepare(out string reason), "Could not prepare the recovery fixture: " + reason);
            SetProperty(Stage, "FallRecovery", service);
            SetProperty(Stage, "IsReady", true);
            return service;
        }

        public BaekhoMapModule Room(string name, Vector3 localPosition)
        {
            var room = Host(name, Root.transform).AddComponent<BaekhoMapModule>();
            room.transform.localPosition = localPosition;
            room.localBounds = new Bounds(Vector3.up * 2f, new Vector3(20f, 8f, 20f));
            var floor = Box("Room floor", room.transform, room.transform.position + Vector3.down * .25f,
                new Vector3(18f, .5f, 18f));
            floor.gameObject.layer = LayerMask.NameToLayer("Ground");
            room.groundObjects = new[] { floor.gameObject };
            var point = Host("TeleportPoint", room.transform).transform;
            point.SetPositionAndRotation(DungeonFastTravelLanding.PlayerRootAtFloor(room.transform.position,
                room.transform.rotation, Player.Controller), room.transform.rotation);
            var anchors = room.gameObject.AddComponent<StageMapAnchors>();
            anchors.teleportPoint = point;
            anchors.playerSpawn = point;
            return room;
        }

        public void Place(Vector3 floor) => MoveFixture(Player,
            DungeonFastTravelLanding.PlayerRootAtFloor(floor, Root.transform.rotation, Player.Controller),
            Root.transform.rotation);

        public GameObject Host(string name, Transform parent = null)
        {
            var host = new GameObject(name);
            SceneManager.MoveGameObjectToScene(host, Scene);
            if (parent != null) host.transform.SetParent(parent, false);
            return host;
        }

        public BoxCollider Box(string name, Transform parent, Vector3 position, Vector3 size)
        {
            var host = Host(name, parent);
            host.transform.position = position;
            var box = host.AddComponent<BoxCollider>();
            box.size = size;
            return box;
        }
    }
}

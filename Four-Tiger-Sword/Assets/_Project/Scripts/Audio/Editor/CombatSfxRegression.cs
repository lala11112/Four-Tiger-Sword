// Callable editor regressions. Run() is safe in Edit Mode; RunPlayMode() uses a temporary pool.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

public static class CombatSfxRegression
{
    private const BindingFlags StaticHidden = BindingFlags.Static | BindingFlags.NonPublic;
    private const BindingFlags InstanceHidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Vector3 TestPosition = new Vector3(123456f, 123456f, 123456f);

    private sealed class ProbeForm : BaseForm
    {
        public ProbeForm(WeaponActionDataSO data, PlayerController player = null) : base(data)
            => _playerController = player;
        public CombatSoundCue Hit(WeaponActionData step = null, HitEvent hit = null) => ResolveHitSound(step, hit);
        public CombatSoundCue Secondary() => ResolveSecondaryHitSound();
        public void Reset() => ResetAttackSounds();
        public void Tick(WeaponActionData step, float time) => UpdateSwingSounds(step, time);
    }

    private sealed class DamageProbe : IDamageable
    {
        public DamageResult Result;
        public int Calls;
        public Action OnDamage;
        public DamageResult TakeDamage(float damage, ElementType damageType = ElementType.ELEMENT_NONE,
            bool isCritical = false, Vector3 power = default, float poiseDamage = 20f,
            StaggerResistLevel staggerResistLevel = StaggerResistLevel.NONE, bool isParryable = true, object source = null)
        {
            Calls++;
            OnDamage?.Invoke();
            return Result;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Combat SFX: " + message);
    }

    private static CombatSoundSettings Sound(AudioClip clip)
        => new CombatSoundSettings { Clip = clip, PitchRange = Vector2.one };

    private static AudioClip Silent(string name) => AudioClip.Create(name, 44100, 1, 44100, false);

    public static string Run()
    {
        Check(!Application.isPlaying, "Run() requires Edit Mode; use RunPlayMode() while playing.");
        var randomState = UnityEngine.Random.state;
        var scene = EditorSceneManager.NewPreviewScene();
        var created = new List<Object>();
        try
        {
            AudioClip a = Silent("CombatSfx regression A"), b = Silent("CombatSfx regression B");
            created.Add(a); created.Add(b);
            var settings = Sound(a);
            settings.Accent = b;
            settings.Volume = 0.4f;
            CombatSoundCue snapshot = settings.Resolve();
            var hit = new HitInfo(10f, ElementType.ELEMENT_NONE, 0f, 1f, trueDamage: true, impactSound: snapshot);
            settings.Clip = b; settings.Accent = null; settings.Volume = 0.1f;
            Check(snapshot.Clip == a && snapshot.Accent == b && snapshot.Volume == 0.4f,
                "Resolved cues must survive later setting edits.");
            Check(hit.ImpactSound.Clip == a && hit.ImpactSound.Accent == b, "HitInfo lost its audio snapshot.");
            Check(new CombatSoundSettings().Resolve(a).Clip == a && !new CombatSoundSettings().HasSound,
                "Empty settings must retain legacy clip fallback without becoming an explicit override.");
            CombatSoundCue safe = new CombatSoundCue(a, volume: float.NaN, accentVolume: 2f,
                pitchRange: new Vector2(3f, 0.2f));
            Check(safe.Volume == 0f && safe.AccentVolume == 1f && safe.PitchRange == new Vector2(0.2f, 3f),
                "Invalid volume or reversed pitch bounds escaped cue validation.");

            var data = ScriptableObject.CreateInstance<FireFormActionDataSO>();
            created.Add(data);
            var form = new ProbeForm(data);
            var step = new WeaponActionData();
            var hitEvent = new HitEvent();
            data.HitSound = a;
            Check(form.Hit(step, hitEvent).Clip == a, "Form legacy hit fallback failed.");
            data.CommonFeedback.HitAudio = Sound(b);
            Check(form.Hit(step, hitEvent).Clip == b, "Form hit settings failed.");
            step.Feedback.HitAudio = Sound(a);
            Check(form.Hit(step, hitEvent).Clip == a, "Step hit override failed.");
            hitEvent.Feedback.HitAudio = Sound(b);
            Check(form.Hit(step, hitEvent).Clip == b, "Hit-event override failed.");
            hitEvent.Feedback.HitAudio.Clip = null;
            Check(form.Hit(step, hitEvent).Clip == a, "Empty event must inherit the step.");
            Check(form.Secondary().Clip == b, "Empty secondary hit must inherit form audio.");
            data.CommonFeedback.SecondaryHitAudio = Sound(a);
            Check(form.Secondary().Clip == a, "Secondary effect override failed.");

            int before = CombatSfx.PlayedCueCount, poolBefore = CombatSfx.PooledVoiceCount;
            CombatSfx.Play(snapshot, TestPosition);
            Check(CombatSfx.PlayedCueCount == before && CombatSfx.PooledVoiceCount == poolBefore,
                "Edit Mode must not allocate or play a runtime audio pool.");
            int forms = VerifyAssignedAssets();
            return "PASS: immutable cues / HitInfo snapshots / safe bounds / event-step-form-legacy inheritance / secondary effects / Edit Mode gate / "
                + forms + " assigned form assets and all enemy skills.";
        }
        finally
        {
            for (int i = created.Count - 1; i >= 0; i--) Object.DestroyImmediate(created[i]);
            EditorSceneManager.ClosePreviewScene(scene);
            UnityEngine.Random.state = randomState;
        }
    }

    private static int VerifyAssignedAssets()
    {
        var clips = new HashSet<AudioClip>();
        string[] forms = AssetDatabase.FindAssets("t:WeaponActionDataSO", new[] { "Assets/_Project/Scripts/FormActions" });
        Check(forms.Length >= 7, "Expected the seven existing form assets, including WaterFormLab variants.");
        foreach (string guid in forms)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var data = AssetDatabase.LoadAssetAtPath<WeaponActionDataSO>(path);
            var form = new ProbeForm(data);
            RequireCue(form.Hit(), path + " common hit", clips);
            RequireCue(form.Secondary(), path + " secondary hit", clips);
            var steps = new List<WeaponActionData>();
            if (data.ComboSteps != null) steps.AddRange(data.ComboSteps);
            if (data.SkillSteps != null) steps.AddRange(data.SkillSteps);
            if (data.UltimateSteps != null) steps.AddRange(data.UltimateSteps);
            steps.Add(data.AirAttackStep); steps.Add(data.CounterStep);
            if (data is WaterFormActionDataSO water) steps.Add(water.HeavyAttackStep);
            foreach (WeaponActionData step in steps)
            {
                if (step == null) continue;
                RequireCue(step.Feedback.SwingAudio.Resolve(step.SwingSound), path + " swing", clips);
                RequireCue(form.Hit(step), path + " hit", clips);
                if (step.HitEvents == null) continue;
                foreach (HitEvent hit in step.HitEvents)
                    if (hit != null) RequireCue(form.Hit(step, hit), path + " event hit", clips);
            }
        }
        string[] enemies = AssetDatabase.FindAssets("t:MonsterSkillData", new[] { "Assets/_Project" });
        Check(enemies.Length >= 11, "Enemy skill coverage is incomplete.");
        foreach (string guid in enemies)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var data = AssetDatabase.LoadAssetAtPath<MonsterSkillData>(path);
            RequireCue(data.SwingAudio.Resolve(), path + " swing", clips);
            RequireCue(data.HitAudio.Resolve(), path + " hit", clips);
            if (data is MonsterJumpSlamSO) RequireCue(data.LandingAudio.Resolve(), path + " landing", clips);
        }
        foreach (AudioClip clip in clips)
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)) as AudioImporter;
            Check(importer != null && importer.defaultSampleSettings.preloadAudioData,
                clip.name + " must preload for the first attack.");
        }
        return forms.Length;
    }

    private static void RequireCue(CombatSoundCue cue, string label, HashSet<AudioClip> clips)
    {
        Check(!cue.IsEmpty, label + " has no assigned sound.");
        if (cue.Clip != null) clips.Add(cue.Clip);
        if (cue.Accent != null) clips.Add(cue.Accent);
    }

    public static string RunPlayMode()
    {
        Check(Application.isPlaying, "RunPlayMode() requires Play Mode.");
        Check(Time.deltaTime > 0f, "RunPlayMode() requires an unpaused simulation frame.");
        var randomState = UnityEngine.Random.state;
        var created = new List<Object>();
        using (new IsolatedPool())
        {
            try
            {
                AudioClip a = Silent("CombatSfx runtime A"), b = Silent("CombatSfx runtime B");
                created.Add(a); created.Add(b);
                var root = new GameObject("CombatSfx regression target") { hideFlags = HideFlags.HideAndDontSave };
                root.transform.position = TestPosition;
                created.Add(root);
                VerifyDamageDispatch(root, a);
                var playerObject = new GameObject("CombatSfx regression inactive player");
                playerObject.SetActive(false);
                playerObject.transform.SetParent(root.transform, false);
                var player = playerObject.AddComponent<PlayerController>();
                var data = ScriptableObject.CreateInstance<FireFormActionDataSO>();
                created.Add(data);
                VerifyPlayerTiming(new ProbeForm(data, player), a, b);
                VerifyPlayerSpecialPaths(player, a, b, created);
                VerifyEnemyTiming(root, a, created);
                for (int i = 0; i < CombatSfx.Capacity * 4; i++)
                    CombatSfx.Play(new CombatSoundCue(a, b, pitchRange: Vector2.one), TestPosition);
                Check(CombatSfx.PooledVoiceCount == CombatSfx.Capacity
                    && CombatSfx.ActiveVoiceCount + CombatSfx.PendingVoiceCount <= CombatSfx.Capacity,
                    "A burst exceeded the 24-voice pool.");
                Check(CombatSfx.FailedLoadCount == 0, "Loaded test clips failed to play.");
                VerifyPausedCleanup(created);
                return "PASS: confirmed HP/shield/kill impacts / rejected, zero, blocked, parried silence / pre-callback impact position / same-frame impact dedup / player timed events and cancellation / Water ten missed strikes / Wood-Earth activation-only ultimates / Fire secondary snapshots / enemy default, combo, dash, jump, explosion hooks / bounded voice pool / real-time cleanup at timeScale zero.";
            }
            finally
            {
                for (int i = created.Count - 1; i >= 0; i--) Object.DestroyImmediate(created[i]);
                UnityEngine.Random.state = randomState;
            }
        }
    }

    private static void VerifyDamageDispatch(GameObject target, AudioClip clip)
    {
        var probe = new DamageProbe();
        foreach (DamageResult result in new[] {
            default(DamageResult), new DamageResult(DamageOutcome.Blocked), new DamageResult(DamageOutcome.Parried),
            new DamageResult(DamageOutcome.Applied), new DamageResult(DamageOutcome.Applied, 5f),
            new DamageResult(DamageOutcome.Applied, shieldDamage: 5f), new DamageResult(DamageOutcome.Applied, 5f, killed: true) })
        {
            probe.Result = result;
            int before = CombatSfx.PlayedCueCount;
            var hit = new HitInfo(5f, ElementType.ELEMENT_NONE, 0f, 1f, source: new object(), trueDamage: true,
                impactSound: new CombatSoundCue(clip, pitchRange: Vector2.one));
            DamageManager.Apply(hit, probe, target);
            int expected = result.Applied && result.HealthDamage + result.ShieldDamage > 0f ? 1 : 0;
            Check(CombatSfx.PlayedCueCount == before + expected, "Incorrect impact dispatch for " + result.Outcome + ".");
        }
        int callsBefore = probe.Calls, cuesBefore = CombatSfx.PlayedCueCount;
        DamageManager.Apply(new HitInfo(float.NaN, ElementType.ELEMENT_NONE, 0f, 1f, trueDamage: true,
            impactSound: new CombatSoundCue(clip)), probe, target);
        Check(probe.Calls == callsBefore && CombatSfx.PlayedCueCount == cuesBefore, "Invalid damage reached the target or audio.");
        var source = new object();
        var cue = new CombatSoundCue(clip, pitchRange: Vector2.one);
        var confirmed = new HitInfo(5f, ElementType.ELEMENT_NONE, 0f, 1f, source: source, trueDamage: true, impactSound: cue);
        probe.Result = new DamageResult(DamageOutcome.Applied, 5f);
        Vector3 position = target.transform.position;
        probe.OnDamage = () => target.transform.position += Vector3.right * 10f;
        cuesBefore = CombatSfx.PlayedCueCount;
        int suppressedBefore = CombatSfx.SuppressedImpactCount;
        DamageManager.Apply(confirmed, probe, target);
        CombatSfx pool = (CombatSfx)typeof(CombatSfx).GetField("instance", StaticHidden).GetValue(null);
        bool atImpact = false;
        foreach (AudioSource voice in pool.GetComponentsInChildren<AudioSource>())
            if (voice.clip == clip && voice.transform.position == position) atImpact = true;
        Check(atImpact, "Damage callbacks moved the impact sound away from the contact position.");
        DamageManager.Apply(confirmed, probe, target);
        Check(CombatSfx.PlayedCueCount == cuesBefore + 1 && CombatSfx.SuppressedImpactCount == suppressedBefore + 1,
            "One source/cue in the same frame must not multiply its impact voices.");
        probe.OnDamage = null;
    }

    private static void VerifyPlayerTiming(ProbeForm form, AudioClip a, AudioClip b)
    {
        var step = new WeaponActionData { Duration = 2f };
        step.Feedback.SwingAudio = Sound(a);
        step.HitEvents.Add(new HitEvent { StartTime = 0.2f, Duration = 0.1f });
        step.HitEvents.Add(new HitEvent { StartTime = 0.7f, Duration = 0.1f });
        step.HitEvents[1].Feedback.SwingAudio = Sound(b);
        form.Reset();
        int before = CombatSfx.PlayedCueCount;
        form.Tick(step, 0.1f);
        Check(CombatSfx.PlayedCueCount == before, "Player swing played during windup.");
        form.Tick(step, 1f); form.Tick(step, 1.2f);
        Check(CombatSfx.PlayedCueCount == before + 2, "Skipped/repeated frames lost or duplicated player events.");
        form.Reset(); form.Tick(step, 0.1f); form.Reset(); form.Tick(step, 0.1f);
        Check(CombatSfx.PlayedCueCount == before + 2, "Canceled windup leaked a swing.");
        step.HitEvents.Clear(); step.HitStartTime = 0f; step.HitDuration = 0.1f;
        form.Reset(); form.Tick(step, 0.01f); form.Tick(step, 0.05f);
        Check(CombatSfx.PlayedCueCount == before + 3, "A zero-time single hit must swing exactly once.");
    }

    private static void VerifyPlayerSpecialPaths(PlayerController player, AudioClip a, AudioClip b, List<Object> created)
    {
        // Keep the player inactive: these paths need a stat snapshot, not input, navigation or a live state machine.
        var stats = player.GetComponent<PlayerStatManager>();
        typeof(PlayerController).GetProperty(nameof(PlayerController.StatManager)).SetValue(player, stats);
        var values = (Dictionary<StatType, float>)typeof(PlayerStatManager)
            .GetField("finalValues", InstanceHidden).GetValue(stats);
        values[StatType.ST_ATK] = 10f;
        values[StatType.ST_CRT] = 0f;
        values[StatType.ST_CRTD] = 1f;
        values[StatType.ST_ATK_SPD] = 1f;

        var step = new WeaponActionData { Duration = 2.1f };
        step.Feedback.SwingAudio = Sound(a);
        for (int i = 0; i < 10; i++)
        {
            var hit = new HitEvent { StartTime = (i + 1) * 0.2f, Duration = 0.05f };
            if (i % 2 != 0) hit.Feedback.SwingAudio = Sound(b);
            step.HitEvents.Add(hit);
        }
        var waterData = ScriptableObject.CreateInstance<WaterFormActionDataSO>(); created.Add(waterData);
        waterData.UltimateSteps = new List<WeaponActionData> { step };
        waterData.AreaRadius = 0.1f;
        var water = new WaterForm(waterData);
        BindPlayer(water, player);
        Vector3 center = TestPosition + Vector3.up * 1000f;
        foreach (var unused in FormEffectRunner.Targets(center, waterData.AreaRadius))
            Check(false, "The missed-strike fixture unexpectedly overlaps an enemy.");
        typeof(WaterForm).GetField("_ultimateCenter", InstanceHidden).SetValue(water, center);
        typeof(WaterForm).GetField("_ultimateElapsed", InstanceHidden).SetValue(water, 2.1f);
        int before = CombatSfx.PlayedCueCount;
        water.UpdateUltimate(out bool complete);
        Check(complete && CombatSfx.PlayedCueCount == before + 10,
            "Water's fixed ten-strike ultimate must swing ten times even when every strike misses.");
        water.UpdateUltimate(out complete);
        Check(complete && CombatSfx.PlayedCueCount == before + 10,
            "Completed Water ultimate replayed its strike sounds.");

        var woodData = ScriptableObject.CreateInstance<WoodFormActionDataSO>(); created.Add(woodData);
        woodData.UltimateSteps = new List<WeaponActionData> { step };
        woodData.ElementBuffDuration = 0f;
        woodData.UltimateFieldDuration = 0f;
        var wood = new WoodForm(woodData);
        BindPlayer(wood, player);
        before = CombatSfx.PlayedCueCount;
        wood.BeginUltimate(); wood.UpdateUltimate(out complete); wood.UpdateUltimate(out complete);
        Check(complete && CombatSfx.PlayedCueCount == before + 1,
            "Wood's activation-only ultimate must not replay the configured attack events.");

        var earthData = ScriptableObject.CreateInstance<EarthFormActionDataSO>(); created.Add(earthData);
        earthData.UltimateSteps = new List<WeaponActionData> { step };
        var earth = new EarthForm(earthData);
        BindPlayer(earth, player);
        before = CombatSfx.PlayedCueCount;
        earth.BeginUltimate(); earth.UpdateUltimate(out complete); earth.UpdateUltimate(out complete);
        Check(complete && CombatSfx.PlayedCueCount == before + 1,
            "Earth's activation-only ultimate must not replay the configured attack events.");

        var fireData = ScriptableObject.CreateInstance<FireFormActionDataSO>(); created.Add(fireData);
        fireData.CommonFeedback.HitAudio = Sound(a);
        fireData.CommonFeedback.SecondaryHitAudio = Sound(b);
        var fire = new FireForm(fireData);
        BindPlayer(fire, player);
        var snapshot = (HitInfo)typeof(BaseForm).GetMethod("EffectHit", InstanceHidden)
            .Invoke(fire, new object[] { 2.5f, 0f, false, null, null, true });
        fireData.CommonFeedback.SecondaryHitAudio.Clip = a;
        Check(snapshot.ImpactSound.Clip == b && snapshot.Element == ElementType.ELEMENT_FIRE
            && ReferenceEquals(snapshot.Source, player),
            "Fire's delayed stack explosion must capture its secondary audio independently of future form/settings edits.");
    }

    private static void BindPlayer(BaseForm form, PlayerController player)
        => typeof(BaseForm).GetField("_playerController", InstanceHidden).SetValue(form, player);

    private static void VerifyPausedCleanup(List<Object> created)
    {
        var pool = (CombatSfx)typeof(CombatSfx).GetField("instance", StaticHidden).GetValue(null);
        typeof(CombatSfx).GetMethod("ClearVoices", InstanceHidden).Invoke(pool, null);
        AudioClip shortClip = AudioClip.Create("CombatSfx paused cleanup", 441, 1, 44100, false);
        created.Add(shortClip);
        float previousTimeScale = Time.timeScale;
        try
        {
            Time.timeScale = 0f;
            int before = CombatSfx.PlayedVoiceCount;
            CombatSfx.Play(new CombatSoundCue(shortClip, pitchRange: Vector2.one), TestPosition);
            Check(CombatSfx.PlayedVoiceCount == before + 1 && CombatSfx.ActiveVoiceCount == 1,
                "Hitstop must not prevent a loaded combat sound from starting.");
            foreach (AudioSource voice in pool.GetComponentsInChildren<AudioSource>())
                if (voice.clip == shortClip)
                    Check(voice.pitch == 1f && voice.ignoreListenerPause,
                        "The combat voice inherited paused simulation pitch or listener pause.");
            // The synchronous tool entry cannot yield a frame; use a bounded wall wait, then the real runtime clock.
            System.Threading.Thread.Sleep(180);
            typeof(CombatSfx).GetMethod("Update", InstanceHidden).Invoke(pool, null);
            Check(CombatSfx.ActiveVoiceCount == 0 && CombatSfx.PendingVoiceCount == 0,
                "Expired voices were retained while simulation time was stopped.");
        }
        finally
        {
            Time.timeScale = previousTimeScale;
            typeof(CombatSfx).GetMethod("ClearVoices", InstanceHidden).Invoke(pool, null);
        }
    }

    private static void VerifyEnemyTiming(GameObject root, AudioClip clip, List<Object> created)
    {
        var go = new GameObject("CombatSfx regression inactive enemy");
        go.SetActive(false); go.transform.SetParent(root.transform, false);
        var enemy = go.AddComponent<Enemy>();
        go.AddComponent<NavMeshAgent>().enabled = false;
        var single = ScriptableObject.CreateInstance<MonsterDefaultAttackSO>(); created.Add(single);
        single.isParryable = false; single.SwingAudio = Sound(clip);
        single.hitStartTime = 0.2f; single.hitDuration = 0.1f; single.attackDuration = 2f;
        single.attackSpeed = 0.8f / Time.deltaTime;
        var action = single.CreateAction(enemy);
        int before = CombatSfx.PlayedCueCount;
        action.Enter(); action.Update(); action.Update(); action.Exit();
        Check(CombatSfx.PlayedCueCount == before + 1, "Enemy single hit lost/duplicated a skipped-window swing.");
        var combo = ScriptableObject.CreateInstance<MonsterComboAttackSO>(); created.Add(combo);
        combo.isParryable = false; combo.SwingAudio = Sound(clip); combo.attackDuration = 2f;
        combo.attackSpeed = 1f / Time.deltaTime;
        action = combo.CreateAction(enemy); action.Enter(); action.Update(); action.Update(); action.Exit();
        Check(CombatSfx.PlayedCueCount == before + 4, "Enemy combo must swing once per hit event.");
        var dash = ScriptableObject.CreateInstance<MonsterDashAttackSO>(); created.Add(dash);
        dash.isParryable = false; dash.SwingAudio = Sound(clip); dash.startupTime = 0.1f;
        dash.attackSpeed = 1f / Time.deltaTime; dash.dashSpeed = 0f;
        action = dash.CreateAction(enemy); action.Enter(); action.Update(); action.Update(); action.Exit();
        Check(CombatSfx.PlayedCueCount == before + 5, "Dash swing must fire once at the phase transition.");
        var jump = ScriptableObject.CreateInstance<MonsterJumpSlamSO>(); created.Add(jump);
        jump.isParryable = false; jump.SwingAudio = Sound(clip); jump.startupTime = 0.1f;
        jump.attackSpeed = 1f / Time.deltaTime;
        action = jump.CreateAction(enemy); action.Enter(); action.Update();
        // Navigation restoration needs a baked NavMesh; only the independent launch cue is exercised here.
        Check(CombatSfx.PlayedCueCount == before + 6, "Jump launch sound must fire before a landing/hit exists.");
        var explosion = ScriptableObject.CreateInstance<MonsterSelfDestructSO>(); created.Add(explosion);
        explosion.isParryable = false; explosion.SwingAudio = Sound(clip); explosion.explosionDelay = 0f;
        action = explosion.CreateAction(enemy); action.Enter(); action.Update(); action.Update(); action.Exit();
        Check(CombatSfx.PlayedCueCount == before + 7, "A missed explosion must sound exactly once.");
    }

    // Preserve the live pool, voices, dedup state, pitch RNG and counters throughout synchronous tests.
    private sealed class IsolatedPool : IDisposable
    {
        private readonly Dictionary<FieldInfo, object> saved = new Dictionary<FieldInfo, object>();
        private readonly List<object> impacts = new List<object>();
        private readonly GameObject root;
        public IsolatedPool()
        {
            foreach (FieldInfo field in typeof(CombatSfx).GetFields(StaticHidden))
            {
                if (!field.IsLiteral && !field.IsInitOnly) saved.Add(field, field.GetValue(null));
            }
            var impactSet = typeof(CombatSfx).GetField("frameImpacts", StaticHidden).GetValue(null);
            foreach (object key in (IEnumerable)impactSet) impacts.Add(key);
            impactSet.GetType().GetMethod("Clear").Invoke(impactSet, null);
            typeof(CombatSfx).GetField("instance", StaticHidden).SetValue(null, null);
            typeof(CombatSfx).GetField("pitchRandom", StaticHidden).SetValue(null, new System.Random(0));
            root = new GameObject("CombatSfx regression isolated pool");
            root.AddComponent<CombatSfx>();
        }
        public void Dispose()
        {
            Object.DestroyImmediate(root);
            foreach (var item in saved) item.Key.SetValue(null, item.Value);
            var impactSet = typeof(CombatSfx).GetField("frameImpacts", StaticHidden).GetValue(null);
            impactSet.GetType().GetMethod("Clear").Invoke(impactSet, null);
            foreach (object key in impacts) impactSet.GetType().GetMethod("Add").Invoke(impactSet, new[] { key });
        }
    }
}

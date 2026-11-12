using System;
using System.Collections.Generic;
using System.Reflection;
using Baekho.Generated.a3355a1c8240244a995e4f671b0dc48bb;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

/// <summary>Exercises the attack warning with the real damage text prefab and attack actions.</summary>
public static class EnemyAttackWarningRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Vector3 Origin = new Vector3(10100f, 0f, 10100f);
    private static readonly Color WarningYellow = new Color(1f, 1f, 0f, 1f);

    public static string Run()
    {
        bool playing = Application.isPlaying;
        if (EditorApplication.isPlayingOrWillChangePlaymode && !playing)
            throw new InvalidOperationException("Wait for Unity to finish entering Play Mode.");
        using var fixture = new Fixture();
        fixture.ExercisePresentation();
        fixture.ExerciseGenericHead();
        fixture.ExerciseDash();
        if (playing) fixture.ExerciseRuntimeAttacks();
        fixture.ExerciseRestart();
        return $"PASS: attack warning in {(playing ? "Play" : "Edit")} Mode ({fixture.Checks} checks).";
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DamageTextManager _previousManager = DamageTextManager.Instance;
        private readonly UnityEngine.Random.State _previousRandom = UnityEngine.Random.state;
        private readonly List<Object> _data = new();
        private readonly GameObject _root = new GameObject("__EnemyAttackWarningRegression")
            { hideFlags = HideFlags.HideAndDontSave };
        private readonly GameObject _prefab;
        private readonly Camera _camera;
        private DamageTextManager _manager;
        private Canvas _canvas;
        private NavMeshDataInstance _navigation;
        private int _enemyCount;
        public int Checks { get; private set; }

        public Fixture()
        {
            try
            {
                _prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DemageUI.prefab");
                Check(_prefab != null && _prefab.TryGetComponent<DamageText>(out _), "Damage text prefab is missing.");
                _camera = Child("Camera").AddComponent<Camera>();
                _camera.transform.position = Origin + new Vector3(0f, 2f, -10f);
                StartManager();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void ExercisePresentation()
        {
            var skill = Skill<MonsterDefaultAttackSO>();
            var first = Actor<Enemy>();
            var second = Actor<Enemy>();
            var firstAction = Start(skill, first);
            var secondAction = Start(skill, second);
            var firstLabel = Label(firstAction);
            var secondLabel = Label(secondAction);
            var style = _prefab.GetComponent<DamageText>().textMesh;
            Check(firstLabel != secondLabel && Warnings.Count == 2, "Simultaneous enemies shared a label.");
            Check(firstLabel.textMesh.text == "!" && firstLabel.textMesh.color == WarningYellow
                && Mathf.Approximately(firstLabel.textMesh.fontSize, 54f), "Warning glyph, color or size is incorrect.");
            Check(firstLabel.textMesh.font == style.font && firstLabel.textMesh.fontSharedMaterial == style.fontSharedMaterial
                && !firstLabel.textMesh.raycastTarget, "Warning did not use the noninteractive damage text style.");
            _manager.ShowAttackWarning(firstAction);
            Check(Label(firstAction) == firstLabel && Warnings.Count == 2, "Showing the same action duplicated the warning.");

            var collider = first.GetComponent<CapsuleCollider>();
            first.transform.position += new Vector3(2f, .5f, 1f);
            Physics.SyncTransforms();
            CheckWarningPosition(firstLabel, ColliderTop(collider), "Warning did not stay above the moving collider.");

            _canvas.gameObject.SetActive(false);
            Check(firstAction.IsAttackWarningActive && Warnings.TryGetValue(firstAction, out var hiddenLabel)
                && hiddenLabel == firstLabel && !firstLabel.gameObject.activeInHierarchy,
                "Hiding the canvas cleared a warning for an attack still in preparation.");
            first.transform.position += Vector3.up;
            Physics.SyncTransforms();
            _canvas.gameObject.SetActive(true);
            CheckWarningPosition(firstLabel, ColliderTop(collider), "Reactivated canvas did not restore warning tracking.");

            first.CancelCurrentAction();
            CheckHidden(firstAction, "Cancellation");
            Check(Label(secondAction) == secondLabel && !firstLabel.gameObject.activeSelf,
                "Cancelling one attack affected another enemy's warning.");
            second.gameObject.SetActive(false);
            if (!Application.isPlaying) Invoke(typeof(Enemy), second, "OnDisable");
            CheckHidden(secondAction, "Disable");

            skill.isParryable = true;
            var parried = Start(skill, first);
            Check(ParryEventBus.IsTelegraphActive(parried), "Warning changed the existing parry telegraph.");
            parried.OnParried();
            CheckHidden(parried, "Parry");
            Check(!ParryEventBus.IsTelegraphActive(parried), "Parry did not clear its existing telegraph.");

            skill.isParryable = false;
            var dying = Start(skill, first);
            Invoke(typeof(Enemy), first, "Die");
            Check(first.IsDead, "Death fixture did not reach the death path.");
            CheckHidden(dying, "Death");
        }

        public void ExerciseGenericHead()
        {
            var actor = Actor<Enemy>();
            var rig = Child("Generic rig");
            rig.transform.SetParent(actor.transform, false);
            var animator = rig.AddComponent<Animator>();
            var head = Child("Rig:Head").transform;
            head.SetParent(rig.transform, false);
            head.localPosition = new Vector3(.25f, 3.5f, .2f);

            var action = Start(Skill<MonsterDefaultAttackSO>(), actor);
            var animatorProperty = typeof(Enemy).GetProperty(nameof(Enemy.Animator));
            animatorProperty.SetValue(actor, animator);
            try
            {
                _manager.HideAttackWarning(action);
                _manager.ShowAttackWarning(action);
                var label = Label(action);
                CheckWarningPosition(label, head.position, "Generic rig warning did not prefer the head over its body collider.");
                head.localPosition += new Vector3(.2f, -.35f, .15f);
                CheckWarningPosition(label, head.position, "Warning did not follow the generic rig head movement.");
            }
            finally
            {
                animatorProperty.SetValue(actor, null);
                actor.CancelCurrentAction();
            }
        }

        public void ExerciseDash()
        {
            var skill = Skill<MonsterDashAttackSO>();
            var actor = Actor<Enemy>();
            var action = Start(skill, actor);
            Label(action);
            SetTimer(action, skill.startupTime);
            action.Update();
            CheckHidden(action, "Dash entering its hit phase");
            actor.CancelCurrentAction();
        }

        public void ExerciseRuntimeAttacks()
        {
            Check(Time.deltaTime > 0f, "Run the play-mode checks while game time is advancing.");
            var actor = Actor<Enemy>();
            var melee = Skill<MonsterDefaultAttackSO>();
            var action = Start(melee, actor);
            var warning = Label(action);
            Check(!ParryEventBus.IsTelegraphActive(action), "Non-parryable melee opened a parry telegraph.");
            TickAcross(action, melee.hitStartTime);
            CheckHidden(action, "Missed melee hit window");
            actor.CancelCurrentAction();

            _manager.ShowDamageText(12f, actor.transform.position, ElementType.ELEMENT_FIRE);
            Check(warning.gameObject.activeSelf && warning.textMesh.text == "12"
                && Mathf.Approximately(warning.textMesh.fontSize, warning.normalFontSize)
                && Mathf.Approximately(warning.textMesh.color.g, .35f) && warning.textMesh.color.b == 0f,
                "The recycled label retained the warning appearance for a damage number.");
            Vector3 damagePosition = Field<Vector3>(typeof(DamageText), warning, "_worldPosition");
            Check(warning.textMesh.color.a < 1f && damagePosition.y < actor.transform.position.y + 1.5f,
                "The recycled damage number did not move or fade.");
            actor.transform.position += Vector3.up;
            Invoke(typeof(DamageText), warning, "LateUpdate");
            Check(Field<Vector3>(typeof(DamageText), warning, "_worldPosition") == damagePosition,
                "The recycled damage number still followed the warning owner.");

            var combo = Skill<MonsterComboAttackSO>();
            action = Start(combo, actor);
            Label(action);
            TickAcross(action, combo.hits[0].hitStartTime);
            CheckHidden(action, "First combo hit window");
            TickAcross(action, combo.hits[1].hitStartTime);
            CheckHidden(action, "Second combo hit window");
            actor.CancelCurrentAction();

            var ranged = Skill<MonsterTripleSphereBurstSO>();
            ranged.shotInterval = 2f;
            action = Start(ranged, actor);
            Label(action);
            TickAcross(action, ranged.startupTime);
            CheckHidden(action, "First ranged launch");
            TickAcross(action, ranged.startupTime + ranged.shotInterval);
            CheckHidden(action, "Second ranged launch");
            actor.CancelCurrentAction();

            var explosive = Skill<MonsterSelfDestructSO>();
            explosive.explosionDelay = 0f;
            action = Start(explosive, actor);
            Label(action);
            action.Update();
            CheckHidden(action, "Self-destruct explosion");
            actor.CancelCurrentAction();

            var boss = Actor<ConfiguredBoss>();
            var burst = Skill<BossPhaseBurstSkillSO>();
            var phaseAction = (BossPhaseBurstAction)Start(burst, boss);
            Check(phaseAction.WarningStarted, "Grounded boss burst did not begin charging.");
            Label(phaseAction);
            TickAcross(phaseAction, burst.chargeDuration);
            Check(phaseAction.BurstPlayed, "Boss burst did not reach its damage window.");
            CheckHidden(phaseAction, "Boss burst damage window");
            boss.CancelCurrentAction();

            ExerciseNavigationAttacks();
        }

        private void ExerciseNavigationAttacks()
        {
            Vector3 floor = Origin + Vector3.right * 60f;
            var source = new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
                transform = Matrix4x4.Translate(floor + Vector3.down * .25f), size = new Vector3(36f, .5f, 36f), area = 0 };
            var navigationData = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0),
                new List<NavMeshBuildSource> { source }, new Bounds(floor, new Vector3(38f, 6f, 38f)),
                Vector3.zero, Quaternion.identity);
            Check(navigationData != null, "Could not build isolated navigation for the landing test.");
            _data.Add(navigationData);
            _navigation = NavMesh.AddNavMeshData(navigationData);

            var jumper = Actor<Enemy>(floor - Vector3.right * 8f);
            var jump = Skill<MonsterJumpSlamSO>();
            var action = Start(jump, jumper);
            Label(action);
            TickAcross(action, jump.startupTime);
            Label(action);
            TickAcross(action, jump.jumpDuration);
            CheckHidden(action, "Jump slam landing");
            jumper.CancelCurrentAction();

            var boss = Actor<ConfiguredBoss>(floor + Vector3.right * 8f);
            var target = Child("Mobility target");
            target.transform.position = boss.transform.position + Vector3.forward * 3f;
            boss.SetCombatTarget(target.transform);
            Physics.SyncTransforms();
            var escape = Skill<BossMobilitySkillSO>();
            escape.kind = BossMobilityKind.EscapeJump;
            var move = (BossMobilityAction)Start(escape, boss);
            Check(move.HasStarted && move.IsActive, "Boss escape movement did not start.");
            CheckHidden(move, "Movement-only boss escape");
            boss.CancelCurrentAction();
        }

        public void ExerciseRestart()
        {
            var actor = Actor<Enemy>();
            var action = Start(Skill<MonsterDefaultAttackSO>(), actor);
            var label = Label(action);
            var oldWarnings = Warnings;
            StopManager();
            Check(label == null && oldWarnings.Count == 0 && DamageTextManager.Instance == null,
                "Scene teardown retained a warning or its manager.");
            actor.CancelCurrentAction();
            StartManager();
            action = Start(action.SkillData, actor);
            Check(Label(action).transform.parent == _canvas.transform && !ReferenceEquals(oldWarnings, Warnings),
                "Restart attached a warning to the previous scene.");
        }

        private GameObject Child(string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(_root.transform, false);
            return child;
        }

        private T Actor<T>(Vector3? onNavMesh = null) where T : Enemy
        {
            var body = Child(typeof(T).Name);
            body.SetActive(false);
            body.transform.position = onNavMesh ?? Origin + Vector3.right * (3f * _enemyCount++);
            var collider = body.AddComponent<CapsuleCollider>();
            collider.height = 2.6f;
            collider.center = Vector3.up * 1.3f;
            body.AddComponent<NavMeshAgent>().enabled = onNavMesh.HasValue;
            var actor = body.AddComponent<T>();
            actor.EnemyStat = body.GetComponent<EnemyStat>();
            body.SetActive(true);
            actor.EnemyStat.CurrentHp = 100f;
            Physics.SyncTransforms();
            return actor;
        }

        private T Skill<T>() where T : MonsterSkillData
        {
            var skill = ScriptableObject.CreateInstance<T>();
            skill.isParryable = false;
            _data.Add(skill);
            return skill;
        }

        private EnemyAction Start(MonsterSkillData skill, Enemy owner)
        {
            var action = skill.CreateAction(owner);
            Check(action != null, $"Could not create {skill.GetType().Name}.");
            owner.CurrentAction = action;
            action.Enter();
            return action;
        }

        private void StartManager()
        {
            DamageTextManager.Instance = null;
            var canvas = new GameObject("Damage text overlay", typeof(RectTransform), typeof(Canvas));
            canvas.transform.SetParent(_root.transform, false);
            canvas.transform.localScale = Vector3.one * .02f;
            _canvas = canvas.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.worldCamera = _camera;
            var host = Child("Damage text manager");
            host.SetActive(false);
            _manager = host.AddComponent<DamageTextManager>();
            typeof(DamageTextManager).GetField("damageTextPrefab", Hidden).SetValue(_manager, _prefab);
            typeof(DamageTextManager).GetField("worldSpaceCanvas", Hidden).SetValue(_manager, _canvas);
            host.SetActive(true);
            if (!Application.isPlaying) Invoke(typeof(DamageTextManager), _manager, "Awake");
        }

        private void StopManager()
        {
            // Scene unload may remove the canvas before the manager sees OnDestroy.
            if (_canvas != null) Object.DestroyImmediate(_canvas.gameObject);
            if (_manager == null) return;
            if (!Application.isPlaying) Invoke(typeof(DamageTextManager), _manager, "OnDestroy");
            Object.DestroyImmediate(_manager.gameObject);
        }

        public void Dispose()
        {
            foreach (var actor in _root.GetComponentsInChildren<Enemy>(true)) actor.CancelCurrentAction();
            StopManager();
            Object.DestroyImmediate(_root);
            if (_navigation.valid) _navigation.Remove();
            foreach (var data in _data) Object.DestroyImmediate(data);
            DamageTextManager.Instance = _previousManager;
            UnityEngine.Random.state = _previousRandom;
        }

        private Dictionary<EnemyAction, DamageText> Warnings =>
            Field<Dictionary<EnemyAction, DamageText>>(typeof(DamageTextManager), _manager, "_attackWarnings");

        private DamageText Label(EnemyAction action)
        {
            Warnings.TryGetValue(action, out var label);
            Check(action.IsAttackWarningActive && label != null && label.gameObject.activeSelf,
                $"{action.GetType().Name} did not show a warning in preparation.");
            return label;
        }

        private void CheckHidden(EnemyAction action, string stage)
            => Check(!action.IsAttackWarningActive && !Warnings.ContainsKey(action), $"{stage} retained a warning.");

        private void CheckWarningPosition(DamageText label, Vector3 anchor, string message)
        {
            Invoke(typeof(DamageText), label, "LateUpdate");
            Vector3 position = Field<Vector3>(typeof(DamageText), label, "_worldPosition");
            Check(Vector3.Distance(position, anchor + Vector3.up * .35f) < .01f
                && label.gameObject.activeInHierarchy && label.textMesh.enabled && label.textMesh.color == WarningYellow, message);
        }

        private static Vector3 ColliderTop(Collider collider)
        {
            var bounds = collider.bounds;
            return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        }

        private void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Attack warning: " + message);
            Checks++;
        }
    }

    private static void TickAcross(EnemyAction action, float boundary)
    {
        SetTimer(action, boundary - .5f * action.Owner.ActionDeltaTime * action.SkillData.attackSpeed);
        action.Update();
    }

    private static void SetTimer(EnemyAction action, float time)
        => typeof(EnemyAction).GetField("_timer", Hidden).SetValue(action, time);

    private static T Field<T>(Type type, object target, string name)
        => (T)type.GetField(name, Hidden).GetValue(target);

    private static void Invoke(Type type, object target, string name)
        => type.GetMethod(name, Hidden).Invoke(target, null);
}

using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Exercises spawn presentation and combat gates without changing scene or material assets.</summary>
public static class EnemySpawnRevealRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly int FadeAmount = Shader.PropertyToID("_FadeAmount");
    private static readonly int HologramColor = Shader.PropertyToID("_HologramColor");
    private static readonly int GlitchAmount = Shader.PropertyToID("_GlitchAmount");
    private static readonly int HitBlend = Shader.PropertyToID("_HitBlend");
    private const int CaptureSize = 480;

    [MenuItem("Tools/Studio Baekho/Enemies/Run Spawn Reveal Regression")]
    private static void RunMenu() => Debug.Log(Run());

    [MenuItem("Tools/Studio Baekho/Enemies/Capture Spawn Reveal Previews")]
    private static void CaptureMenu() => Debug.Log(CaptureCatalog());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run the spawn reveal regression in Edit Mode.");

        ValidateCatalog();
        var shader = Shader.Find("AllIn13DShader/AllIn13DShader");
        Check(shader != null, "AllIn1 shader is missing.");
        var living = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        var effect = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        effect.EnableKeyword("_FADE_ON");
        effect.EnableKeyword("_GLITCH_ON");
        effect.EnableKeyword("_HOLOGRAM_ON");
        var originalTint = new Color(.1f, .5f, 2f, 1f);
        effect.SetColor(HologramColor, originalTint);
        effect.SetFloat(FadeAmount, 0f);

        Scene previous = SceneManager.GetActiveScene();
        Scene isolated = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(isolated);
            var enemy = CreateEnemy<Enemy>(living, effect, out var renderer, out var fade);
            CreateEnemy<Enemy>(living, effect, out var otherRenderer, out _);
            var global = new MaterialPropertyBlock();
            global.SetFloat(HitBlend, .35f);
            renderer.SetPropertyBlock(global);
            var local = new MaterialPropertyBlock();
            local.SetFloat(HitBlend, .6f);
            renderer.SetPropertyBlock(local, 1);

            Invoke(typeof(Enemy), enemy, "OnEnable");
            Check(enemy.IsSpawning && !enemy.CanAttack && renderer.sharedMaterial == effect
                && Read(renderer, 0).GetFloat(FadeAmount) == 1f,
                "First activation did not hide the actor and close its attack gate before Start.");
            Invoke(typeof(Enemy), enemy, "Start");
            Check(otherRenderer.sharedMaterial == living && Read(otherRenderer, 0).isEmpty,
                "Spawn materials or property overrides leaked to a second instance.");
            var poise = enemy.GetComponent<PoiseHandler>();
            var status = enemy.GetComponent<StatusEffectHandler>();
            Check(!enemy.TakeDamage(10f).Applied && enemy.EnemyStat.CurrentHp == 100f
                && !enemy.IsHurt && !enemy.IsKnockbacking, "Spawn accepted damage or a reaction.");
            poise.TakePoiseDamage(100f);
            poise.ForceBreak();
            Check(poise.PoiseRatio == 1f && !enemy.PendingGroggy, "Spawn accepted direct poise damage.");
            Check(!status.TryApply(new SilenceEffect(1f)) && !status.Has<SilenceEffect>(),
                "Spawn accepted a status effect.");

            Tick(fade, .35f);
            var mid = Read(renderer, 0);
            var red = mid.GetColor(HologramColor);
            Check(mid.GetFloat(FadeAmount) is > 0f and < 1f && red.r > red.g && red.r > red.b
                && mid.GetFloat(GlitchAmount) > 0f, "Spawn did not reveal with a red glitch.");
            Check(Mathf.Approximately(mid.GetFloat(HitBlend), .35f)
                && Mathf.Approximately(Read(renderer, 1).GetFloat(HitBlend), .6f),
                "Spawn discarded an existing renderer or slot override.");
            Check(effect.GetColor(HologramColor) == originalTint && effect.GetFloat(FadeAmount) == 0f,
                "Spawn changed the shared death material.");

            Tick(fade, .64f);
            Check(enemy.IsSpawning && Read(renderer, 0).GetFloat(GlitchAmount) < mid.GetFloat(GlitchAmount),
                "Spawn ended before one second or failed to settle the glitch.");
            Tick(fade, .02f);
            AssertRestored(enemy, renderer, living);
            Check(enemy.CanAttack && enemy.TakeDamage(10f, poiseDamage: 0f).Applied
                && status.TryApply(new SilenceEffect(1f)), "Combat did not resume after the reveal.");

            var disabled = CreateEnemy<Enemy>(living, effect, out var disabledRenderer, out _);
            Activate(disabled);
            Invoke(typeof(Enemy), disabled, "OnDisable");
            Invoke(typeof(Enemy), disabled, "OnEnable");
            Check(!disabled.IsSpawning && disabledRenderer.sharedMaterial == living
                && Read(disabledRenderer, 0).isEmpty, "Disable left the actor phased or replayed the reveal.");
            var interrupted = CreateEnemy<Enemy>(living, effect, out var deathRenderer, out var deathFade);
            Activate(interrupted);
            deathFade.BeginFade();
            Check(!interrupted.IsSpawning && deathRenderer.sharedMaterial == living
                && Read(deathRenderer, 0).isEmpty, "Death did not restore the interrupted spawn before its delay.");
            Tick(deathFade, .6f);
            var death = Read(deathRenderer, 0);
            Check(deathRenderer.sharedMaterial == effect && death.GetFloat(FadeAmount) > 0f
                && !death.HasColor(HologramColor), "Death retained spawn color or failed to fade forward.");

            var bare = CreateEnemy<Enemy>(living, null, out _, out _);
            Activate(bare);
            Check(!bare.IsSpawning && bare.CanAttack, "An empty reveal configuration blocked combat.");
            var boss = CreateEnemy<ConfiguredBoss>(living, effect, out var bossRenderer, out _);
            Activate(boss);
            Check(!boss.IsSpawning && bossRenderer.sharedMaterial == living, "The ordinary spawn effect ran on a boss.");

            return "Enemy spawn reveal passed: catalog materials, first activation, red glitch, combat gates, isolation, restoration, disable, death and boss exclusion.";
        }
        finally
        {
            SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(isolated, true);
            Object.DestroyImmediate(living);
            Object.DestroyImmediate(effect);
        }
    }

    public static string CaptureCatalog()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Capture spawn previews in Edit Mode.");

        var catalog = ValidateCatalog();
        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "EnemySpawnReveal"));
        Directory.CreateDirectory(directory);
        var preview = new PreviewRenderUtility();
        try
        {
            var camera = preview.camera;
            camera.clearFlags = CameraClearFlags.Color;
            camera.backgroundColor = new Color(.065f, .083f, .11f);
            camera.orthographic = true;
            camera.nearClipPlane = .01f;
            preview.ambientColor = new Color(.6f, .6f, .65f);
            preview.lights[0].intensity = 1.2f;
            preview.lights[0].transform.rotation = Quaternion.Euler(35f, 35f, 0f);
            preview.lights[1].intensity = .75f;
            preview.lights[1].transform.rotation = Quaternion.Euler(315f, 215f, 0f);

            foreach (var entry in catalog.enemies)
            {
                var clone = Object.Instantiate(entry.prefab);
                preview.AddSingleGO(clone);
                try
                {
                    var animator = clone.GetComponentInChildren<Animator>();
                    if (animator != null)
                    {
                        animator.fireEvents = false;
                        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                        animator.Update(0f);
                    }
                    Frame(camera, clone);
                    var fade = clone.GetComponent<EnemyDeathFade>();
                    Invoke(typeof(EnemyDeathFade), fade, "BeginSpawnReveal", clone.GetComponent<Enemy>());
                    Check(fade.IsRevealing, $"{entry.prefab.name} had no active reveal renderer.");
                    Capture(preview, Path.Combine(directory, $"{entry.prefab.name}_01-hidden.png"));
                    Tick(fade, .5f);
                    Capture(preview, Path.Combine(directory, $"{entry.prefab.name}_02-red.png"));
                    Tick(fade, .5f);
                    Capture(preview, Path.Combine(directory, $"{entry.prefab.name}_03-complete.png"));
                }
                finally { Object.DestroyImmediate(clone); }
            }
            return directory;
        }
        finally { preview.Cleanup(); }
    }

    private static void Frame(Camera camera, GameObject clone)
    {
        Bounds bounds = default;
        bool found = false;
        foreach (var renderer in clone.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || renderer.forceRenderingOff
                || renderer is not (MeshRenderer or SkinnedMeshRenderer)) continue;
            if (found) bounds.Encapsulate(renderer.bounds);
            else { bounds = renderer.bounds; found = true; }
        }
        Check(found && bounds.extents.sqrMagnitude > 0f, $"{clone.name} has no visible preview geometry.");
        float radius = bounds.extents.magnitude;
        camera.orthographicSize = radius * 1.04f;
        camera.farClipPlane = Mathf.Max(10f, radius * 7f);
        camera.transform.position = bounds.center + new Vector3(.45f, .25f, 1f).normalized * (radius * 4f);
        camera.transform.LookAt(bounds.center);
    }

    private static void Capture(PreviewRenderUtility preview, string path)
    {
        var rect = new Rect(0, 0, CaptureSize, CaptureSize);
        preview.BeginPreview(rect, GUIStyle.none);
        Texture rendered;
        try { preview.Render(true); }
        finally { rendered = preview.EndPreview(); }

        var previous = RenderTexture.active;
        var target = RenderTexture.GetTemporary(CaptureSize, CaptureSize, 0, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(CaptureSize, CaptureSize, TextureFormat.RGB24, false);
        try
        {
            Graphics.Blit(rendered, target);
            RenderTexture.active = target;
            pixels.ReadPixels(rect, 0, 0, false);
            pixels.Apply();
            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(pixels);
        }
    }

    private static DungeonEnemyCatalog ValidateCatalog()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<DungeonEnemyCatalog>(
            "Assets/_Game/ProceduralDungeon/DungeonEnemyCatalog.asset");
        Check(catalog != null && catalog.enemies.Length > 0, "The dungeon catalog is missing or empty.");
        foreach (var entry in catalog.enemies)
        {
            var fade = entry.prefab.GetComponent<EnemyDeathFade>();
            Check(fade != null && fade.enabled, $"{entry.prefab.name} has no enabled spawn presentation.");
            var targets = new SerializedObject(fade).FindProperty("_targets");
            Check(targets.arraySize > 0, $"{entry.prefab.name} has no reveal renderers.");
            for (int i = 0; i < targets.arraySize; i++)
            {
                var target = targets.GetArrayElementAtIndex(i);
                var renderer = target.FindPropertyRelative("Renderer").objectReferenceValue as Renderer;
                var slots = target.FindPropertyRelative("Materials");
                Check(renderer != null && slots.arraySize > 0 && slots.arraySize == renderer.sharedMaterials.Length,
                    $"{entry.prefab.name} has mismatched reveal material slots.");
                for (int s = 0; s < slots.arraySize; s++)
                {
                    var material = slots.GetArrayElementAtIndex(s).objectReferenceValue as Material;
                    Check(material != null && material.IsKeywordEnabled("_FADE_ON")
                        && material.IsKeywordEnabled("_GLITCH_ON") && material.IsKeywordEnabled("_HOLOGRAM_ON"),
                        $"{entry.prefab.name} is missing a serialized Fade/Glitch/Hologram variant.");
                }
            }
        }
        return catalog;
    }

    private static T CreateEnemy<T>(Material living, Material effect, out MeshRenderer renderer, out EnemyDeathFade fade)
        where T : Enemy
    {
        var root = new GameObject(typeof(T).Name);
        root.SetActive(false);
        root.AddComponent<NavMeshAgent>().enabled = false;
        var enemy = root.AddComponent<T>();
        var model = new GameObject("Model");
        model.transform.SetParent(root.transform);
        model.AddComponent<Animator>();
        renderer = model.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { living, living };
        fade = root.AddComponent<EnemyDeathFade>();
        if (effect != null)
        {
            var data = new SerializedObject(fade);
            var targets = data.FindProperty("_targets");
            targets.arraySize = 1;
            var target = targets.GetArrayElementAtIndex(0);
            target.FindPropertyRelative("Renderer").objectReferenceValue = renderer;
            var slots = target.FindPropertyRelative("Materials");
            slots.arraySize = 2;
            for (int i = 0; i < slots.arraySize; i++)
                slots.GetArrayElementAtIndex(i).objectReferenceValue = effect;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        root.AddComponent<StatusEffectHandler>();
        root.SetActive(true);
        root.GetComponent<EnemyStat>().CurrentHp = 100f;
        root.GetComponent<PoiseHandler>().ResetPoise();
        return enemy;
    }

    private static void AssertRestored(Enemy enemy, Renderer renderer, Material living)
    {
        var materials = renderer.sharedMaterials;
        var global = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(global);
        var local = Read(renderer, 1);
        Check(!enemy.IsSpawning && materials[0] == living && materials[1] == living
            && Read(renderer, 0).isEmpty && Mathf.Approximately(global.GetFloat(HitBlend), .35f)
            && Mathf.Approximately(local.GetFloat(HitBlend), .6f) && !local.HasColor(HologramColor),
            "Completion did not restore the original materials and property blocks.");
    }

    private static MaterialPropertyBlock Read(Renderer renderer, int slot)
    {
        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block, slot);
        return block;
    }

    private static void Tick(EnemyDeathFade fade, float seconds) => Invoke(typeof(EnemyDeathFade), fade, "Advance", seconds);

    private static void Activate(Enemy enemy)
    {
        Invoke(typeof(Enemy), enemy, "OnEnable");
        Invoke(typeof(Enemy), enemy, "Start");
    }

    private static void Invoke(Type type, object target, string method, params object[] arguments)
        => type.GetMethod(method, Hidden).Invoke(target, arguments);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Exercises hit feedback against real renderers without running enemy AI.</summary>
public static class EnemyHitFlashRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly int HitBlend = Shader.PropertyToID("_HitBlend");
    private static readonly int FadeAmount = Shader.PropertyToID("_FadeAmount");
    private static int _checks;

    [MenuItem("Tools/Studio Baekho/Enemies/Run Hit Flash Regression")]
    private static void RunMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run hit flash fixtures in Edit Mode.");

        _checks = 0;
        Scene previous = SceneManager.GetActiveScene();
        Scene isolated = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        Material material = null;
        try
        {
            SceneManager.SetActiveScene(isolated);
            var shader = Shader.Find("AllIn13DShader/AllIn13DShader");
            Check(shader != null, "AllIn1 shader is missing.");
            material = new Material(shader) { name = "HitFlashRegression", hideFlags = HideFlags.HideAndDontSave };
            material.EnableKeyword("_HIT_ON");
            material.SetFloat("_Hit", 1f);
            material.SetFloat(HitBlend, 0f);

            ExerciseIsolationAndLifecycle(material);
            ExerciseRendererSelection(material);
            ExerciseDamageDispatch(material);
            return $"Enemy hit flash: {_checks} checks passed (damage dispatch, repeated/lethal hits, "
                + "material isolation, multiple slots, property preservation, disable reset and renderer ownership).";
        }
        finally
        {
            SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(isolated, true);
            if (material != null) Object.DestroyImmediate(material);
        }
    }

    private static void ExerciseIsolationAndLifecycle(Material material)
    {
        var first = EnemyFixture("HitFirst", material, out var firstRenderer);
        EnemyFixture("HitOther", material, out var otherRenderer);
        var properties = new MaterialPropertyBlock();
        properties.SetFloat(FadeAmount, .27f);
        firstRenderer.SetPropertyBlock(properties, 0);
        var flash = first.gameObject.AddComponent<EnemyHitFlash>();
        Set(flash, "_duration", 1f);

        flash.Play();
        Near(Blend(firstRenderer), .2f, "Hit did not immediately flash its renderer.");
        Near(Blend(otherRenderer), 0f, "Hit leaked to an enemy sharing the same material.");
        Near(material.GetFloat(HitBlend), 0f, "Hit mutated a shared material asset.");
        Near(Property(firstRenderer, FadeAmount), .27f, "Hit discarded the existing Fade override.");

        Set(flash, "_elapsed", .4f);
        InvokeUpdate(flash);
        float decayed = Blend(firstRenderer);
        Check(decayed > 0f && decayed < .2f, "Hit did not decay over its duration.");
        flash.Play();
        Near(Blend(firstRenderer), .2f, "A repeated hit did not restart the flash.");

        firstRenderer.GetPropertyBlock(properties, 0);
        properties.SetFloat(FadeAmount, .61f);
        firstRenderer.SetPropertyBlock(properties, 0);
        Set(flash, "_elapsed", 1f);
        InvokeUpdate(flash);
        Near(Blend(firstRenderer), 0f, "Completed flash did not restore its neutral blend.");
        Near(Property(firstRenderer, FadeAmount), .61f, "Flash completion overwrote a concurrent Fade update.");

        flash.Play();
        first.gameObject.SetActive(false);
        // Regular MonoBehaviours do not dispatch this lifecycle callback in Edit Mode.
        typeof(EnemyHitFlash).GetMethod("OnDisable", Hidden).Invoke(flash, null);
        Near(Blend(firstRenderer), 0f, "Disabling the enemy left its flash active.");
        Near(Property(firstRenderer, FadeAmount), .61f, "Disable reset discarded unrelated overrides.");
        first.gameObject.SetActive(true);
        flash.Play();
        Near(Blend(firstRenderer), .2f, "Re-enabled enemy could not flash again.");
    }

    private static void ExerciseRendererSelection(Material material)
    {
        var parent = EnemyFixture("HitParent", material, out var renderer);
        renderer.sharedMaterials = new[] { material, material };
        var global = new MaterialPropertyBlock();
        global.SetFloat(FadeAmount, .38f);
        renderer.SetPropertyBlock(global);
        var nested = EnemyFixture("HitNested", material, out var nestedRenderer);
        nested.transform.SetParent(parent.transform);
        var particles = new GameObject("HitParticles");
        particles.transform.SetParent(parent.transform);
        particles.AddComponent<ParticleSystem>();
        var particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
        particleRenderer.sharedMaterial = material;
        var flash = parent.gameObject.AddComponent<EnemyHitFlash>();

        flash.Play();
        Near(Blend(renderer, 0), .2f, "First material slot did not flash.");
        Near(Blend(renderer, 1), .2f, "Second material slot did not flash.");
        Near(Property(renderer, FadeAmount, 0), .38f, "Per-slot hit lost a renderer-wide override.");
        Near(Property(renderer, FadeAmount, 1), .38f, "Second slot lost a renderer-wide override.");
        Near(Blend(nestedRenderer), 0f, "Parent flash affected a nested enemy.");
        Near(Blend(particleRenderer), 0f, "Enemy flash affected a particle effect.");
    }

    private static void ExerciseDamageDispatch(Material material)
    {
        var enemy = EnemyFixture("HitDamage", material, out var renderer);
        enemy.TakeDamage(0f, poiseDamage: 0f);
        enemy.TakeDamage(-1f, poiseDamage: 0f);
        enemy.TakeDamage(float.NaN, poiseDamage: 0f);
        enemy.TakeDamage(float.PositiveInfinity, poiseDamage: 0f);
        Near(Blend(renderer), 0f, "Zero or invalid damage triggered a flash.");
        Near(enemy.EnemyStat.CurrentHp, 100f, "Zero or invalid damage changed HP.");

        enemy.TakeDamage(10f, poiseDamage: 0f);
        Near(enemy.EnemyStat.CurrentHp, 90f, "Valid damage did not deduct HP.");
        Near(Blend(renderer), .2f, "Valid health damage did not trigger the flash.");
        var flash = enemy.GetComponent<EnemyHitFlash>();
        Check(flash != null, "Damage did not create the hit feedback component.");
        Set(flash, "_elapsed", 1f);
        InvokeUpdate(flash);
        enemy.TakeDamage(200f, poiseDamage: 0f);
        Near(enemy.EnemyStat.CurrentHp, 0f, "Lethal damage did not clamp HP to zero.");
        Near(Blend(renderer), .2f, "Lethal damage did not trigger the flash before death.");
        Set(flash, "_elapsed", 1f);
        InvokeUpdate(flash);
        enemy.TakeDamage(10f, poiseDamage: 0f);
        Near(Blend(renderer), 0f, "A dead enemy flashed from an ignored hit.");
    }

    private static Enemy EnemyFixture(string name, Material material, out MeshRenderer renderer)
    {
        var root = new GameObject(name);
        var enemy = root.AddComponent<Enemy>();
        enemy.EnemyStat = root.GetComponent<EnemyStat>();
        enemy.EnemyStat.CurrentHp = 100f;
        root.GetComponent<PoiseHandler>().ResetPoise();
        var model = new GameObject("Model");
        model.transform.SetParent(root.transform);
        renderer = model.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        return enemy;
    }

    private static float Blend(Renderer renderer, int slot = 0) => Property(renderer, HitBlend, slot);

    private static float Property(Renderer renderer, int property, int slot = 0)
    {
        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block, slot);
        return block.GetFloat(property);
    }

    private static void Set(EnemyHitFlash target, string name, object value)
    {
        var field = typeof(EnemyHitFlash).GetField(name, Hidden)
            ?? throw new InvalidOperationException($"Missing flash field: {name}");
        field.SetValue(target, value);
    }

    private static void InvokeUpdate(EnemyHitFlash target)
    {
        var method = typeof(EnemyHitFlash).GetMethod("Update", Hidden)
            ?? throw new InvalidOperationException("Missing flash Update method.");
        method.Invoke(target, null);
    }

    private static void Near(float actual, float expected, string message)
        => Check(Mathf.Abs(actual - expected) < .0001f, $"{message} Expected {expected}, got {actual}.");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }
}

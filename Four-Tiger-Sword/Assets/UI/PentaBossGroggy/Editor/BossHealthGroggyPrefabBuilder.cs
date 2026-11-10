using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Penta.BossUI.Editor
{
    public static class BossHealthGroggyPrefabBuilder
    {
        public const string PrefabPath = "Assets/UI/PentaBossGroggy/Prefabs/BossHPWithGroggy.prefab";
        private const string SamplePath = "Assets/UI/PentaBossGroggy/Samples/BossHPWithGroggy_Sample.prefab";
        private const string PrefabName = "BossHPWithGroggy";

        public static void Prepare()
        {
            var root = PrefabUtility.LoadPrefabContents(SamplePath);
            try
            {
                var groggy = root.GetComponentInChildren<BossGroggyHPIntegration>(true);
                var bossName = root.GetComponentInChildren<TMP_Text>(true);
                Check(groggy && groggy.HpFill && groggy.Track && groggy.Fill && bossName,
                    "The approved sample is missing a required bar or name reference.");

                root.name = PrefabName;
                var ui = root.AddComponent<BossHealthGroggyUI>();
                var serialized = new SerializedObject(ui);
                serialized.FindProperty("healthFill").objectReferenceValue = groggy.HpFill;
                serialized.FindProperty("groggy").objectReferenceValue = groggy;
                serialized.FindProperty("bossName").objectReferenceValue = bossName;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                ui.SetHealthNormalized(1f);
                ui.SetGroggyNormalized(0f);

                // Saving at the existing path keeps the production asset GUID.
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
                Check(saved, $"Could not save {PrefabPath}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static void ValidatePrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Check(prefab && prefab.name == PrefabName && prefab.transform is RectTransform,
                $"Missing or invalid boss UI prefab at {PrefabPath}.");

            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var ui = root.GetComponent<BossHealthGroggyUI>();
                Check(ui && ui.Groggy && ui.HealthFill && ui.BossName &&
                    ui.Groggy.HpFill == ui.HealthFill && ui.Groggy.Track && ui.Groggy.Fill &&
                    ui.Groggy.Track != ui.Groggy.Fill && ui.HealthFill != ui.Groggy.Fill,
                    "The boss UI prefab has missing or incorrect references.");
                Check(ui.BossName.font && !string.IsNullOrEmpty(ui.BossName.text),
                    "The boss UI prefab is missing its default name or font.");
                CheckBars(ui, 1f, 0f);

                ui.SetHealth(75f, 100f);
                CheckBars(ui, 0.75f, 0f);
                ui.SetGroggy(65f, 100f);
                CheckBars(ui, 0.75f, 0.65f);
                ui.SetHealthNormalized(-1f);
                CheckBars(ui, 0f, 0.65f);
                ui.SetGroggyNormalized(2f);
                CheckBars(ui, 0f, 1f);
                ui.SetHealth(200f, 100f);
                CheckBars(ui, 1f, 1f);

                ui.SetHealth(float.PositiveInfinity, 100f);
                ui.SetGroggy(50f, float.NaN);
                CheckBars(ui, 0f, 0f);
                ui.SetHealth(50f, 0f);
                ui.SetGroggyNormalized(float.NegativeInfinity);
                CheckBars(ui, 0f, 0f);

                ui.SetBossName("Boss UI Validation");
                Check(ui.BossName.text == "Boss UI Validation", "The boss name setter did not update its label.");
                ui.SetBossName(null);
                Check(string.IsNullOrEmpty(ui.BossName.text), "A null boss name did not clear its label.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }

            Debug.Log($"Validated boss health and groggy UI prefab at {PrefabPath}.");
        }

        private static void CheckBars(BossHealthGroggyUI ui, float health, float accumulatedGroggy)
        {
            float remainingGroggy = 1f - accumulatedGroggy;
            Check(Mathf.Approximately(ui.HealthFill.fillAmount, health) &&
                Mathf.Approximately(ui.Groggy.Normalized, accumulatedGroggy) &&
                Mathf.Approximately(ui.Groggy.Fill.fillAmount, remainingGroggy),
                $"Expected health {health} and remaining groggy {remainingGroggy}.");
            if (!ui.Groggy.Fill.overrideSprite)
            {
                Check(Mathf.Approximately(ui.Groggy.Fill.rectTransform.anchorMax.x, remainingGroggy),
                    $"Expected the groggy strip width to match its remaining fraction {remainingGroggy}.");
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}

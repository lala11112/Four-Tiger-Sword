using System;
using System.IO;
using UnityEngine;
using UnityEditor;

// One-time migration: preserve asset GUIDs/references while switching to form-specific SO types.
public static class FormDataMigration
{
    private static readonly string[] Names = { "FireFormAction", "WaterFromAction", "WoodFormAction", "IronFormAction", "EarthFormAction" };
    private static readonly Type[] Types = { typeof(FireFormActionDataSO), typeof(WaterFormActionDataSO), typeof(WoodFormActionDataSO), typeof(IronFormActionDataSO), typeof(EarthFormActionDataSO) };
    private const string Backup = "Temp/FormDataMigration";
    public static string Migrate()
    {
        if (Application.isPlaying) throw new Exception("Exit Play Mode before migration.");
        Directory.CreateDirectory(Backup);
        for (int i = 0; i < Names.Length; i++)
        {
            string path = "Assets/_Project/Scripts/FormActions/" + Names[i] + ".asset";
            var data = AssetDatabase.LoadAssetAtPath<WeaponActionDataSO>(path);
            if (data == null) throw new Exception("Missing data: " + path);
            if (data.GetType() == Types[i])
            {
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssetIfDirty(data);
                continue;
            }
            string snapshot = Backup + "/" + Names[i];
            if (File.Exists(snapshot + ".json")) throw new Exception("Snapshot exists; do not overwrite: " + snapshot);
            File.Copy(path, snapshot + ".asset.bak");
            File.WriteAllText(snapshot + ".json", JsonUtility.ToJson(data));
            File.WriteAllText(snapshot + ".guid", AssetDatabase.AssetPathToGUID(path));
            var temporary = ScriptableObject.CreateInstance(Types[i]);
            try
            {
                var serialized = new SerializedObject(data);
                serialized.FindProperty("m_Script").objectReferenceValue = MonoScript.FromScriptableObject(temporary);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                // Changing m_Script recreates the managed object; reacquire it before saving.
                var migrated = AssetDatabase.LoadAssetAtPath<WeaponActionDataSO>(path);
                EditorUtility.SetDirty(migrated);
                AssetDatabase.SaveAssetIfDirty(migrated);
            }
            finally { UnityEngine.Object.DestroyImmediate(temporary); }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
        return "Migrated five assets in place; snapshots: " + Backup;
    }

    public static string Verify()
    {
        for (int i = 0; i < Names.Length; i++)
        {
            string path = "Assets/_Project/Scripts/FormActions/" + Names[i] + ".asset";
            var data = AssetDatabase.LoadAssetAtPath<WeaponActionDataSO>(path);
            if (data == null || data.GetType() != Types[i]) throw new Exception("Wrong type: " + path);
            if (AssetDatabase.AssetPathToGUID(path) != File.ReadAllText(Backup + "/" + Names[i] + ".guid"))
                throw new Exception("GUID changed: " + path);
            var expected = ScriptableObject.CreateInstance(Types[i]);
            try
            {
                JsonUtility.FromJsonOverwrite(File.ReadAllText(Backup + "/" + Names[i] + ".json"), expected);
                if (JsonUtility.ToJson(expected) != JsonUtility.ToJson(data))
                    throw new Exception("Serialized values changed: " + path);
            }
            finally { UnityEngine.Object.DestroyImmediate(expected); }
        }
        return "PASS: five derived types, unchanged GUIDs, all retained serialized values match pre-migration snapshots.";
    }
}

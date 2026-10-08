using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

// Run through the Unity in-memory script runner, outside Assets.
// The backup captures live values (including unsaved changes) before changing the schema.
public static class FormDataGroupingMigration
{
    private const string Backup = "Temp/FormDataGroupingBackup-20261008";

    public static string Apply()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
        var records = JArray.Parse(File.ReadAllText(Path.Combine(Backup, "manifest.json")));
        var pending = new List<Tuple<WeaponActionDataSO, JObject>>();
        foreach (JObject record in records)
        {
            var data = Load(record);
            var expected = Expected(record, data.GetType());
            var current = DataJson(data);
            if (JToken.DeepEquals(expected, current)) continue;

            // Do not overwrite a migrated asset that somebody subsequently edited.
            string path = AssetDatabase.GetAssetPath(data);
            string original = Path.Combine(Backup, (string)record["key"] + ".asset.bak");
            if (!File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(original)))
                throw new InvalidOperationException("Asset changed since backup: " + path);

            var probe = ScriptableObject.CreateInstance(data.GetType());
            try
            {
                Overwrite(probe, expected);
                AssertEqual(expected, DataJson(probe), path + " dry run");
            }
            finally { UnityEngine.Object.DestroyImmediate(probe); }
            pending.Add(Tuple.Create(data, expected));
        }

        // All records have passed conversion and conflict checks before any asset is changed.
        foreach (var item in pending)
        {
            Overwrite(item.Item1, item.Item2);
            AssertEqual(item.Item2, DataJson(item.Item1), AssetDatabase.GetAssetPath(item.Item1));
            EditorUtility.SetDirty(item.Item1);
            AssetDatabase.SaveAssetIfDirty(item.Item1);
        }
        return "Migrated " + pending.Count + " form assets. " + Verify();
    }

    public static string Verify()
    {
        var records = JArray.Parse(File.ReadAllText(Path.Combine(Backup, "manifest.json")));
        int values = 0;
        foreach (JObject record in records)
        {
            var data = Load(record);
            var expected = Expected(record, data.GetType());
            AssertEqual(expected, DataJson(data), (string)record["path"]);
            values += expected.Descendants().OfType<JValue>().Count();
        }
        return records.Count + " assets match all " + values + " saved values, curves and references.";
    }

    public static string ReimportAndVerify()
    {
        var records = JArray.Parse(File.ReadAllText(Path.Combine(Backup, "manifest.json")));
        foreach (JObject record in records)
            AssetDatabase.ImportAsset((string)record["path"], ImportAssetOptions.ForceUpdate);
        return Verify();
    }

    private static WeaponActionDataSO Load(JObject record)
    {
        string path = AssetDatabase.GUIDToAssetPath((string)record["guid"]);
        if (path != (string)record["path"])
            throw new InvalidOperationException("Asset path/GUID changed: " + record["path"]);
        var data = AssetDatabase.LoadAllAssetsAtPath(path).OfType<WeaponActionDataSO>()
            .SingleOrDefault(asset =>
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long id);
                return id == (long)record["id"];
            });
        if (data == null || data.GetType().Name != (string)record["type"])
            throw new InvalidOperationException("Missing form or changed type: " + path);
        return data;
    }

    private static JObject Expected(JObject record, Type type)
    {
        string path = Path.Combine(Backup, (string)record["key"] + ".json");
        var json = JObject.Parse(File.ReadAllText(path));
        return ConvertObject((JObject)json["MonoBehaviour"], type);
    }

    private static JObject DataJson(UnityEngine.Object data)
    {
        var json = (JObject)JObject.Parse(EditorJsonUtility.ToJson(data))["MonoBehaviour"];
        foreach (var property in json.Properties().Where(p => p.Name.StartsWith("m_", StringComparison.Ordinal)).ToArray())
            property.Remove();
        return json;
    }

    private static JObject ConvertObject(JObject source, Type type)
    {
        var result = new JObject();
        foreach (var property in source.Properties())
        {
            if (property.Name.StartsWith("m_", StringComparison.Ordinal)) continue;
            var paths = FindFields(type, property.Name).ToArray();
            if (paths.Length != 1)
                throw new InvalidOperationException(type.Name + "." + property.Name + " has " + paths.Length + " destinations.");
            FieldInfo[] path = paths[0];
            JObject parent = result;
            foreach (var field in path.Take(path.Length - 1))
            {
                if (parent[field.Name] == null) parent[field.Name] = new JObject();
                parent = (JObject)parent[field.Name];
            }
            var leaf = path[path.Length - 1];
            parent[leaf.Name] = ConvertValue(property.Value, leaf.FieldType);
        }
        return result;
    }

    private static IEnumerable<FieldInfo[]> FindFields(Type type, string oldName)
    {
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            if (field.IsNotSerialized) continue;
            if (field.Name == oldName || field.GetCustomAttributes<FormerlySerializedAsAttribute>()
                .Any(a => a.oldName == oldName))
            {
                yield return new[] { field };
                continue;
            }
            var nested = field.FieldType;
            if (nested.Assembly != typeof(WeaponActionData).Assembly || !nested.IsClass ||
                nested == typeof(WeaponActionData) || nested == typeof(HitEvent) ||
                typeof(UnityEngine.Object).IsAssignableFrom(nested)) continue;
            foreach (var child in FindFields(nested, oldName))
                yield return new[] { field }.Concat(child).ToArray();
        }
    }

    private static JToken ConvertValue(JToken value, Type type)
    {
        if (value.Type == JTokenType.Null) return value.DeepClone();
        if (type == typeof(WeaponActionData) || type == typeof(HitEvent))
            return ConvertObject((JObject)value, type);
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            Type elementType = type.GetGenericArguments()[0];
            return new JArray(((JArray)value).Select(item => ConvertValue(item, elementType)));
        }
        return value.DeepClone();
    }

    private static void Overwrite(UnityEngine.Object target, JObject data)
    {
        EditorJsonUtility.FromJsonOverwrite(new JObject { ["MonoBehaviour"] = data.DeepClone() }.ToString(), target);
    }

    private static void AssertEqual(JObject expected, JObject actual, string context)
    {
        if (!JToken.DeepEquals(expected, actual))
        {
            string report = Path.Combine(Backup, "verification-failure.json");
            File.WriteAllText(report, new JObject { ["context"] = context, ["expected"] = expected, ["actual"] = actual }.ToString());
            throw new InvalidOperationException("Serialized values differ at " + context + "; see " + report);
        }
    }
}

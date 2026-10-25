UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
var output = StudioBaekho.BaekhoMapImporter.BuildFromPath("Assets/_Game/Greybox/Blockout/BaekhoMap16/Map.baekhomap.json");
System.IO.File.WriteAllText(System.IO.Path.Combine(UnityEngine.Application.dataPath, "../BaekhoMap16Import/prefab-path.txt"), output);
UnityEditor.Selection.activeObject = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(output);
UnityEditor.EditorGUIUtility.PingObject(UnityEditor.Selection.activeObject);
return output;

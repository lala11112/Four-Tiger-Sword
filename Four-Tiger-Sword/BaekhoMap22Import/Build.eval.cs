var manifestPath = "Assets/_Game/Greybox/Blockout/BaekhoMap22/Map.baekhomap.json";
if (UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Editor is not ready");
var sceneBefore = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
var scenePathBefore = sceneBefore.path;
var dirtyBefore = sceneBefore.isDirty;
UnityEditor.AssetDatabase.ImportAsset(manifestPath, UnityEditor.ImportAssetOptions.ForceSynchronousImport);
UnityEditor.AssetDatabase.ImportAsset("Assets/_Game/Greybox/Blockout/BaekhoMap22/README.txt", UnityEditor.ImportAssetOptions.ForceSynchronousImport);
UnityEditor.AssetDatabase.ImportAsset("Assets/_Game/Greybox/Blockout/BaekhoMap22/blueprint.png", UnityEditor.ImportAssetOptions.ForceSynchronousImport);
var path = StudioBaekho.BaekhoMapImporter.BuildFromPath(manifestPath);
System.IO.File.WriteAllText("BaekhoMap22Import/generated-prefab-path.txt", path);
var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
UnityEditor.Selection.activeObject = prefab;
UnityEditor.EditorGUIUtility.PingObject(prefab);
var sceneAfter = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
return new { path, sceneUnchanged = scenePathBefore == sceneAfter.path && dirtyBefore == sceneAfter.isDirty, scene = sceneAfter.path };



using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace StudioBaekho
{
    [CustomEditor(typeof(BaekhoMapAsset)), CanEditMultipleObjects]
    public sealed class BaekhoMapAssetEditor : Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(new HelpBox("프리팹 루트에서 등록 여부와 종류를 지정한 뒤 맵 제작기의 ‘표시된 프리팹 가져오기’를 사용하세요. 여러 프리팹을 함께 선택해 편집할 수 있습니다.", HelpBoxMessageType.Info));
            foreach (string property in new[] { "importEnabled", "role", "label" })
                root.Add(new PropertyField(serializedObject.FindProperty(property)));
            var initial = new Foldout { text = "초기 설정 (새로 등록할 때 적용)", value = false };
            foreach (string property in new[] { "useInitialScale", "initialScale", "useInitialRotation", "initialRotationY", "useInitialOffset", "initialOffset", "stairNativeRise", "lightAnchor" })
                initial.Add(new PropertyField(serializedObject.FindProperty(property)));
            root.Add(initial);
            return root;
        }

        [MenuItem("Assets/Baekho/Add Map Asset Metadata")]
        private static void AddSelected()
        {
            try
            {
                int added = AddToSelectedPrefabs(Selection.objects);
                Debug.Log("Baekho: added Map Asset registration to " + added + " selected prefabs. Set their asset roles in the Inspector.");
            }
            catch (Exception exception) { EditorUtility.DisplayDialog("Baekho 프리팹 등록", exception.Message, "OK"); }
        }

        /// <summary>Validate the entire explicit selection before touching any prefab. Never expands folders.</summary>
        public static int AddToSelectedPrefabs(UnityEngine.Object[] selection)
        {
            BaekhoAssetCatalog.RequireReadyEditor();
            string[] guids = BaekhoPropCatalog.ResolveSelectedPrefabGuids(selection);
            int added = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab.GetComponent<BaekhoMapAsset>() != null) continue;
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (contents.GetComponent<BaekhoMapAsset>() != null) continue;
                    contents.AddComponent<BaekhoMapAsset>();
                    GameObject saved = PrefabUtility.SaveAsPrefabAsset(contents, path);
                    if (saved == null) throw new InvalidOperationException("등록 컴포넌트를 저장할 수 없습니다: " + path);
                    added++;
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }
            return added;
        }
    }
}

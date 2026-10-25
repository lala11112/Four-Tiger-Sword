using System;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace StudioBaekho
{
    [CustomEditor(typeof(BaekhoPropSet)), CanEditMultipleObjects]
    public sealed class BaekhoPropSetEditor : Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(new HelpBox("함께 쓸 프리팹 목록을 만들면 맵 제작기가 구역에 맞춰 배치합니다. Project 창에서 여러 .prefab 에셋을 선택해 프리팹 목록에 끌어 놓으세요. 프리팹마다 등록 컴포넌트를 붙일 필요는 없습니다.", HelpBoxMessageType.Info));
            foreach (string property in new[] { "importEnabled", "label", "usage", "arrangement", "prefabs" })
                root.Add(new PropertyField(serializedObject.FindProperty(property)));
            root.Add(new HelpBox("바닥 소품만 넣으세요. 비활성 등록, 바닥 소품 외 역할, 벽 부착 소품과 크기를 잴 수 없는 프리팹은 가져올 때 제외 사유가 표시됩니다. 원본 프리팹과 씬 배치는 변경하지 않습니다.", HelpBoxMessageType.Info));
            return root;
        }

        [MenuItem("Assets/Baekho/Create Prop Set From Selection")]
        private static void CreateSelected()
        {
            try
            {
                BaekhoPropSet set = CreateFromSelectedPrefabs(Selection.objects);
                ProjectWindowUtil.ShowCreatedAsset(set);
            }
            catch (Exception exception) { EditorUtility.DisplayDialog("Baekho 소품 묶음", exception.Message, "확인"); }
        }

        /// <summary>Validate the entire explicit selection before creating a new asset. Source prefabs are read only.</summary>
        public static BaekhoPropSet CreateFromSelectedPrefabs(UnityEngine.Object[] selection)
        {
            BaekhoAssetCatalog.RequireReadyEditor();
            string[] guids = BaekhoPropCatalog.ResolveSelectedPrefabGuids(selection);
            if (guids.Length > BaekhoAssetCatalog.MaxSetMembers)
                throw new InvalidOperationException("소품 묶음에는 최대 256개 프리팹을 넣을 수 있습니다.");
            var prefabs = new GameObject[guids.Length];
            for (int index = 0; index < guids.Length; index++)
                prefabs[index] = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[index]));
            string folder = Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(guids[0])).Replace('\\', '/');
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/Prop Set.asset");
            var set = CreateInstance<BaekhoPropSet>();
            set.prefabs = prefabs;
            AssetDatabase.CreateAsset(set, path);
            return set;
        }
    }
}

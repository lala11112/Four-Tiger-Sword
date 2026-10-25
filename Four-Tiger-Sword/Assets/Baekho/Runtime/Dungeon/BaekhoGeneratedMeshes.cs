using System.Collections.Generic;
using UnityEngine;

namespace StudioBaekho
{
    /// <summary>Owns only generated mesh copies; source prefab meshes always remain shared.</summary>
    [ExecuteAlways, DisallowMultipleComponent, AddComponentMenu("")]
    public sealed class BaekhoGeneratedMeshes : MonoBehaviour
    {
        [SerializeField, HideInInspector] private List<Mesh> meshes = new List<Mesh>();

        public void Own(Mesh mesh)
        {
            if (mesh != null) meshes.Add(mesh);
        }

        public void Release()
        {
            foreach (var mesh in meshes)
                if (mesh != null) BaekhoDungeonGeometry.DestroyObject(mesh);
            meshes.Clear();
        }

        private void OnDestroy() => Release();
    }
}

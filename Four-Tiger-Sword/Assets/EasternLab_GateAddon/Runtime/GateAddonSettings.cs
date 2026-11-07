using System.Collections.Generic;
using UnityEngine;

namespace EasternLab.GateAddon
{
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class GateAddonSettings : MonoBehaviour
    {
        [InspectorName("목적지 씬")]
        [Tooltip("Build Profiles의 Scene List에 등록한 씬 이름. 입력하면 같은 씬의 도착 지점보다 우선합니다.")]
        public string targetScene;
        [InspectorName("같은 씬의 도착 지점")]
        [Tooltip("플레이어의 발이 놓일 위치와 바라볼 방향을 가진 빈 오브젝트입니다.")]
        public Transform destinationPoint;
        [InspectorName("플레이어 카메라")]
        [Tooltip("비워 두면 MainCamera 태그의 카메라를 사용합니다.")]
        public Camera playerCamera;
        [InspectorName("플레이어 (선택)")]
        [Tooltip("외부 카메라를 쓰고 Player 태그가 없는 캐릭터는 여기 지정하세요.")]
        public Transform playerRoot;
        [InspectorName("모델에 빠진 충돌체 추가")]
        public bool addMissingColliders = true;

        [SerializeField, HideInInspector] public GateTransit transit;
        [SerializeField, HideInInspector] public GateAccessPanel panel;
        [SerializeField, HideInInspector] public SpatialPortalView view;
        readonly List<MeshCollider> ownedColliders = new List<MeshCollider>();

        void Awake()
        {
            ApplyDestination();
            var model = transform.parent;
            var architecture = model ? model.Find("Architecture") : null;
            if (!architecture)
            {
                Debug.LogWarning("WarpGate_Addon을 EasternLab_Model의 바로 아래에 배치하세요.", this);
                return;
            }
            var frame = architecture.Find("03_DataGate");
            if (frame && transit && transit.activation)
            {
                var renderer = frame.GetComponent<MeshRenderer>();
                if (renderer)
                {
                    int slot = System.Array.FindIndex(renderer.sharedMaterials,
                        material => material && material.name == "V9_UI_gate_status");
                    if (slot >= 0)
                    {
                        transit.activation.statusRenderer = renderer;
                        transit.activation.statusMaterialIndex = slot;
                        transit.activation.ApplyVisuals();
                    }
                }
            }
            if (!addMissingColliders) return;
            foreach (var mesh in architecture.GetComponentsInChildren<MeshFilter>(false))
                AddMissingCollider(mesh);
            var bore = model.Find("GateHardware/InnerFrame/Bore");
            if (bore) AddMissingCollider(bore.GetComponent<MeshFilter>());
        }

        public void ApplyDestination()
        {
            if (transit)
            {
                transit.targetScene = targetScene == null ? "" : targetScene.Trim();
                transit.demoExit = destinationPoint;
                transit.playerRoot = playerRoot;
                transit.playerCamera = playerCamera;
            }
            if (panel) panel.playerCamera = playerCamera;
            if (view) view.playerCamera = playerCamera;
        }

        void AddMissingCollider(MeshFilter filter)
        {
            if (!filter || !filter.sharedMesh || filter.GetComponent<Collider>()) return;
            var collider = filter.gameObject.AddComponent<MeshCollider>();
            if (collider.sharedMesh != filter.sharedMesh) collider.sharedMesh = filter.sharedMesh;
            ownedColliders.Add(collider);
        }

        void OnDestroy()
        {
            foreach (var collider in ownedColliders)
                if (collider) Destroy(collider);
        }
    }
}

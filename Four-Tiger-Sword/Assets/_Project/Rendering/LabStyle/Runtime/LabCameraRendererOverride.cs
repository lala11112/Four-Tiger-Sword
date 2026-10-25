using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FourTigerSword.Rendering
{
    /// <summary>
    /// Opts this camera into a lab renderer only on the designated desktop pipeline.
    /// Other quality pipelines retain their own default renderer, including after quality changes.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera), typeof(UniversalAdditionalCameraData))]
    [AddComponentMenu("Four Tiger Sword/Rendering/Lab Camera Renderer Override")]
    public sealed class LabCameraRendererOverride : MonoBehaviour
    {
        public UniversalRenderPipelineAsset desktopPipeline;
        public ScriptableRendererData desktopRenderer;

        private UniversalAdditionalCameraData _cameraData;

        private void OnEnable()
        {
            _cameraData = GetComponent<UniversalAdditionalCameraData>();
            RenderPipelineManager.activeRenderPipelineAssetChanged += OnPipelineChanged;
            ApplyCurrent();
        }

        private void OnPipelineChanged(RenderPipelineAsset previous, RenderPipelineAsset current)
        {
            ApplyForPipeline(current);
        }

        [ContextMenu("Apply Renderer For Current Pipeline")]
        public void ApplyCurrent()
        {
            ApplyForPipeline(GraphicsSettings.currentRenderPipeline);
        }

        private void ApplyForPipeline(RenderPipelineAsset current)
        {
            if (_cameraData == null) _cameraData = GetComponent<UniversalAdditionalCameraData>();
            if (_cameraData == null) return;

            int rendererIndex = -1;
            if (desktopPipeline != null && desktopRenderer != null && current == desktopPipeline)
            {
                var renderers = desktopPipeline.rendererDataList;
                for (int index = 0; index < renderers.Length; index++)
                {
                    if (renderers[index] != desktopRenderer) continue;
                    rendererIndex = index;
                    break;
                }
            }
            _cameraData.SetRenderer(rendererIndex);
        }

        private void OnDisable()
        {
            RenderPipelineManager.activeRenderPipelineAssetChanged -= OnPipelineChanged;
            if (_cameraData != null) _cameraData.SetRenderer(-1);
        }
    }
}

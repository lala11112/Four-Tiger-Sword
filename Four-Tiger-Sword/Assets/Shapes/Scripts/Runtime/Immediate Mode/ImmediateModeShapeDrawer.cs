// Shapes © Freya Holmér - https://twitter.com/FreyaHolmer/
// Website & Documentation - https://acegikmo.com/shapes/

namespace Shapes {

	using UnityEngine;

	/// <summary>A helper type to inherit from when you want a component that draws immediate mode shapes</summary>
	public class ImmediateModeShapeDrawer : MonoBehaviour {

		/// <summary>Whether or not to only draw in cameras that can see the layer of this GameObject</summary>
		[Tooltip( "When enabled, shapes will only draw in cameras that can see the layer of this GameObject" )]
		public bool useCullingMasks = false;

		/// <summary>Override this to draw Shapes in immediate mode. This is called once per camera. You can draw using this code: using(Draw.Command(cam)){ // Draw here }</summary>
		/// <param name="cam">The camera that is currently rendering</param>
		public virtual void DrawShapes( Camera cam ) {
			// override this and draw shapes in immediate mode here
		}

		void OnCameraPreRender( Camera cam ) {
			switch( cam.cameraType ) {
				case CameraType.Preview:
				case CameraType.Reflection:
					return; // Don't render in preview windows or in reflection probes in case we run this script in the editor
			}
			if( useCullingMasks && ( cam.cullingMask & ( 1 << gameObject.layer ) ) == 0 )
				return; // scene & game view cameras should respect culling layer settings if you tell them to

			DrawShapes( cam );
		}

		public virtual void OnEnable() {
			if( UnityInfo.CurrentRp == RenderPipeline.Legacy ) {
				Camera.onPreRender += OnCameraPreRender;
			} else {
				#if URP_INSTALLED || HDRP_INSTALLED
				UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += DrawShapesSRP;
				#endif
			}
		}

		public virtual void OnDisable() {
			if( UnityInfo.CurrentRp == RenderPipeline.Legacy ) {
				Camera.onPreRender -= OnCameraPreRender;
			} else {
				#if URP_INSTALLED || HDRP_INSTALLED
				UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= DrawShapesSRP;
				#endif
			}
		}

		#if URP_INSTALLED || HDRP_INSTALLED
		void DrawShapesSRP( UnityEngine.Rendering.ScriptableRenderContext ctx, Camera cam ) => OnCameraPreRender( cam );
		#endif

	}

}
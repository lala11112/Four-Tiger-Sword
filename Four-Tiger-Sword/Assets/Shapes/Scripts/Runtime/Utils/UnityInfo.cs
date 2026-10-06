using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
#if HDRP_INSTALLED
using UnityEngine.Rendering.HighDefinition;
#endif
#if URP_INSTALLED
using UnityEngine.Rendering.Universal;
#endif

// Shapes © Freya Holmér - https://twitter.com/FreyaHolmer/
// Website & Documentation - https://acegikmo.com/shapes/
namespace Shapes {

	internal static class UnityInfo {
		public static bool UsingSRP => CurrentRp != RenderPipeline.Legacy;
		public static string OnPreRenderNameForCurrentPipeline => UsingSRP ? "RenderPipelineManager.beginCameraRendering" : "Camera.onPreRender";
		public const int INSTANCES_MAX = 1023;

		internal static RenderPipeline DefaultRp => RenderPipelineOfAsset( GraphicsSettings.defaultRenderPipeline );
		internal static RenderPipeline CurrentRp => RenderPipelineOfAsset( CurrentRpAsset );

		internal static RenderPipelineAsset CurrentRpAsset {
			get {
				RenderPipelineAsset rpa = QualitySettings.renderPipeline;
				return rpa == null ? GraphicsSettings.defaultRenderPipeline : rpa;
			}
		}

		static RenderPipeline RenderPipelineOfAsset( RenderPipelineAsset asset ) {
			if( asset == null )
				return RenderPipeline.Legacy;
			#if URP_INSTALLED
			if( asset is UniversalRenderPipelineAsset urpAsset )
				return RenderPipeline.URP;
			#endif
			#if HDRP_INSTALLED
			if( asset is HDRenderPipelineAsset hdrpAsset )
				return RenderPipeline.HDRP;
			#endif

			Debug.LogWarning( $"Unknown Render pipeline of type {asset.GetType()}, falling back to the built-in render pipeline" );
			return RenderPipeline.Legacy;
		}

		public static bool IsReadyForImmediateMode() => IsReadyForImmediateMode( CurrentRpAsset );

		public static bool IsReadyForImmediateMode( RenderPipelineAsset asset ) {
			if( RenderPipelineOfAsset( asset ) is RenderPipeline.URP ) {
				// URP requires adding renderer features
				#if URP_INSTALLED
				if( asset is UniversalRenderPipelineAsset urpAsset ) {
					// todo: does it need to be in every renderer data or just one?
					foreach( ScriptableRendererData data in urpAsset.rendererDataList ) {
						if( IsReadyForImmediateMode( data ) )
							return true;
					}
				}
				#endif
				return false;
			}
			return true;
		}

		#if URP_INSTALLED
		public static bool IsReadyForImmediateMode( ScriptableRendererData data ) => data.TryGetRendererFeature( out ShapesRenderFeature _ );
		#endif

		internal static IEnumerable<RenderPipeline> RenderPipelinesAvailable {
			get {
				#if URP_INSTALLED
				yield return RenderPipeline.URP;
				#endif
				#if HDRP_INSTALLED
				yield return RenderPipeline.HDRP;
				#endif
				yield return RenderPipeline.Legacy;
			}
		}

		#if UNITY_EDITOR
		#if URP_INSTALLED
		// #if UNITY_2021_2_OR_NEWER
		// using URP_RND_DATA = UnityEngine.Rendering.Universal.ScriptableRendererData;
		// #else
		// using URP_RND_DATA = UnityEngine.Rendering.Universal.ForwardRendererData;
		// #endif
		internal static IEnumerable<ScriptableRendererData> LoadAllURPRenderData() => ShapesIO.LoadAllAssets<ScriptableRendererData>( "Assets/" );
		#endif
		#endif

		public static int MainColorPropInCurrentRp => UnityInfo.CurrentRp == RenderPipeline.Legacy ? ShapesMaterialUtils.propColor : ShapesMaterialUtils.propBaseColor;
	}

}
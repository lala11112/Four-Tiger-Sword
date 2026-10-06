using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

#if URP_INSTALLED
using UnityEditor.Rendering.Universal;
using URP_RND_DATA = UnityEngine.Rendering.Universal.UniversalRendererData;
#endif

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.Build;

#if URP_INSTALLED
using URP_RND_DATA_EDITOR = UnityEditor.Rendering.Universal.UniversalRendererDataEditor;
#endif

#endif

// Shapes © Freya Holmér - https://twitter.com/FreyaHolmer/
// Website & Documentation - https://acegikmo.com/shapes/
namespace Shapes {

	public static class ShapesImportState {

		#if UNITY_EDITOR
		[DidReloadScripts( 1 )]
		public static void CheckRenderPipelineSoon() {
			if( ShapesConfig.Instance != null && ShapesConfig.Instance.autoConfigureRenderPipeline )
				EditorApplication.delayCall += AutoCheckRenderPipeline;
		}

		static void AutoCheckRenderPipeline() {
			// make sure we have a valid RP state
			if( IsUsingLegacyPreprocessorKeywords( out string keywordList ) ) {
				string desc = $"Your project is using keywords from an old version of Shapes ({keywordList})\n" +
							  $"Would you like to strip those keywords and recompile Shapes for your current setup?\n(Shapes may not work if you don't)\n\n" +
							  $"Note: You disable this auto-checker in the Shapes settings";

				if( EditorUtility.DisplayDialog( "Shapes setup checker", desc, $"yes (recompile)", "cancel" ) ) {
					ForceUpdateRpStuff();
				}
			}
		}

		internal static void ForceUpdateRpStuff() {
			Debug.Log( $"Shapes is recompiling render pipeline specifics..." );
			StripLegacyKeywordsFromProject(); // set up preprocessor defines, this will also require a second pass of this whole method
			EditorApplication.delayCall += () => {
				ForceSetRpSecondPass();
				Debug.Log( $"Shapes is done recompiling" );
			};
		}


		static void ForceSetRpSecondPass() {
			CodegenShaders.GenerateShadersAndMaterials();
			if( UnityInfo.IsReadyForImmediateMode() == false ) {
				string msg = "In order for immediate mode drawing to work, URP render data needs the shapes render features. Would you like to open Shapes settings to make sure immediate mode drawing is supported?";
				if( EditorUtility.DisplayDialog( "URP render features", msg, "yes", "no, I don't need IM drawing" ) )
					MenuItems.OpenCsharpSettings();
			}

			MakeSureSampleMaterialsAreValid();
		}


		static void MakeSureSampleMaterialsAreValid() {
			Shader targetShader = GetDefaultShader();

			bool changed = false;
			if( ShapesIO.TryMakeAssetsEditable( UIAssets.Instance.sampleMaterials ) ) { // ensures version control allows us to edit
				foreach( var mat in UIAssets.Instance.sampleMaterials ) {
					if( mat == null )
						continue; // samples were probably not imported into this project (or they were deleted) if this is null
					if( mat.shader != targetShader ) {
						Undo.RecordObject( mat, "Shapes update sample materials shaders" );
						Color color = GetMainColor( mat );
						mat.shader = targetShader;
						mat.SetColor( UnityInfo.MainColorPropInCurrentRp, color );
						changed = true;
					}
				}
			}

			if( changed )
				Debug.Log( "Shapes updated sample material shaders to match your current render pipeline" );
		}

		public static Shader GetDefaultShader() => UnityInfo.CurrentRp == RenderPipeline.Legacy ? UIAssets.Instance.birpDefaultShader : GraphicsSettings.defaultRenderPipeline.defaultShader;

		static Color GetMainColor( Material mat ) {
			if( mat.HasProperty( ShapesMaterialUtils.propColor ) ) return mat.GetColor( ShapesMaterialUtils.propColor );
			if( mat.HasProperty( ShapesMaterialUtils.propBaseColor ) ) return mat.GetColor( ShapesMaterialUtils.propBaseColor );
			return Color.white;
		}


		#if URP_INSTALLED
		/* this is pretty cursed, I'm commenting this out for now.
		static class UrpRndFuncs {
			const BindingFlags bfs = BindingFlags.Instance | BindingFlags.NonPublic;
			public static readonly FieldInfo fRndDataList = typeof(UniversalRenderPipelineAsset).GetField( "m_RendererDataList", bfs );
			public static readonly MethodInfo fAddComponent = typeof(ScriptableRendererDataEditor).GetMethod( "AddComponent", bfs );
			public static readonly MethodInfo fOnEnable = typeof(ScriptableRendererDataEditor).GetMethod( "OnEnable", bfs );
			public static readonly bool successfullyLoaded = fRndDataList != null && fAddComponent != null && fOnEnable != null;

			public static readonly string failMessage = $"Unity's URP API seems to have changed. Failed to load: " +
														$"{( fRndDataList == null ? "UniversalRenderPipelineAsset.m_RendererDataList" : "" )} " +
														$"{( fAddComponent == null ? "ScriptableRendererDataEditor.AddComponent" : "" )} " +
														$"{( fOnEnable == null ? "ScriptableRendererDataEditor.OnEnable" : "" )}";
		}

		static void EnsureShapesPassExistsInTheUrpRenderer() {
			if( UrpRndFuncs.successfullyLoaded ) { // if our reflected members failed to load, we're kinda screwed :c
				if( GraphicsSettings.renderPipelineAsset is UniversalRenderPipelineAsset urpa ) { // find the URP asset
					ScriptableRendererData[] srd = (ScriptableRendererData[])UrpRndFuncs.fRndDataList.GetValue( urpa );
					foreach( var rndd in srd.Where( x => x is URP_RND_DATA ) ) { // only add to forward renderer
						if( rndd.rendererFeatures.Any( x => x is ShapesRenderFeature ) == false ) { // does it have Shapes?
							// does not contain the Shapes render feature, so, oh boy, here we go~
							if( ShapesIO.TryMakeAssetsEditable( urpa ) ) {
								URP_RND_DATA_EDITOR fwEditor = (URP_RND_DATA_EDITOR)Editor.CreateEditor( rndd );
								UrpRndFuncs.fOnEnable.Invoke( fwEditor, null ); // you ever just call OnEnable manually
								UrpRndFuncs.fAddComponent.Invoke( fwEditor, new[] { (object)nameof(ShapesRenderFeature) } );
								DestroyImmediate( fwEditor ); // luv 2 create temporary editors
								Debug.Log( $"Added Shapes renderer feature to {rndd.name}", rndd );
							}
						}
					}
				} else
					Debug.LogWarning( $"Shapes failed to load the URP pipeline asset to add the renderer feature. " +
									  $"You might have to add {nameof(ShapesRenderFeature)} to your renderer asset manually" );
			} else
				Debug.LogError( UrpRndFuncs.failMessage );
		}*/

		#endif

		static NamedBuildTarget CurrentNamedBuildTarget => NamedBuildTarget.FromBuildTargetGroup( BuildPipeline.GetBuildTargetGroup( EditorUserBuildSettings.activeBuildTarget ) );
		static List<string> GetCurrentKeywords() => PlayerSettings.GetScriptingDefineSymbols( CurrentNamedBuildTarget ).Split( ';' ).ToList();
		static void SetCurrentKeywords( IEnumerable<string> keywords ) => PlayerSettings.SetScriptingDefineSymbols( CurrentNamedBuildTarget, string.Join( ";", keywords ) );

		internal static bool IsUsingLegacyPreprocessorKeywords( out string keywordList ) => string.IsNullOrEmpty( keywordList = string.Join( ", ", LegacyPreprocessors ) ) == false;
		static IEnumerable<string> LegacyPreprocessors =>
			GetCurrentKeywords()
				.Where( s =>
					s == RenderPipeline.URP.LegacyPreprocessorDefineName() ||
					s == RenderPipeline.HDRP.LegacyPreprocessorDefineName() ||
					s == RenderPipeline.Legacy.LegacyPreprocessorDefineName()
				);

		static readonly string[] legacyRpPreprocessors = new[] { RenderPipeline.URP.LegacyPreprocessorDefineName(), RenderPipeline.HDRP.LegacyPreprocessorDefineName(), RenderPipeline.Legacy.LegacyPreprocessorDefineName() };

		static void StripLegacyKeywordsFromProject() {
			List<string> keywords = GetCurrentKeywords();
			if( keywords.Intersect( legacyRpPreprocessors ).Any() && ShapesIO.TryMakeAssetsEditable( ShapesIO.projectSettingsPath ) ) {
				SetCurrentKeywords( keywords.Except( legacyRpPreprocessors ) );
			}
		}

		// static void SetPreprocessorRpSymbols( RenderPipeline rpTarget ) {
		// 	Debug.Log( $"Setting preprocessor symbols for {rpTarget.PrettyName()}" );
		// 	List<string> symbols = GetCurrentKeywords();
		//
		// 	bool changed = false;
		//
		// 	void CheckRpSymbol( RenderPipeline rp ) {
		// 		bool on = rp == rpTarget;
		// 		string ppName = rp.LegacyPreprocessorDefineName();
		// 		if( on && symbols.Contains( ppName ) == false ) {
		// 			symbols.Add( ppName );
		// 			changed = true;
		// 		} else if( on == false && symbols.Remove( ppName ) )
		// 			changed = true;
		// 	}
		//
		// 	CheckRpSymbol( RenderPipeline.URP );
		// 	CheckRpSymbol( RenderPipeline.HDRP );
		//
		// 	if( changed && ShapesIO.TryMakeAssetsEditable( ShapesIO.projectSettingsPath ) ) {
		// 		//Debug.Log( $"Shapes updated your project scripting define symbols since you seem to be using {rpTarget.PrettyName()}, I hope that's okay~" );
		// 		SetCurrentKeywords( symbols );
		// 	}
		// }

		#endif
	}

}
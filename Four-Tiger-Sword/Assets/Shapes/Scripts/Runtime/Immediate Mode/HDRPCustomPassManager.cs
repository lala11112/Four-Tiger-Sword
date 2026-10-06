using System.Collections.Generic;
using UnityEngine;

#if HDRP_INSTALLED
using UnityEngine.Rendering.HighDefinition;
#endif

// Shapes © Freya Holmér - https://twitter.com/FreyaHolmer/
// Website & Documentation - https://acegikmo.com/shapes/
namespace Shapes {

	#if HDRP_INSTALLED
	[ExecuteAlways] public class HDRPCustomPassManager : MonoBehaviour {

		static HDRPCustomPassManager instance;
		public static HDRPCustomPassManager Instance {
			get {
				if( instance == null ) {
					// no cached one, see if we can find it in the scene
					if( ( instance = FindAnyObjectByType<HDRPCustomPassManager>() ) == null ) {
						// nothing in the scene, create it
						GameObject go = new GameObject( "Shapes HDRP Manager" ) { hideFlags = HideFlags.DontSave };
						instance = go.AddComponent<HDRPCustomPassManager>();
					}
				}

				return instance;
			}
		}

		void OnEnable() => instance = this;
		void Awake() => instance = this;

		Dictionary<CustomPassInjectionPoint, CustomPassVolume> volumes = new Dictionary<CustomPassInjectionPoint, CustomPassVolume>();

		public void MakeSureVolumeExistsForInjectionPoint( CustomPassInjectionPoint injPt ) {
			if( volumes.ContainsKey( injPt ) )
				return;

			// not found in the dictionary - see if there is one on this object
			CustomPassVolume volume = null;
			foreach( CustomPassVolume v in GetComponents<CustomPassVolume>() ) {
				if( v.injectionPoint == injPt ) {
					volume = v;
					break;
				}
			}

			if( volume == null ) {
				// not found on this object, create it
				volume = gameObject.AddComponent<CustomPassVolume>();
				volume.injectionPoint = injPt;
				volume.hideFlags = HideFlags.DontSave; // don't serialize this pls
					#if UNITY_EDITOR
				volume.runInEditMode = true;
					#endif

				volume.AddPassOfType( typeof(ShapesRenderPassHdrp) ); // this pass branches internally
			}

			volumes[injPt] = volume;
		}

	}
	#endif

}
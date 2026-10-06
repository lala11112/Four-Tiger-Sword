#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

#if URP_INSTALLED
using UnityEngine.Rendering.Universal;
#endif

#if HDRP_INSTALLED
using UnityEngine.Rendering.HighDefinition;
#endif


// Shapes © Freya Holmér - https://twitter.com/FreyaHolmer/
// Website & Documentation - https://acegikmo.com/shapes/
namespace Shapes {

	public class DrawCommand : System.IDisposable {

		#region static stuff

		static int bufferID;
		static int drawCommandWriteNestLevel;
		internal static bool IsAddingDrawCommandsToBuffer => drawCommandWriteNestLevel > 0;
		internal static DrawCommand CurrentWritingCommandBuffer => cBuffersWriting.Peek();

		static Stack<DrawCommand> cBuffersWriting = new Stack<DrawCommand>(); // while adding commands
		internal static Dictionary<Camera, List<DrawCommand>> cBuffersRendering = new Dictionary<Camera, List<DrawCommand>>(); // to avoid decentralized nullchecking for every onPostRender event

		static DrawCommand() {
			if( UnityInfo.CurrentRp == RenderPipeline.Legacy )
				Camera.onPostRender += OnPostRenderBuiltInRP;
			SceneManager.sceneUnloaded += scene => FlushNullCameras();
			#if UNITY_EDITOR
			AssemblyReloadEvents.beforeAssemblyReload += ClearAllCommands; // to prevent leaking when reloading scripts
			EditorSceneManager.sceneClosed += scene => FlushNullCameras();
			EditorSceneManager.activeSceneChangedInEditMode += ( sceneA, sceneB ) => FlushNullCameras();
			EditorSceneManager.newSceneCreated += ( scene, setup, mode ) => FlushNullCameras();
			#endif
		}

		/// <summary>Removes all DrawCommands currently submitted</summary>
		public static void ClearAllCommands() {
			FlushNullCameras(); // first remove all null cameras if any happened to go sad
			foreach( var drawCmds in cBuffersRendering.Values ) {
				drawCmds.ForEach( cmd => cmd.Clear() ); // clear the contents of each draw command
				drawCmds.Clear(); // clear list of commands on this camera
			}

			cBuffersRendering.Clear(); // clear the per-camera commands list
		}


		// clean up any cameras that were unloaded or destroyed
		public static void FlushNullCameras() {
			var sadCameras = cBuffersRendering.Where( kvp => kvp.Key == null ).ToList();
			foreach( var kvp in sadCameras ) {
				kvp.Value.ForEach( cmd => cmd.Clear() );
				cBuffersRendering.Remove( kvp.Key );
			}
		}

		static void RegisterCommand( DrawCommand cmd ) {
			switch( UnityInfo.CurrentRp ) {
				case RenderPipeline.Legacy:
					// if we're in built-in RP, then we need to explicitly add this to the camera
					cmd.AddToCamera();
					break;
				case RenderPipeline.HDRP:
					// make sure we have the volumes ready to read from the rendering buffers
					#if HDRP_INSTALLED
					HDRPCustomPassManager.Instance.MakeSureVolumeExistsForInjectionPoint( cmd.camEvtHdrp );
					#endif
					break;
				case RenderPipeline.URP: break; // shapes import script will ensure you have the pass added to your renderer, the rest is already handled
				default:                 throw new ArgumentOutOfRangeException();
			}

			if( cBuffersRendering.TryGetValue( cmd.cam, out List<DrawCommand> list ) == false ) {
				cBuffersRendering.Add( cmd.cam, list = new List<DrawCommand>() );
			}

			list.Add( cmd );
		}

		#if URP_INSTALLED || HDRP_INSTALLED
		internal static void OnCommandRendered( DrawCommand cmd ) {
			cmd.hasRendered = true;
			if( cBuffersRendering.TryGetValue( cmd.cam, out List<DrawCommand> drawCmds ) ) {
				cmd.Clear();
				drawCmds.Remove( cmd );
			} else
				Debug.LogError( $"Tried to remove unlisted draw command {cmd.id}" );
		}
		#endif

		// this is all extremely cursed but there is literally no way to know when a command is done drawing in the built-in RP
		static void OnPostRenderBuiltInRP( Camera cam ) {
			if( cBuffersRendering.TryGetValue( cam, out List<DrawCommand> drawCmds ) ) {
				for( int i = drawCmds.Count - 1; i >= 0; i-- ) {
					if( drawCmds[i].CheckIfRenderIsDone() ) {
						drawCmds[i].Clear();
						drawCmds.RemoveAt( i );
					}
				}
			}
		}

		#endregion

		bool hasValidCamera;
		internal bool hasRendered; // set by the static events above
		internal int id;
		bool pushPopState;
		Camera cam;
		internal readonly List<int> cachedTextIds = new List<int>();
		internal readonly List<Object> cachedAssets = new List<Object>();
		internal readonly List<DisposableMesh> cachedMeshes = new List<DisposableMesh>();
		internal readonly List<ShapeDrawCall> drawCalls = new List<ShapeDrawCall>();
		int camEvt = 0;

		#if URP_INSTALLED
		public RenderPassEvent camEvtUrp {
			get => (RenderPassEvent)camEvt;
			set => camEvt = (int)value;
		}
		#endif

		#if HDRP_INSTALLED
		public CustomPassInjectionPoint camEvtHdrp {
			get => (CustomPassInjectionPoint)camEvt;
			set => camEvt = (int)value;
		}
		#endif

		public CameraEvent camEvtBirp {
			get => (CameraEvent)camEvt;
			set => camEvt = (int)value;
		}

		#if URP_INSTALLED
		internal DrawCommand Initialize( Camera cam, RenderPassEvent cameraEvent = RenderPassEvent.BeforeRenderingPostProcessing ) => Initialize( cam, (int)cameraEvent );
		#endif
		#if HDRP_INSTALLED
		internal DrawCommand Initialize( Camera cam, CustomPassInjectionPoint cameraEvent = CustomPassInjectionPoint.BeforePostProcess ) => Initialize( cam, (int)cameraEvent );
		#endif

		internal DrawCommand Initialize( Camera cam, CameraEvent cameraEvent = CameraEvent.BeforeImageEffects ) => Initialize( cam, (int)cameraEvent );

		DrawCommand Initialize( Camera cam, int cameraEvent ) {
			this.cam = cam;
			this.id = bufferID++;
			hasValidCamera = cam != null;
			if( hasValidCamera == false )
				Debug.LogWarning( $"null camera passed into {nameof(DrawCommand)}, nothing will be drawn" );
			this.camEvt = cameraEvent;
			cBuffersWriting.Push( this );
			drawCommandWriteNestLevel++;
			pushPopState = ShapesConfig.Instance.pushPopStateInDrawCommands;
			if( pushPopState )
				Draw.Push();
			return this;
		}

		#if URP_INSTALLED
		internal void AppendToBuffer( RasterCommandBuffer cmd ) {
			foreach( ShapeDrawCall draw in drawCalls )
				draw.AddToCommandBuffer( cmd );
		}
		#endif

		internal void AppendToBuffer( CommandBuffer cmd ) {
			foreach( ShapeDrawCall draw in drawCalls )
				draw.AddToCommandBuffer( cmd );
		}

		void Clear() { // prepares for removing it from the list, if we want to delete it
			CleanupCachedAssetsAndMeshes();
			if( UnityInfo.CurrentRp == RenderPipeline.Legacy )
				RemoveFromCamera();
			hasRendered = false;
			for( int i = 0; i < drawCalls.Count; i++ )
				drawCalls[i].Cleanup();
			drawCalls.Clear();
			ObjectPool<DrawCommand>.Free( this );
		}

		void CleanupCachedAssetsAndMeshes() {
			foreach( int i in cachedTextIds )
				ShapesTextPool.Instance.ReleaseElement( i );
			cachedTextIds.Clear();
			foreach( Object asset in cachedAssets )
				asset.DestroyBranched();
			cachedAssets.Clear();
			foreach( DisposableMesh mesh in cachedMeshes )
				mesh.ReleaseFromCommand( this );
			cachedMeshes.Clear();
		}

		public void Dispose() {
			// make sure the last mpb gets added if it exists
			if( IMDrawer.metaMpbPrevious != null && IMDrawer.metaMpbPrevious.HasContent )
				drawCalls.Add( IMDrawer.metaMpbPrevious.ExtractDrawCall() );
			if( UnityInfo.CurrentRp == RenderPipeline.HDRP ) {
				#if HDRP_INSTALLED
				RegisterCommand( this );
				#endif
			} else {
				if( hasValidCamera ) RegisterCommand( this );
			}

			drawCommandWriteNestLevel--;
			cBuffersWriting.Pop();
			if( pushPopState )
				Draw.Pop();
		}

		// only built-in RP adds buffers directly to cameras
		CommandBuffer cmdBufBirp;

		// Built-in RP only
		void AddToCamera() {
			cmdBufBirp = ObjectPool<CommandBuffer>.Alloc();
			cmdBufBirp.name = "Shapes Command Buffer";
			AppendToBuffer( cmdBufBirp );
			cam.AddCommandBuffer( camEvtBirp, cmdBufBirp );
		}

		// Built-in RP only
		private bool CheckIfRenderIsDone() {
			if( hasRendered ) return true; // done
			hasRendered = true;
			return false; // not done
		}

		// Built-in RP only
		void RemoveFromCamera() {
			if( cam != null )
				cam.RemoveCommandBuffer( camEvtBirp, cmdBufBirp );
			cmdBufBirp.Clear();
			ObjectPool<CommandBuffer>.Free( cmdBufBirp );
			cmdBufBirp = null;
		}

	}

}
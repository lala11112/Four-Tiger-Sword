using System.Collections.Generic;
using UnityEngine.Rendering;

#if URP_INSTALLED || HDRP_INSTALLED
using UnityEngine.Rendering.RenderGraphModule;
#endif

#if HDRP_INSTALLED
using UnityEngine.Rendering.HighDefinition;
#endif

#if URP_INSTALLED
using UnityEngine.Rendering.Universal;
#endif

// Shapes © Freya Holmér - https://twitter.com/FreyaHolmer/
// Website & Documentation - https://acegikmo.com/shapes/
namespace Shapes {

	#if URP_INSTALLED
	internal class ShapesRenderPassUrp : ScriptableRenderPass {
		DrawCommand drawCommand;
		readonly CommandBuffer cmdBuf = new CommandBuffer();

		public ShapesRenderPassUrp Init( DrawCommand drawCommand ) {
			this.drawCommand = drawCommand;
			renderPassEvent = drawCommand.camEvtUrp;
			return this;
		}

		private class PassData {
			public DrawCommand drawCommand;
		}

		public override void RecordRenderGraph( RenderGraph renderGraph, ContextContainer frameData ) {
			using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<PassData>( "Render Shapes", out PassData data );
			data.drawCommand = drawCommand;
			builder.AllowPassCulling( false );
			UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
			builder.SetRenderAttachment( resourceData.activeColorTexture, 0, AccessFlags.Write );
			builder.SetRenderAttachmentDepth( resourceData.activeDepthTexture, AccessFlags.Write );
			builder.SetRenderFunc( ( PassData dataParam, RasterGraphContext context ) => {
					dataParam.drawCommand.AppendToBuffer( context.cmd );
				}
			);
		}

		// Unity 6.3 has put Execute() behind the URP_COMPATIBILITY_MODE preprocessor define
		#if !UNITY_6000_3_OR_NEWER || URP_COMPATIBILITY_MODE
		[System.Obsolete( "This rendering path is for compatibility mode only (when Render Graph is disabled)", false )]
		public override void Execute( ScriptableRenderContext context, ref RenderingData renderingData ) {
			drawCommand.AppendToBuffer( cmdBuf );
			context.ExecuteCommandBuffer( cmdBuf );
			cmdBuf.Clear();
		}
		#endif

		public override void FrameCleanup( CommandBuffer cmd ) {
			DrawCommand.OnCommandRendered( drawCommand );
			drawCommand = null;
			ObjectPool<ShapesRenderPassUrp>.Free( this );
		}
	}
	#endif

	#if HDRP_INSTALLED
	public class ShapesRenderPassHdrp : CustomPass {
		protected override void Setup( ScriptableRenderContext renderContext, CommandBuffer cmd ) => this.name = "Shapes Render Pass";
		// HDRP doesn't have ScriptableRenderPass stuff, so we use *one* custom pass per injection point, but branch inside of it instead
		// this does mean there will be redundancy/overhead in the way this is done, but, can't do much about it for now I think
		static readonly List<DrawCommand> executingCommands = new List<DrawCommand>();

		protected override void Execute( CustomPassContext passContext ) {
			ScriptableRenderContext context = passContext.renderContext;
			CommandBuffer cmd = passContext.cmd;
			HDCamera hdCamera = passContext.hdCamera;

			if( DrawCommand.cBuffersRendering.TryGetValue( hdCamera.camera, out List<DrawCommand> cmds ) ) {
				for( int i = 0; i < cmds.Count; i++ ) {
					if( cmds[i].camEvtHdrp == injectionPoint ) {
						executingCommands.Add( cmds[i] );
						cmds[i].AppendToBuffer( cmd );
					}
				}
			}

			// if we added commands, execute them immediately
			if( executingCommands.Count > 0 ) {
				context.ExecuteCommandBuffer( cmd ); // we have to execute it because OnCommandRendered might want to destroy used materials
				cmd.Clear();
				foreach( DrawCommand drawCommand in executingCommands )
					DrawCommand.OnCommandRendered( drawCommand ); // deletes cached assets
			}
			executingCommands.Clear();
		}
	}
	#endif

}
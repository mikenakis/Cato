namespace Cato;

using System.Collections.Immutable;
using MikeNakis.Kit;
using MikeNakis.Kit.FileSystem;
using static Microsoft.AspNetCore.Builder.DefaultFilesExtensions;
using static Microsoft.AspNetCore.Builder.HostFilteringBuilderExtensions;
using static Microsoft.AspNetCore.Builder.StaticFileExtensions;
using static Microsoft.AspNetCore.Builder.UseExtensions;
using static Microsoft.AspNetCore.Builder.WebSocketMiddlewareExtensions;
using static Microsoft.AspNetCore.Hosting.HostingAbstractionsWebHostBuilderExtensions;
using static Microsoft.AspNetCore.Http.SendFileResponseExtensions;
using static Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions;
using static MikeNakis.Kit.GlobalStatics;
using AspBuilder = Microsoft.AspNetCore.Builder;
using AspDepInj = Microsoft.Extensions.DependencyInjection;
using AspHttp = Microsoft.AspNetCore.Http;
using AspLog = Microsoft.Extensions.Logging;
using AspLogDebug = Microsoft.Extensions.Logging.Debug;
using Log = MikeNakis.Kit.Log;
using SysNetWebSock = System.Net.WebSockets;
using SysTask = System.Threading.Tasks;
using SysText = System.Text;
using SysThread = System.Threading;
using System.Collections.Concurrent;
using Sys = System;

sealed class CatoWebServer : Sys.IDisposable
{
	readonly LifeGuard lifeGuard = LifeGuard.Create();
	readonly ThreadGuard threadGuard = ThreadGuard.Create();
	//readonly DirectoryPath webRoot;
	//readonly string hostName;
	//readonly int portNumber;
	readonly AspBuilder.WebApplication app;
	readonly ConcurrentDictionary<SysNetWebSock.WebSocket, SysNetWebSock.WebSocket> webSockets = new();

	public CatoWebServer( DirectoryPath webRoot, string hostName, int portNumber )
	{
		//this.webRoot = webRoot;
		//this.hostName = hostName;
		//this.portNumber = portNumber;

		// from Microsoft Learn
		//     https://learn.microsoft.com/en-us/aspnet/core/fundamentals/websockets?view=aspnetcore-9.0
		// an alternative but similar implementation is here:
		//     https://www.tabsoverspaces.com/233883-simple-websocket-client-and-server-application-using-dotnet
		AspBuilder.WebApplicationBuilder builder = AspBuilder.WebApplication.CreateBuilder( new AspBuilder.WebApplicationOptions() { WebRootPath = webRoot.Path } );
		builder.WebHost.UseUrls( $"http://{hostName}:{portNumber}" );
		builder.Logging.Services.RemoveAll<AspLog.ILoggerProvider>();
		builder.Logging.Services.TryAddEnumerable( AspDepInj.ServiceDescriptor.Singleton<AspLog.ILoggerProvider, AspLogDebug.DebugLoggerProvider>() );
		app = builder.Build();
		app.UseWebSockets();
		app.UseHostFiltering();
		app.UseDefaultFiles( new AspBuilder.DefaultFilesOptions() { DefaultFileNames = ImmutableArray.Create( "index.html" ) } );
		app.UseStaticFiles( new AspBuilder.StaticFileOptions()
		{
			FileProvider = new MyFileProvider( webRoot.Path ),
			RedirectToAppendTrailingSlash = true,
			ServeUnknownFileTypes = true
		} );
		app.Use( async ( context, next ) =>
		{
			if( context.Request.Path == "/live-reload-websocket" )
			{
				await doWebSocket( context );
				return;
			}
			if( context.Request.Path == "/live-reload.js" )
			{
				await serveLiveReloadJs( context );
				return;
			}
			await next( context );
		} );
		app.RunAsync();
	}

	public void Dispose()
	{
		Assert( lifeGuard.IsAliveAssertion() );
		Assert( threadGuard.InThreadAssertion() );
		lifeGuard.Dispose();
		SysTask.Task.Run( async () => app.DisposeAsync() );
		Sys.IDisposable appAsDisposable = app;
		appAsDisposable.Dispose();
	}

	async SysTask.Task doWebSocket( AspHttp.HttpContext context )
	{
		string connectionId = $"{context.Connection.RemoteIpAddress}:{context.Connection.RemotePort}";
		Log.Debug( $"Connection {connectionId}: Established." );
		if( !context.WebSockets.IsWebSocketRequest )
		{
			Log.Debug( $"Connection {connectionId}: Not a web socket request! {context}" );
			context.Response.StatusCode = AspHttp.StatusCodes.Status400BadRequest;
			return;
		}
		using( SysNetWebSock.WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync() )
		{
			Log.Debug( $"Connection {connectionId}: WebSocket accepted." );
			bool ok = webSockets.TryAdd( webSocket, webSocket );
			Assert( ok );
			await runReceiver( webSocket );
			Log.Debug( $"Connection {connectionId}: WebSocket done." );
		}

		// PEARL: A webSocket does not detect that it has been disconnected unless there is a read pending on it.
		//     So, we have to keep a pending read on the webSocket even though we do not care to read anything from it.
		async SysTask.Task runReceiver( SysNetWebSock.WebSocket webSocket )
		{
			byte[] buffer = new byte[16];
			while( true )
			{
				SysNetWebSock.WebSocketReceiveResult result = await webSocket.ReceiveAsync( buffer, SysThread.CancellationToken.None );
				if( result.MessageType == SysNetWebSock.WebSocketMessageType.Close )
				{
					Log.Debug( $"Connection {connectionId}: WebSocket closed: {result.CloseStatus} {result.CloseStatusDescription}" );
					bool ok = webSockets.TryRemove( webSocket, out SysNetWebSock.WebSocket? _ );
					Assert( ok );
					break;
				}
				else
					Log.Debug( $"Connection {connectionId}: {result.MessageType}; EndOfMessage={result.EndOfMessage}" );
			}
		}
	}

	static async SysTask.Task serveLiveReloadJs( AspHttp.HttpContext context )
	{
		FilePath liveReloadJavascriptFilePath = DotNetHelpers.GetMainModuleDirectoryPath().File( "live-reload.js" );
		context.Response.ContentType = "text/javascript";
		await context.Response.SendFileAsync( liveReloadJavascriptFilePath.Path );
	}

	public void NotifyContentChanged()
	{
		Assert( lifeGuard.IsAliveAssertion() );
		Assert( threadGuard.InThreadAssertion() );
		Log.Debug( "Sending refresh message to all WebSockets..." );
		byte[] bytes = SysText.Encoding.ASCII.GetBytes( "refresh" );
		foreach( SysNetWebSock.WebSocket webSocket in webSockets.Values )
			webSocket.SendAsync( bytes, SysNetWebSock.WebSocketMessageType.Text, true, SysThread.CancellationToken.None );
	}
}

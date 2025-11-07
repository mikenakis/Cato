namespace Cato;

using System.Collections.Immutable;
using MikeNakis.Clio.Extensions;
using MikeNakis.Kit;
using MikeNakis.Kit.Extensions;
using MikeNakis.Kit.FileSystem;
using static System.Reflection.CustomAttributeExtensions;
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
using Clio = MikeNakis.Clio;
using Log = MikeNakis.Kit.Log;
using Sys = System;
using SysIo = System.IO;
using SysNetWebSock = System.Net.WebSockets;
using SysReflect = System.Reflection;
using SysTask = System.Threading.Tasks;
using SysText = System.Text;
using SysThread = System.Threading;

public sealed class CatoMain
{
	static void Main( string[] arguments )
	{
		Clio.ArgumentParser argumentParser = new();
		Clio.IOptionArgument<string> hostNameArgument = argumentParser.AddStringOptionWithDefault( "host-name", "localhost", 'c', "The host name to serve at", "name" );
		Clio.IOptionArgument<int> portNumberArgument = argumentParser.AddOptionWithDefault( "port-number", Clio.IntCodec.Instance, 8080, 'p', "The port number to serve at", "number" );
		Clio.IPositionalArgument<string> contentDirectoryArgument = argumentParser.AddStringPositionalWithDefault( "content-directory", ".", "The directory containing the files to serve" );
		argumentParser.TryParse( arguments );
		DirectoryPath contentDirectory = DirectoryPath.FromAbsoluteOrRelativePath( contentDirectoryArgument.Value, DotNetHelpers.GetWorkingDirectoryPath() );
		Sys.Console.WriteLine( getVersionInformation() );
		Sys.Console.WriteLine( $"Serving '{contentDirectory}'" );
		Sys.Console.WriteLine( $"On 'http://{hostNameArgument.Value}:{portNumberArgument.Value}'" );
		AwaitableEvent awaitableEvent = new();
		LoggingAction loggingAction = new LoggingAction( awaitableEvent.Trigger, "Change detected" );
		using( Hysterator hysterator = new( Sys.TimeSpan.FromSeconds( 0.5 ), loggingAction.EntryPoint ) )
		{
			startFileSystemWatcher( contentDirectory, hysterator.Action );
			startWebServer( contentDirectory, hostNameArgument.Value, portNumberArgument.Value, awaitableEvent.Awaitable );
			Sys.Console.WriteLine( "Press [Enter] to terminate: " );
			Sys.Console.ReadLine();
		}
	}

	static string getVersionInformation()
	{
		// PEARL: Unlike all other attributes defined in AssemblyInfo.cs, the "AssemblyVersion" attribute is **_not_** stored in
		// the assembly. Therefore, `assembly.GetCustomAttribute<SysReflect.AssemblyVersionAttribute>()` will always return `null`.
		SysReflect.Assembly assembly = SysReflect.Assembly.GetExecutingAssembly();
		string v1 = $"{assembly.GetCustomAttribute<SysReflect.AssemblyInformationalVersionAttribute>()?.InformationalVersion}";
		string v2 = $"{assembly.GetCustomAttribute<SysReflect.AssemblyFileVersionAttribute>()?.Version}";
		return $"{getAssemblyName()} v1={v1} v2={v2}\r\n";
		// Without GitInfo: v1=1.0.0+0a4715fab1005a120cfd5fdc69dffc1cf08a10bb v2=1.0.0.0
		// With GitInfo: v1=1.0.0+0a4715fab1005a120cfd5fdc69dffc1cf08a10bb v2=1.0.0.0
		//+ $" BaseVersion={ThisAssembly.Git.BaseVersion.Major}.{ThisAssembly.Git.BaseVersion.Minor}.{ThisAssembly.Git.BaseVersion.Patch}\r\n"
		//+ $" SemVer={ThisAssembly.Git.SemVer.Major}.{ThisAssembly.Git.SemVer.Minor}.{ThisAssembly.Git.SemVer.Patch}\r\n"
		//+ $" Source={ThisAssembly.Git.SemVer.Source} DashLabel='{ThisAssembly.Git.SemVer.DashLabel}' Label='{ThisAssembly.Git.SemVer.Label}'\r\n"
		//+ $" Branch={ThisAssembly.Git.Branch} Commits={ThisAssembly.Git.Commits} CommitDate={ThisAssembly.Git.CommitDate}\r\n"
		//+ $" Tag={ThisAssembly.Git.Tag} BaseTag={ThisAssembly.Git.BaseTag} Sha={ThisAssembly.Git.Sha}";

		static string getAssemblyName()
		{
			SysReflect.Assembly assembly = SysReflect.Assembly.GetExecutingAssembly();
			SysReflect.AssemblyName assemblyName = assembly.GetName();
			return assemblyName.Name.OrThrow();
		}
	}

	static SysIo.FileSystemWatcher startFileSystemWatcher( DirectoryPath directoryPath, Sys.Action waitableTaskTrigegr )
	{
		SysIo.FileSystemWatcher fileSystemWatcher = new();
		fileSystemWatcher.Path = directoryPath.Path;
		fileSystemWatcher.IncludeSubdirectories = true;
		//fileSystemWatcher.Filter                = "*";
		fileSystemWatcher.NotifyFilter = SysIo.NotifyFilters.FileName | //SysIo.NotifyFilters.Attributes | SysIo.NotifyFilters.LastAccess |
				SysIo.NotifyFilters.DirectoryName | //
				SysIo.NotifyFilters.Size |
				SysIo.NotifyFilters.LastWrite |
				SysIo.NotifyFilters.CreationTime |
				SysIo.NotifyFilters.Security;
		fileSystemWatcher.Changed += onFileSystemWatcherNormalEvent;
		fileSystemWatcher.Created += onFileSystemWatcherNormalEvent;
		fileSystemWatcher.Deleted += onFileSystemWatcherNormalEvent;
		fileSystemWatcher.Error += onFileSystemWatcherErrorEvent;
		fileSystemWatcher.Renamed += onFileSystemWatcherNormalEvent;
		fileSystemWatcher.EnableRaisingEvents = true;
		return fileSystemWatcher;

		void onFileSystemWatcherNormalEvent( object sender, SysIo.FileSystemEventArgs e )
		{
			Assert( sender == fileSystemWatcher );
			Log.Debug( $"{e.ChangeType} {e.FullPath}" );
			waitableTaskTrigegr.Invoke();
		}

		void onFileSystemWatcherErrorEvent( object sender, SysIo.ErrorEventArgs e )
		{
			Assert( sender == fileSystemWatcher );
			Log.Warn( $"{directoryPath}", e.GetException() );
		}
	}

	static void startWebServer( DirectoryPath webRoot, string hostName, int portNumber, Awaitable awaitable )
	{
		// from Microsoft Learn
		//     https://learn.microsoft.com/en-us/aspnet/core/fundamentals/websockets?view=aspnetcore-9.0
		// an alternative, but very similar, implementation is here:
		//     https://www.tabsoverspaces.com/233883-simple-websocket-client-and-server-application-using-dotnet
		AspBuilder.WebApplicationBuilder builder = AspBuilder.WebApplication.CreateBuilder( new AspBuilder.WebApplicationOptions() { WebRootPath = webRoot.Path } );
		builder.WebHost.UseUrls( $"http://{hostName}:{portNumber}" );
		builder.Logging.Services.RemoveAll<AspLog.ILoggerProvider>();
		builder.Logging.Services.TryAddEnumerable( AspDepInj.ServiceDescriptor.Singleton<AspLog.ILoggerProvider, AspLogDebug.DebugLoggerProvider>() );
		AspBuilder.WebApplication app = builder.Build();
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
				await doWebSocket( context, awaitable );
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

	static async SysTask.Task doWebSocket( AspHttp.HttpContext context, Awaitable awaitable )
	{
		if( !context.WebSockets.IsWebSocketRequest )
		{
			context.Response.StatusCode = AspHttp.StatusCodes.Status400BadRequest;
			return;
		}
		Log.Debug( $"Connected: {context.Connection.RemoteIpAddress}:{context.Connection.RemotePort}" );
		using( SysNetWebSock.WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync() )
		{
			_ = runReceiver( webSocket );
			await runTransmitter( webSocket, awaitable );
		}

		// PEARL: a webSocket does not detect that it has been disconnected unless there is a read pending on it.
		//     Thus, we have to have this method which keeps a pending read on the webSocket even though we have no
		//     use for reading anything from it.
		static async SysTask.Task runReceiver( SysNetWebSock.WebSocket webSocket )
		{
			byte[] buffer = new byte[16];
			while( true )
			{
				SysNetWebSock.WebSocketReceiveResult result = await webSocket.ReceiveAsync( buffer, SysThread.CancellationToken.None );
				if( result.MessageType == SysNetWebSock.WebSocketMessageType.Close )
				{
					Log.Debug( $"Socket closed: {result.CloseStatus} {result.CloseStatusDescription}" );
					break;
				}
			}
		}

		static async SysTask.Task runTransmitter( SysNetWebSock.WebSocket webSocket, Awaitable awaitable )
		{
			while( true )
			{
				await awaitable.Invoke();
				Log.Debug( "WebSocket server received change event." );
				if( webSocket.State != SysNetWebSock.WebSocketState.Open )
				{
					Log.Debug( $"WebSocket state is {webSocket.State}, aborting." );
					break;
				}
				Log.Debug( "Sending refresh message to websocket client..." );
				byte[] bytes = SysText.Encoding.ASCII.GetBytes( "refresh" );
				await webSocket.SendAsync( bytes, SysNetWebSock.WebSocketMessageType.Text, true, SysThread.CancellationToken.None );
			}
		}
	}

	static async SysTask.Task serveLiveReloadJs( AspHttp.HttpContext context )
	{
		FilePath liveReloadJavascriptFilePath = DotNetHelpers.GetMainModuleDirectoryPath().File( "live-reload.js" );
		context.Response.ContentType = "text/javascript";
		await context.Response.SendFileAsync( liveReloadJavascriptFilePath.Path );
	}
}

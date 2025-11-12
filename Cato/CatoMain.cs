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
		Sys.Console.WriteLine( getVersionInformation() );
		Clio.ArgumentParser argumentParser = new();
		Clio.IOptionArgument<string> hostNameArgument = argumentParser.AddStringOptionWithDefault( "host-name", "localhost", 'c', "The host name to serve at", "name" );
		Clio.IOptionArgument<int> portNumberArgument = argumentParser.AddOptionWithDefault( "port-number", Clio.IntCodec.Instance, 8080, 'p', "The port number to serve at", "number" );
		Clio.IPositionalArgument<string> contentDirectoryArgument = argumentParser.AddStringPositionalWithDefault( "content-directory", ".", "The directory containing the files to serve" );
		argumentParser.TryParse( arguments );
		DirectoryPath contentDirectory = DirectoryPath.FromAbsoluteOrRelativePath( contentDirectoryArgument.Value, DotNetHelpers.GetWorkingDirectoryPath() );
		string hostName = hostNameArgument.Value;
		int portNumber = portNumberArgument.Value;
		run( contentDirectory, hostName, portNumber );
	}

	static void run( DirectoryPath contentDirectory, string hostName, int portNumber )
	{
		Sys.Console.WriteLine( $"Serving '{contentDirectory}'" );
		Sys.Console.WriteLine( $"On 'http://{hostName}:{portNumber}'" );
		AwaitableEvent awaitableEvent = new();
		LoggingAction loggingAction = new LoggingAction( awaitableEvent.Trigger, "Change detected" );
		using( Hysterator hysterator = new( Sys.TimeSpan.FromSeconds( 0.5 ), loggingAction.EntryPoint ) )
		{
			startFileSystemWatcher( contentDirectory, hysterator.Action );
			startWebServer( contentDirectory, hostName, portNumber, awaitableEvent.Awaitable );
			Sys.Console.WriteLine( "Press [Enter] to terminate: " );
			Sys.Console.ReadLine();
		}
	}

	static string getVersionInformation()
	{
		SysText.StringBuilder stringBuilder = new();
		SysReflect.Assembly assembly = SysReflect.Assembly.GetExecutingAssembly();
		stringBuilder.Append( assembly.GetName().Name.OrThrow() ).Append( ' ' );
		stringBuilder.Append( assembly.GetCustomAttribute<SysReflect.AssemblyFileVersionAttribute>().OrThrow().Version );
		return stringBuilder.ToString();

		//Log.Debug( $"GitInfo:" );
		//// PEARL: Unlike all other attributes defined in AssemblyInfo.cs, the "AssemblyVersion" attribute is **_not_**
		//// stored in the assembly. Therefore, `assembly.GetCustomAttribute<SysReflect.AssemblyVersionAttribute>()` will
		//// always return `null`.
		//Log.Debug( $"  AssemblyInformationalVersion={SysReflect.Assembly.GetExecutingAssembly().GetCustomAttribute<SysReflect.AssemblyInformationalVersionAttribute>()?.InformationalVersion}" );
		//Log.Debug( $"  AssemblyFileVersion={SysReflect.Assembly.GetExecutingAssembly().GetCustomAttribute<SysReflect.AssemblyFileVersionAttribute>()?.Version}" );
		//// Without GitInfo: v1=1.0.0+0a4715fab1005a120cfd5fdc69dffc1cf08a10bb v2=1.0.0.0
		//// With GitInfo: v1=5.1.27+master.f0b5bd7.f0b5bd745a763c94e55cfff3ecb514ca16a8f8f1 v2=5.1.27.0
		//Log.Debug( $"  BaseVersion={ThisAssembly.Git.BaseVersion.Major}.{ThisAssembly.Git.BaseVersion.Minor}.{ThisAssembly.Git.BaseVersion.Patch}" );
		//Log.Debug( $"  SemVer={ThisAssembly.Git.SemVer.Major}.{ThisAssembly.Git.SemVer.Minor}.{ThisAssembly.Git.SemVer.Patch}" );
		//Log.Debug( $"  Source={ThisAssembly.Git.SemVer.Source} DashLabel='{ThisAssembly.Git.SemVer.DashLabel}' Label='{ThisAssembly.Git.SemVer.Label}'" );
		//Log.Debug( $"  Branch={ThisAssembly.Git.Branch} Commits={ThisAssembly.Git.Commits} CommitDate={ThisAssembly.Git.CommitDate}" );
		//Log.Debug( $"  Tag={ThisAssembly.Git.Tag} BaseTag={ThisAssembly.Git.BaseTag} Sha={ThisAssembly.Git.Sha}" );

		//SysText.StringBuilder stringBuilder = new();
		//stringBuilder.Append( getAssemblyName() ).Append( ' ' );
		//stringBuilder.Append( ThisAssembly.Git.SemVer.Major ).Append( '.' );
		//stringBuilder.Append( ThisAssembly.Git.SemVer.Minor ).Append( '.' );
		//stringBuilder.Append( ThisAssembly.Git.SemVer.Patch );
		//if( Identity( ThisAssembly.Git.Branch ) != "master" )
		//	stringBuilder.Append( '-' ).Append( ThisAssembly.Git.Branch );
		//if( Identity( ThisAssembly.Git.Commits ) != "0" )
		//	stringBuilder.Append( '+' ).Append( ThisAssembly.Git.Commits );
		//return stringBuilder.ToString();

		//static string getAssemblyName()
		//{
		//	SysReflect.Assembly assembly = SysReflect.Assembly.GetExecutingAssembly();
		//	SysReflect.AssemblyName assemblyName = assembly.GetName();
		//	return assemblyName.Name.OrThrow();
		//}
	}

	static SysIo.FileSystemWatcher startFileSystemWatcher( DirectoryPath directoryPath, Sys.Action observer )
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
			observer.Invoke();
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
		// an alternative but similar implementation is here:
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

		// PEARL: A webSocket does not detect that it has been disconnected unless there is a read pending on it.
		//     So, we have to keep a pending read on the webSocket even though we do not care to read anything from it.
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

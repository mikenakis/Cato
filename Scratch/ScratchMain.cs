namespace WebSocketTest;

using MikeNakis.Clio.Extensions;
using MikeNakis.Console;
using MikeNakis.Kit;
using MikeNakis.Kit.FileSystem;
using Clio = MikeNakis.Clio;
using Sys = System;
using SysNet = System.Net;

public sealed class DevWebServerMain
{
	static void Main( string[] arguments )
	{
		StartupProjectDirectory.Initialize();
		ConsoleHelpers.Run( false, () => run( arguments ) );
	}

	static int run( string[] arguments )
	{
		Clio.ArgumentParser argumentParser = new();
		Clio.IPositionalArgument<string> prefixArgument = argumentParser.AddStringPositionalWithDefault( "prefix", "http://localhost:8000/", "The host name and port to serve" );
		Clio.IPositionalArgument<string> webRootArgument = argumentParser.AddStringPositionalWithDefault( "web-root", ".", "The directory containing the files to serve" );
		if( !argumentParser.TryParse( arguments ) )
			return -1;
		var webRoot = DirectoryPath.FromAbsoluteOrRelativePath( webRootArgument.Value, DotNetHelpers.GetWorkingDirectoryPath() );
		Sys.Console.WriteLine( $"Serving '{webRoot}'" );
		Sys.Console.WriteLine( $"On '{prefixArgument.Value}'" );
		startWebSocketServer();
		using( var httpServer = new HttpServer( prefixArgument.Value, webRoot ) )
		{
			Sys.Console.Write( "Press [Enter] to terminate: " );
			Sys.Console.ReadLine();
		}
		return 0;
	}

	static void startWebSocketServer()
	{
		Server server = new Server( new SysNet.IPEndPoint( SysNet.IPAddress.Parse( "127.0.0.1" ), 8080 ) );
		server.OnClientConnected += ( object? sender, OnClientConnectedHandler e ) =>
		{
			Sys.Console.WriteLine( "Client with GUID: {0} Connected!", e.GetClient().GetGuid() );
		};
		server.OnClientDisconnected += ( object? sender, OnClientDisconnectedHandler e ) =>
		{
			Sys.Console.WriteLine( "Client {0} Disconnected", e.GetClient().GetGuid() );
		};
		server.OnMessageReceived += ( object? sender, OnMessageReceivedHandler e ) =>
		{
			Sys.Console.WriteLine( "Received Message: '{1}' from client: {0}", e.GetClient().GetGuid(), e.GetMessage() );
			e.GetClient().GetServer().SendMessage( e.GetClient(), $"{e.GetMessage()} back to you!" );
		};
		server.OnSendMessage += ( object? sender, OnSendMessageHandler e ) =>
		{
			Sys.Console.WriteLine( "Sent message: '{0}' to client {1}", e.GetMessage(), e.GetClient().GetGuid() );
		};
	}
}

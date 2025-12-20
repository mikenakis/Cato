namespace Cato;

using MikeNakis.Clio.Extensions;
using MikeNakis.Kit;
using MikeNakis.Kit.Extensions;
using MikeNakis.Kit.FileSystem;
using static System.Reflection.CustomAttributeExtensions;
using Clio = MikeNakis.Clio;
using Sys = System;
using SysReflect = System.Reflection;
using SysTask = System.Threading.Tasks;
using SysText = System.Text;

public sealed class CatoMain
{
	static readonly Sys.TimeSpan hysteresis = Sys.TimeSpan.FromSeconds( 0.5 );

	static void Main( string[] arguments )
	{
		Sys.Console.WriteLine( getVersionInformation() );
		Clio.ArgumentParser argumentParser = new();
		Clio.IOptionArgument<string> hostNameArgument = argumentParser.AddStringOptionWithDefault( "host-name", "localhost", 'c', "The host name to serve at", "name" );
		Clio.IOptionArgument<int> portNumberArgument = argumentParser.AddOptionWithDefault( "port-number", Clio.IntCodec.Instance, 8080, 'p', "The port number to serve at", "number" );
		Clio.IPositionalArgument<string> contentDirectoryArgument = argumentParser.AddStringPositionalWithDefault( "content-directory", ".", "The directory containing the files to serve" );
		if( !argumentParser.TryParse( arguments ) )
			Sys.Environment.Exit( 1 );
		DirectoryPath contentDirectory = DirectoryPath.FromAbsoluteOrRelativePath( contentDirectoryArgument.Value, DotNetHelpers.GetWorkingDirectoryPath() );
		string hostName = hostNameArgument.Value;
		int portNumber = portNumberArgument.Value;
		Sys.Console.WriteLine( $"Serving '{contentDirectory}'" );
		Sys.Console.WriteLine( $"On 'http://{hostName}:{portNumber}'" );
		using( EventDriver eventDriver = new() )
		{
			using( CatoWebServer catoWebServer = new( contentDirectory, hostName, portNumber ) )
			{
				using( Debouncer debouncer = new( hysteresis, () => eventDriver.Proxy.Post( catoWebServer.NotifyContentChanged ) ) )
				{
					using( DirectoryWatcher directoryWatcher = new( contentDirectory, () => eventDriver.Proxy.Post( debouncer.Action ) ) )
					{
						SysTask.Task.Run( () =>
						{
							waitForEscToTerminate();
							eventDriver.Proxy.PostQuit();
						} );
						eventDriver.Run();
					}
				}
			}
		}
	}

	static string getVersionInformation()
	{
		SysText.StringBuilder stringBuilder = new();
		SysReflect.Assembly assembly = SysReflect.Assembly.GetExecutingAssembly();
		stringBuilder.Append( assembly.GetName().Name.OrThrow() ).Append( ' ' );
		stringBuilder.Append( assembly.GetCustomAttribute<SysReflect.AssemblyFileVersionAttribute>().OrThrow().Version );
		return stringBuilder.ToString();
	}

	static void waitForEscToTerminate()
	{
		promptForEsc();
		while( true )
		{
			Sys.ConsoleKeyInfo keyInfo = Sys.Console.ReadKey( true );
			if( keyInfo.Key == Sys.ConsoleKey.Escape )
				break;
			promptForEsc();
		}
		return;

		static void promptForEsc()
		{
			Sys.Console.WriteLine();
			Sys.Console.Write( "Press [ESC] to terminate: " );
		}
	}
}

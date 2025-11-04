namespace WebSocketTest;

using MikeNakis.Kit;
using MikeNakis.Kit.Extensions;
using MikeNakis.Kit.FileSystem;
using static MikeNakis.Kit.GlobalStatics;
using Sys = System;
using SysDiag = System.Diagnostics;
using SysNet = System.Net;
using SysNetHttp = System.Net.Http;
using SysTask = System.Threading.Tasks;
using SysThread = System.Threading;

public class HttpServer : Sys.IDisposable
{
	readonly LifeGuard lifeGuard = LifeGuard.Create();
	readonly string prefix;
	readonly DirectoryPath webRoot;
	readonly SysThread.Thread thread;
	volatile bool running;

	public HttpServer( string prefix, DirectoryPath webRoot )
	{
		this.prefix = prefix;
		this.webRoot = webRoot;
		running = true;
		thread = new( () => invokeSafe( threadProcedure ) );
		thread.Start();
	}

	public void Dispose()
	{
		Assert( lifeGuard.IsAliveAssertion() );
		running = false;
		issueRequestAndIgnore( prefix );
		thread.Join();
		lifeGuard.Dispose();
	}

	static void issueRequestAndIgnore( string prefix )
	{
		using( SysNetHttp.HttpClient client = new() )
			try
			{
				SysTask.Task.Run( () => client.GetByteArrayAsync( prefix ) ).Wait();
			}
			catch( Sys.Exception )
			{
				/* swallow */
			}
	}

	static void invokeSafe( Sys.Action action )
	{
		if( SysDiag.Debugger.IsAttached )
			action.Invoke();
		else
			try
			{
				action.Invoke();
			}
			catch( Sys.Exception exception )
			{
				Log.Error( "Unhandled exception", exception );
			}
	}

	void threadProcedure()
	{
		using( var listener = new SysNet.HttpListener() )
		{
			listener.Prefixes.Add( prefix );
			listener.Start();

			while( true )
			{
				SysNet.HttpListenerContext context = listener.GetContext();
				if( !running )
					break;
				try
				{
					Log.Info( $"{context.Request.HttpMethod} {context.Request.Url}" );
					(int statusCode, string statusDescription) = process0( context.Request, context.Response );
					context.Response.StatusCode = statusCode;
					context.Response.StatusDescription = statusDescription;
					context.Response.Close();
				}
				catch( Sys.Exception exception )
				{
					Log.Error( "Http transaction failed miserably: ", exception );
				}
			}
		}
	}

	(int statusCode, string statusDescription) process0( SysNet.HttpListenerRequest request, SysNet.HttpListenerResponse response )
	{
		try
		{
			return process1( request, response );
		}
		catch( Sys.Exception exception )
		{
			Log.Error( "Internal server error: ", exception );
			return (500, "Internal Server Error");
		}
	}

	(int statusCode, string statusDescription) process1( SysNet.HttpListenerRequest request, SysNet.HttpListenerResponse response )
	{
		Assert( request.IsLocal );
		if( request.HttpMethod != "GET" )
		{
			Log.Info( $"Unknown HTTP method '{request.HttpMethod}'." );
			return (405, "Method not allowed");
		}

		string localPath = getLocalPath( request.RawUrl );
		FilePath filePath = webRoot.RelativeFile( localPath );
		if( !filePath.Exists() )
		{
			Log.Info( $"File not found: '{localPath}'." );
			return (404, "Not Found");
		}

		byte[] data = filePath.ReadAllBytes();
		response.ContentType = getMimeType( filePath );
		response.ContentEncoding = DotNetHelpers.BomlessUtf8;
		response.ContentLength64 = data.LongLength;
		response.OutputStream.Write( data );
		return (200, "OK");

		static string getLocalPath( string? rawUrl )
		{
			string url = rawUrl ?? "/";
			string localPath = url.EndsWith( '/' ) ? url + "index.html" : url;
			Assert( localPath.StartsWith2( "/" ) );
			return localPath[1..];
		}
	}

	static string getMimeType( FilePath filePath )
	{
		// from https://github.com/Microsoft/referencesource/blob/main/System.Web/MimeMapping.cs
		return filePath.Extension switch
		{
			".html" => "text/html",
			_ => "application/octet-stream"
		};
	}
}

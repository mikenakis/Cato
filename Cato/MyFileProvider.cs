namespace Cato;

using MikeNakis.Kit;
using MikeNakis.Kit.Extensions;
using static MikeNakis.Kit.GlobalStatics;
using AspFileProviders = Microsoft.Extensions.FileProviders;
using AspPrimitives = Microsoft.Extensions.Primitives;
using Log = MikeNakis.Kit.Log;
using Sys = System;
using SysIo = System.IO;

sealed class MyFileProvider : AspFileProviders.IFileProvider, Sys.IDisposable
{
	readonly LifeGuard lifeGuard = LifeGuard.Create();
	readonly AspFileProviders.PhysicalFileProvider delegee;

	public MyFileProvider( string root )
	{
		delegee = new( root );
	}

	public void Dispose()
	{
		Assert( lifeGuard.IsAliveAssertion() );
		delegee.Dispose();
		lifeGuard.Dispose();
	}

	public AspFileProviders.IDirectoryContents GetDirectoryContents( string subpath ) => delegee.GetDirectoryContents( subpath );

	public AspFileProviders.IFileInfo GetFileInfo( string subpath )
	{
		AspFileProviders.IFileInfo fileInfo = delegee.GetFileInfo( subpath );
		string? physicalPath = fileInfo.PhysicalPath;
		if( physicalPath == null || !physicalPath.EndsWith2( ".html" ) )
			return fileInfo;
		return new MyFileInfo( fileInfo );
	}

	public AspPrimitives.IChangeToken Watch( string filter ) => delegee.Watch( filter );

	sealed class MyFileInfo : AspFileProviders.IFileInfo
	{
		readonly AspFileProviders.IFileInfo delegee;
		readonly byte[] content;

		public MyFileInfo( AspFileProviders.IFileInfo delegee )
		{
			this.delegee = delegee;
			Assert( delegee.Exists );
			Assert( !delegee.IsDirectory );
			content = getContent( delegee );
		}

		static byte[] getContent( AspFileProviders.IFileInfo delegee )
		{
			long length = delegee.Length;
			Assert( length < int.MaxValue );
			byte[] bytes = new byte[length];
			using( SysIo.Stream stream = delegee.CreateReadStream() )
				stream.ReadExactly( bytes );
			string s = DotNetHelpers.BomlessUtf8.GetString( bytes );
			int i = s.IndexOf2( "</head>" );
			if( i == -1 )
			{
				Log.Warn( $"File {delegee.PhysicalPath} does not contain '</head>'" );
				return bytes;
			}
			while( i > 1 && char.IsWhiteSpace( s[i - 1] ) )
				i--;
			string newContent = s[0..i] + $"\r\n<!-- Inserted by {DotNetHelpers.GetProductName()} -->\r\n<script type=\"text/javascript\" src=\"live-reload.js\" defer=\"defer\"></script>" + s[i..];
			return DotNetHelpers.BomlessUtf8.GetBytes( newContent );
		}

		public bool Exists => true;

		public long Length => content.Length;

		// PEARL: A return value of 'null' is documented to mean that the file is "not directly accessible", which is
		//     a mystifying enigma.  Undocumentedly, it means that CreateReadStream() should be invoked.
		//     If a non-null value is returned, then asp.net will, undocumentedly, drectly fetch the file on its own,
		//     without invoking CreateReadStream().
		public string? PhysicalPath => null;

		public string Name => delegee.Name;

		public Sys.DateTimeOffset LastModified => delegee.LastModified;

		public bool IsDirectory => delegee.IsDirectory;

		public SysIo.Stream CreateReadStream()
		{
			Log.Debug( $"Serving file: {delegee.PhysicalPath}" );
			return new SysIo.MemoryStream( content );
		}
	}
}

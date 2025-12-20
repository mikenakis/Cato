namespace Cato;

using MikeNakis.Kit;
using MikeNakis.Kit.FileSystem;
using static MikeNakis.Kit.GlobalStatics;
using Log = MikeNakis.Kit.Log;
using Sys = System;
using SysIo = System.IO;

sealed class DirectoryWatcher : Sys.IDisposable
{
	readonly LifeGuard lifeGuard = LifeGuard.Create();
	readonly SysIo.FileSystemWatcher fileSystemWatcher = new();
	readonly DirectoryPath directoryPath;
	readonly Sys.Action observer;

	public DirectoryWatcher( DirectoryPath directoryPath, Sys.Action observer )
	{
		this.directoryPath = directoryPath;
		this.observer = observer;

		//PEARL: The documentation says "you can set the buffer to 4 KB or larger, but it must not exceed 64 KB."
		//    (See https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher.internalbuffersize)
		//    HOWEVER, experimentation shows that a buffer size of 65535 does not solve buffer full problems, but a
		//    buffer size of 1024 * 1024 does. Go figure.
		fileSystemWatcher.InternalBufferSize = 1024 * 1024; //65535;
		fileSystemWatcher.Path = directoryPath.Path;
		fileSystemWatcher.IncludeSubdirectories = true;
		//fileSystemWatcher.Filter                = "*";
		fileSystemWatcher.NotifyFilter = SysIo.NotifyFilters.FileName |
				// SysIo.NotifyFilters.Attributes |
				// SysIo.NotifyFilters.LastAccess |
				// SysIo.NotifyFilters.Security |
				SysIo.NotifyFilters.DirectoryName |
				SysIo.NotifyFilters.Size |
				SysIo.NotifyFilters.LastWrite |
				SysIo.NotifyFilters.CreationTime;
		fileSystemWatcher.Changed += onFileSystemWatcherNormalEvent;
		fileSystemWatcher.Created += onFileSystemWatcherNormalEvent;
		fileSystemWatcher.Deleted += onFileSystemWatcherNormalEvent;
		fileSystemWatcher.Error += onFileSystemWatcherErrorEvent;
		fileSystemWatcher.Renamed += onFileSystemWatcherNormalEvent;
		fileSystemWatcher.EnableRaisingEvents = true;
	}

	public void Dispose()
	{
		Assert( lifeGuard.IsAliveAssertion() );
		lifeGuard.Dispose();
		fileSystemWatcher.Dispose();
	}

	void onFileSystemWatcherNormalEvent( object sender, SysIo.FileSystemEventArgs e )
	{
		Assert( sender == fileSystemWatcher );
		//Log.Debug( $"{e.ChangeType} {e.FullPath}" );
		observer.Invoke();
	}

	void onFileSystemWatcherErrorEvent( object sender, SysIo.ErrorEventArgs e )
	{
		Assert( sender == fileSystemWatcher );
		Log.Warn( $"{directoryPath}", e.GetException() );
	}
}

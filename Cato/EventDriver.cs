namespace Cato;

using System.Collections.Concurrent;
using MikeNakis.Kit;
using static MikeNakis.Kit.GlobalStatics;
using Sys = System;
using SysThread = System.Threading;

public interface EventDriverProxy
{
	void Post( Sys.Action action );
	void PostQuit();
}

sealed class EventDriver : Sys.IDisposable
{
	sealed class MyProxy : EventDriverProxy
	{
		readonly EventDriver eventDriver;

		public MyProxy( EventDriver eventDriver )
		{
			this.eventDriver = eventDriver;
		}

		public void Post( Sys.Action action )
		{
			eventDriver.post( action );
		}

		public void PostQuit()
		{
			eventDriver.post( eventDriver.quit );
		}
	}

	readonly ThreadGuard threadGuard = ThreadGuard.Create();
	readonly LifeGuard lifeGuard = LifeGuard.Create();
	readonly ConcurrentQueue<Sys.Action> queue = new();
	readonly SysThread.AutoResetEvent autoResetEvent = new( false );
	bool running = true;
	public event Sys.Action? Idle;
	readonly MyProxy myProxy;

	public EventDriver()
	{
		myProxy = new( this );
	}

	public void Dispose()
	{
		Assert( threadGuard.InThreadAssertion() );
		Assert( lifeGuard.IsAliveAssertion() );
		autoResetEvent.Dispose();
		lifeGuard.Dispose();
	}

	public EventDriverProxy Proxy => myProxy;

	void post( Sys.Action action )
	{
		queue.Enqueue( action );
		autoResetEvent.Set();
	}

	public void PostQuit()
	{
		Assert( threadGuard.InThreadAssertion() );
		post( quit );
	}

	void quit()
	{
		running = false;
	}

	public void Run()
	{
		Assert( threadGuard.InThreadAssertion() );
		while( true )
		{
			while( queue.TryDequeue( out Sys.Action? action ) )
			{
				Assert( action != null );
				action.Invoke();
			}

			if( !running )
				break;

			Idle?.Invoke();
			autoResetEvent.WaitOne();
		}
	}
}

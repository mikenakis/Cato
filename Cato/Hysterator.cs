namespace Cato;

using MikeNakis.Kit;
using static MikeNakis.Kit.GlobalStatics;
using Sys = System;
using SysTask = System.Threading.Tasks;

sealed class Hysterator : Sys.IDisposable
{
	readonly LifeGuard lifeGuard = LifeGuard.Create();
	readonly Sys.TimeSpan delay;
	readonly Sys.Action action;
	volatile bool pending;

	public Hysterator( Sys.TimeSpan delay, Sys.Action action )
	{
		this.delay = delay;
		this.action = action;
	}

	public void Dispose()
	{
		Assert( lifeGuard.IsAliveAssertion() );
		lifeGuard.Dispose();
	}

	public void Action()
	{
		if( pending )
			return;
		pending = true;
		SysTask.Task.Run( async () =>
		{
			await SysTask.Task.Delay( delay );
			pending = false;
			action.Invoke();
		} );
	}
}

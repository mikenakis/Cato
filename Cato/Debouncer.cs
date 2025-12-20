namespace Cato;

using MikeNakis.Kit;
using static MikeNakis.Kit.GlobalStatics;
using Sys = System;
using SysTask = System.Threading.Tasks;

sealed class Debouncer : Sys.IDisposable
{
	readonly ThreadGuard threadGuard = ThreadGuard.Create();
	readonly LifeGuard lifeGuard = LifeGuard.Create();
	readonly Sys.TimeSpan hysteresis;
	readonly Sys.Action action;
	bool pending;

	public Debouncer( Sys.TimeSpan hysteresis, Sys.Action action )
	{
		this.hysteresis = hysteresis;
		this.action = action;
	}

	public void Dispose()
	{
		Assert( threadGuard.InThreadAssertion() );
		Assert( lifeGuard.IsAliveAssertion() );
		lifeGuard.Dispose();
	}

	public void Action()
	{
		Assert( threadGuard.InThreadAssertion() );
		Assert( lifeGuard.IsAliveAssertion() );
		if( pending )
			return;
		pending = true;
		SysTask.Task.Run( async () =>
		{
			await SysTask.Task.Delay( hysteresis );
			pending = false;
			action.Invoke();
		} );
	}
}

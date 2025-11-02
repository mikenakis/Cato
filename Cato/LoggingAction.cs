namespace Cato;

using Log = MikeNakis.Kit.Log;
using Sys = System;

sealed class LoggingAction
{
	readonly Sys.Action delegee;
	readonly string message;

	public LoggingAction( Sys.Action delegee, string message )
	{
		this.delegee = delegee;
		this.message = message;
	}

	public void EntryPoint()
	{
		Log.Debug( message );
		delegee.Invoke();
	}
}

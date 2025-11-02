namespace Cato;

using SysTask = System.Threading.Tasks;

public delegate SysTask.Task Awaitable();

sealed class AwaitableEvent
{
	SysTask.TaskCompletionSource taskCompletionSource;

	public AwaitableEvent()
	{
		taskCompletionSource = new();
	}

	public Awaitable Awaitable => () => taskCompletionSource.Task;

	public void Trigger()
	{
		SysTask.TaskCompletionSource temp = taskCompletionSource;
		taskCompletionSource = new();
		temp.SetResult();
	}
}

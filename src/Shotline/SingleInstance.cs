namespace Shotline;

internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle wake;
    private readonly ManualResetEvent stop = new(false);
    private Task? listener;
    internal bool IsFirst { get; }
    internal SingleInstance()
    {
        mutex = new Mutex(true, @"Local\Shotline.Instance", out var created);
        IsFirst = created;
        wake = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Shotline.Wake");
        if (!created) wake.Set();
    }
    internal void Listen(Action callback)
    {
        listener = Task.Run(() =>
        {
            WaitHandle[] handles = [stop, wake];
            while (WaitHandle.WaitAny(handles) == 1) Application.Current.Dispatcher.InvokeAsync(callback);
        });
    }
    public void Dispose()
    {
        stop.Set(); listener?.GetAwaiter().GetResult();
        if (IsFirst) mutex.ReleaseMutex();
        mutex.Dispose(); wake.Dispose(); stop.Dispose();
    }
}

namespace Lifeboat;

public static class Extensions
{
    public static Task WaitOneAsync(this WaitHandle waitHandle)
    {
        if (waitHandle == null)
            throw new ArgumentNullException("waitHandle");

        var tcs = new TaskCompletionSource<bool>();
        var rwh = ThreadPool.RegisterWaitForSingleObject(waitHandle,
            delegate { tcs.TrySetResult(true); }, null, -1, true);
        var t = tcs.Task;
        t.ContinueWith((antecedent) => rwh.Unregister(null));
        return t;
    }
}
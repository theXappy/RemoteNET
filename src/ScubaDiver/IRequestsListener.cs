using System;

namespace ScubaDiver;

public interface IRequestsListener : IDisposable
{
    public event EventHandler<ScubaDiverMessage> RequestReceived;
    void Start();
    void Stop();
    void WaitForExit();
}
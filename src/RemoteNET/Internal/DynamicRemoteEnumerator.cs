using System.Collections;

namespace RemoteNET.Internal
{
    public class DynamicRemoteEnumerator : IEnumerator
    {
        private dynamic _remoteEnumerator;
        public DynamicRemoteEnumerator(dynamic remoteEnumerator)
        {
            _remoteEnumerator = remoteEnumerator;
        }

        public object Current => _remoteEnumerator.Current;

        public bool MoveNext() => _remoteEnumerator.MoveNext();

        public void Reset() => _remoteEnumerator.Reset();
    }
}

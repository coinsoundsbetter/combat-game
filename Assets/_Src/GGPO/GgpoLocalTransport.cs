using System;

namespace _Src.GGPO
{
    /// <summary>
    /// Transport used by local-only sessions.  It preserves the same session
    /// call path as a network transport without allocating a socket or sending
    /// packets that no peer can consume.
    /// </summary>
    public sealed class GgpoLocalTransport<TInput> : IGgpoTransport<TInput>
    {
        private bool m_Disposed;

        public void QueueLocalInput(int playerIndex, int frame, TInput input)
        {
            ThrowIfDisposed();
        }

        public void Pump(Action<int, int, TInput> onRemoteInput)
        {
            ThrowIfDisposed();
            if (onRemoteInput == null)
                throw new ArgumentNullException(nameof(onRemoteInput));
        }

        public void Dispose()
        {
            m_Disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(GgpoLocalTransport<TInput>));
        }
    }
}

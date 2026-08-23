using System;

namespace _Src.GGPO {
    public interface IGgpoTransport<TInput> : IDisposable {
        void QueueLocalInput(int playerIndex, int frame, TInput input);
        void Pump(Action<int, int, TInput> onRemoteInput);
    }
}

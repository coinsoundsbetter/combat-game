using System;
using System.Collections.Generic;

namespace _Code.GGPO {
    /// <summary>
    /// 本地传输层,支持模拟延迟
    /// </summary>
    public sealed class GgpoLocalTransport<TInput> : IGgpoTransport<TInput> {
        private readonly List<PendingInput> m_PendingInputs = new List<PendingInput>();
        private readonly int m_SimulatedDelayTicks;
        private int m_PumpTick;
        private bool m_Disposed;

        public GgpoLocalTransport(int simulatedDelayTicks = 0) {
            if (simulatedDelayTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(simulatedDelayTicks));

            m_SimulatedDelayTicks = simulatedDelayTicks;
        }

        public void QueueLocalInput(int playerIndex, int frame, TInput input) {
            ThrowIfDisposed();
        }

        public void QueueRemoteInput(int playerIndex, int frame, TInput input) {
            ThrowIfDisposed();

            m_PendingInputs.Add(new PendingInput {
                PlayerIndex = playerIndex,
                Frame = frame,
                Input = input,
                DeliverAtTick = m_PumpTick + m_SimulatedDelayTicks,
            });
        }

        public void Pump(Action<int, int, TInput> onRemoteInput) {
            ThrowIfDisposed();

            if (onRemoteInput == null)
                throw new ArgumentNullException(nameof(onRemoteInput));

            m_PumpTick++;

            for (var i = m_PendingInputs.Count - 1; i >= 0; i--) {
                var pending = m_PendingInputs[i];
                if (pending.DeliverAtTick > m_PumpTick)
                    continue;

                onRemoteInput(pending.PlayerIndex, pending.Frame, pending.Input);
                m_PendingInputs.RemoveAt(i);
            }
        }

        public void Dispose() {
            m_Disposed = true;
            m_PendingInputs.Clear();
        }

        private void ThrowIfDisposed() {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(GgpoLocalTransport<TInput>));
        }

        private struct PendingInput {
            public int PlayerIndex;
            public int Frame;
            public TInput Input;
            public int DeliverAtTick;
        }
    }
}

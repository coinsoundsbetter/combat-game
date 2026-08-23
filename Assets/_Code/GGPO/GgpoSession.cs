using System;
using System.Collections.Generic;

namespace _Code.GGPO {
    public class GgpoSession<TInput> : IDisposable {
        private readonly GgpoCallback<TInput> m_Callback;
        private readonly IGgpoTransport<TInput> m_Transport;
        private readonly int m_MaxRollbackFrames;
        private readonly List<GgpoInputQueue<TInput>> m_PlayerQueues;
        private readonly Dictionary<int, GgpoSavedState> m_Snapshots = new Dictionary<int, GgpoSavedState>();
        private readonly List<int> m_FramesToRemove = new List<int>();
        private TInput[] m_SynchronizedInputs;
        private int m_CurrentFrame;
        private int m_EarliestRollbackFrame = -1;
        private bool m_ArePlayersLocked;
        private bool m_HasSynchronizedCurrentFrame;
        private bool m_IsRollingBack;
        private int m_RegisteredPlayerCount;
        private bool m_IsClosed;

        public GgpoSession(
            GgpoCallback<TInput> callback,
            IGgpoTransport<TInput> transport,
            int maxPlayerCount,
            int maxRollbackFrames) {
            m_Callback = callback;
            m_Transport = transport;
            m_MaxRollbackFrames = maxRollbackFrames;
            m_PlayerQueues = new List<GgpoInputQueue<TInput>>(maxPlayerCount);
            m_SynchronizedInputs = new TInput[maxPlayerCount];
            m_Callback.OnSessionStarted?.Invoke();
        }

        public void Dispose() {
            
        }

        public void Idle(int timeoutMilliseconds) {
            
        }

        public int AddPlayer(GgpoPlayerType playerType) {
            var playerIndex = m_RegisteredPlayerCount;
            m_PlayerQueues[playerIndex] = new GgpoInputQueue<TInput>();
            m_RegisteredPlayerCount++;
            return playerIndex;
        }
        
        public void Add
    }
}

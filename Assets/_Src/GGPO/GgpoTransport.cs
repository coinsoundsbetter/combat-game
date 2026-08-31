using System;

namespace _Src.GGPO {

    public interface IGgpoTransport<TInput> : IDisposable {

        /// <summary>
        /// 添加本地玩家输入,会进入发送队列
        /// </summary>
        void QueueLocalInput(int playerIndex, int frame, TInput input);


        /// <summary>
        /// 
        /// </summary>
        /// <param name="onRemoteInput"></param>
        void Pump(Action<int, int, TInput> onRemoteInput);
    }

    /// <summary>
    /// 可选的确认帧状态校验通道。校验失败只报告分叉，不参与状态修正。
    /// </summary>
    public interface IGgpoChecksumTransport {
        event Action<int, uint> RemoteChecksumReceived;

        void QueueChecksum(int stateFrame, uint checksum);
    }

    /// <summary>
    /// 输入包携带的时间同步样本。帧优势为正表示发送方认为自己领先，
    /// 为负表示发送方认为自己落后。
    /// </summary>
    public readonly struct GgpoTimeSyncSample {
        public readonly int RemoteFrame;
        public readonly float RemoteFrameAdvantage;

        public GgpoTimeSyncSample(int remoteFrame, float remoteFrameAdvantage) {
            RemoteFrame = remoteFrame;
            RemoteFrameAdvantage = remoteFrameAdvantage;
        }
    }

    /// <summary>
    /// 可选的时间同步通道。UDP 传输实现它，本地传输无需实现。
    /// </summary>
    public interface IGgpoTimeSyncTransport {
        event Action<GgpoTimeSyncSample> TimeSyncSampleReceived;

        void SetLocalTimeSyncState(int currentFrame, float localFrameAdvantage);
    }

    /// <summary>
    /// 可选的可靠输入诊断。UDP 本身仍是不可靠传输，但输入会一直重发，
    /// 直到收到对端针对该玩家、该逻辑帧的确认。
    /// </summary>
    public interface IGgpoReliableInputDiagnostics {
        int PendingLocalInputCount { get; }
        int ReceivedInputAckCount { get; }
    }

    public enum GgpoConnectionState {
        NotStarted,
        WaitingForPeer,
        PlayerIndexConflict,
        Synchronized,
    }

    /// <summary>
    /// 可选的会话启动同步。网络传输在双方完成 READY/ACK 之前仍然收发包，
    /// 但战斗逻辑必须保持在状态帧 0，避免先启动的一端永久领先。
    /// </summary>
    public interface IGgpoConnectionTransport {
        GgpoConnectionState ConnectionState { get; }
        bool IsSynchronized { get; }

        void BeginSynchronization(int localPlayerIndex);
    }
}

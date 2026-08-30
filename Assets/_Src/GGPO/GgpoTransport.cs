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
}

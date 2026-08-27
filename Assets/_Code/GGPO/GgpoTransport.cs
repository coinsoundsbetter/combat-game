using System;

namespace _Code.GGPO {

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
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace _Code.GGPO
{
    public class GgpoSession<TInput> : IDisposable
    {
        private readonly GgpoCallback<TInput> m_Callback;
        private readonly IGgpoTransport<TInput> m_Transport;
        private readonly int m_MaxRollbackFrames;
        private readonly GgpoInputQueue<TInput>[] m_PlayerQueues;
        private readonly Dictionary<int, GgpoSavedState> m_Snapshots = new Dictionary<int, GgpoSavedState>();
        private readonly List<int> m_FramesToRemove = new List<int>();
        private TInput[] m_SynchronizedInputs;
        private int m_CurrentFrame;
        private int m_EarliestRollbackFrame = -1;
        private bool m_HasSynchronizedCurrentFrame;
        private bool m_IsRollingBack;
        private int m_RegisteredPlayerCount;
        private int m_InputDelayFrames;
        private bool m_IsClosed;

        public GgpoSession(
            GgpoCallback<TInput> callback,
            IGgpoTransport<TInput> transport,
            int maxPlayerCount,
            int maxRollbackFrames,
            int inputDelayFrames)
        {
            m_Callback = callback;
            m_Transport = transport;
            m_InputDelayFrames = inputDelayFrames;
            m_MaxRollbackFrames = maxRollbackFrames;
            m_PlayerQueues = new GgpoInputQueue<TInput>[maxPlayerCount];
            m_SynchronizedInputs = new TInput[maxPlayerCount];
            m_Callback.OnSessionStarted?.Invoke();
        }

        public void Dispose()
        {
            if (m_IsClosed)
            {
                return;
            }

            m_IsClosed = true;
            for(int i = 0; i < m_PlayerQueues.Length; i++)
            {
                var queue = m_PlayerQueues[i];
                if (queue == null)
                {
                    continue;
                }

                queue.Inputs.Clear();
                queue.PredictedInputs.Clear();
                queue.UsedInputs.Clear();
            }

            m_Snapshots.Clear();
            m_FramesToRemove.Clear();
            m_Transport.Dispose();
        }

        public void Idle(int timeoutMilliseconds)
        {
            m_Transport.Pump(ReceiveRemoteInput);
            RollbackResimulate();
        }

        public bool TrySynqhronizeInputs(TInput[] inputs)
        {
            if (m_HasSynchronizedCurrentFrame)
            {
                throw new InvalidOperationException("Current frame has already been synchronized");
            }

            if (!IsAllLocalInputsSubmitted())
            {
                return false;
            }

            if (IsRemotePredictionLimitReached())
            {
                return false;
            }

            SynchronizeInputs(m_CurrentFrame, m_SynchronizedInputs);

            Array.Copy(m_SynchronizedInputs, inputs, m_SynchronizedInputs.Length);

            m_HasSynchronizedCurrentFrame = true;
            return true;
        }

        public void AdvanceFrame()
        {
            SaveSnapshot(m_CurrentFrame);
            PruneHistory();

            //逻辑帧模拟
            SimulateOneFrame(m_CurrentFrame, m_SynchronizedInputs);

            //当前帧结束,推进帧号
            m_CurrentFrame++;
            m_HasSynchronizedCurrentFrame = false;

            SaveSnapshot(m_CurrentFrame);
        }

        public int AddPlayer(GgpoPlayerType playerType)
        {
            if (m_RegisteredPlayerCount >= m_PlayerQueues.Length)
            {
                throw new InvalidOperationException("Max player count reached");
            }

            var playerIndex = m_RegisteredPlayerCount;  
            m_PlayerQueues[playerIndex] = new GgpoInputQueue<TInput>(playerType, m_InputDelayFrames);
            m_RegisteredPlayerCount++;
            return playerIndex;
        }

        public void AddLocalInput(int playerIndex, TInput input)
        {
            var queue = m_PlayerQueues[playerIndex];
            var appliedFrame = m_CurrentFrame + queue.InputDelayFrames;
            if (queue.Inputs.ContainsKey(appliedFrame))
            {
                throw new Exception($"Input already exists for frame {appliedFrame}");
            }

            queue.Inputs.Add(appliedFrame, input);
            try
            {
                m_Transport.QueueLocalInput(playerIndex, appliedFrame, input);
            }
            catch
            {
                queue.Inputs.Remove(appliedFrame);
                throw;
            }

            queue.LastLocalSubmittedFrame = m_CurrentFrame;
        }

        private TInput GetInput(GgpoInputQueue<TInput> queue, int frame)
        {
            //拿到真实输入
            if (queue.Inputs.TryGetValue(frame, out var actualInput))
            {
                queue.PredictedInputs.Remove(frame);
                return actualInput;
            }

            //没拿到,按我们的策略是使用最后一个已知的历史输入进行预测
            var predictedInput = GetLastInput(queue, frame);
            //只有远端玩家,我们才会记录预测输入
            if (queue.PlayerType == GgpoPlayerType.Remote)
            {
                queue.PredictedInputs.Add(frame, predictedInput);
            }

            return predictedInput;
        }

        private TInput GetLastInput(GgpoInputQueue<TInput> queue, int frame)
        {
            var result = queue.HasInputBeforeHistory ? queue.InputBeforeHistory : default(TInput);
            var latestFrame = queue.HasInputBeforeHistory
                ? queue.InputBeforeHistoryFrame
                : -1;
            foreach (KeyValuePair<int, TInput> pair in queue.Inputs)
            {
                if (pair.Key <= frame && pair.Key > latestFrame)
                {
                    result = pair.Value;
                    latestFrame = pair.Key;
                }
            }

            return result;
        }

        private bool IsAllLocalInputsSubmitted()
        {
            foreach (var queue in m_PlayerQueues)
            {
                if (queue.PlayerType == GgpoPlayerType.Local &&
                    queue.LastLocalSubmittedFrame != m_CurrentFrame)
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsRemotePredictionLimitReached()
        {
            var hasRemotePlayer = false;
            var lastConfirmedFrame = int.MaxValue;

            foreach (var queue in m_PlayerQueues)
            {
                if (queue.PlayerType != GgpoPlayerType.Remote)
                    continue;

                hasRemotePlayer = true;
                if (queue.LastConfirmedRemoteFrame < lastConfirmedFrame)
                    lastConfirmedFrame = queue.LastConfirmedRemoteFrame;
            }

            return hasRemotePlayer &&
                   m_CurrentFrame - lastConfirmedFrame >= m_MaxRollbackFrames;
        }

        private void SaveSnapshot(int frame)
        {
            if (!m_Snapshots.ContainsKey(frame))
            {
                m_Snapshots.Add(frame, m_Callback.SaveGameState(frame));
            }
        }

        private void ReceiveRemoteInput(int playerIndex, int frame, TInput input)
        {
            var leftLimit = m_CurrentFrame - m_MaxRollbackFrames;
            if (frame < leftLimit)
            {
                // UDP 会重发历史输入；超出回滚窗口的包无法再影响当前状态，直接丢弃。
                return;
            }

            var queue = m_PlayerQueues[playerIndex];
            if (queue.PlayerType != GgpoPlayerType.Remote)
            {
                throw new InvalidOperationException("非远端玩家尝试接收远端输入");
            }

            TInput usedInput = default(TInput);
            bool hasSimulated = frame < m_CurrentFrame &&
                                queue.UsedInputs.TryGetValue(frame, out usedInput);
            queue.Inputs[frame] = input;

            //更新已确认的最新帧号 todo:如果是先来新的 后来旧的呢?
            while(queue.Inputs.ContainsKey(queue.LastConfirmedRemoteFrame + 1))
            {
                queue.LastConfirmedRemoteFrame++;
            }

            //这个输入已经被我们的预测模拟过了,发现不一致,需要回滚重放
            if (hasSimulated && !EqualityComparer<TInput>.Default.Equals(input, usedInput))
            {
                // 确认回滚从哪里开始执行_应该从最早发现预测错误的帧号开始
                if (m_EarliestRollbackFrame < 0 || frame < m_EarliestRollbackFrame)
                {
                    m_EarliestRollbackFrame = frame;
                }
            }
            else
            {
                //否则只要将它从预测输入历史里去掉就可以
                queue.PredictedInputs.Remove(frame);
            }
        }

        private void RollbackResimulate()
        {
            //无需回滚
            if (m_EarliestRollbackFrame < 0)
            {
                return;
            }

            var rollbackStartFrame = m_EarliestRollbackFrame;
            var rollbackEndFrame = m_CurrentFrame;
            GgpoSavedState snapshot;
            if (!m_Snapshots.TryGetValue(rollbackStartFrame, out snapshot))
            {
                throw new InvalidOperationException($"缺少历史快照,回滚无法进行,frame:{rollbackStartFrame}");
            }

            m_IsRollingBack = true;
            m_HasSynchronizedCurrentFrame = false;

            try
            {
                //设置当前游戏快照
                m_Callback.LoadGameState(snapshot.Buffer);
                
                for(int i = 0; i < m_PlayerQueues.Length; i++)
                {
                    RemoveKeysAtOrAfter(m_PlayerQueues[i].UsedInputs, rollbackStartFrame);
                    RemoveKeysAtOrAfter(m_PlayerQueues[i].PredictedInputs, rollbackStartFrame);
                }
                RemoveSnapshotsAfter(rollbackStartFrame);
                m_CurrentFrame = rollbackStartFrame;

                //然后我们就基于当前状态执行输入重放
                while (m_CurrentFrame < rollbackEndFrame)
                {
                    SynchronizeInputs(m_CurrentFrame, m_SynchronizedInputs);
                    SimulateOneFrame(m_CurrentFrame, m_SynchronizedInputs);
                    m_CurrentFrame++;
                    //纠正之后快照可能有变化
                    SaveSnapshot(m_CurrentFrame);
                }

                m_EarliestRollbackFrame = -1;
            }
            catch
            {
                Dispose();
                throw;
            }
            finally
            {
                m_IsRollingBack = false;
                m_HasSynchronizedCurrentFrame = false;
            }
        }

        private void RemoveSnapshotsAfter(int retainedFrame)
        {
            m_FramesToRemove.Clear();

            foreach (KeyValuePair<int, GgpoSavedState> pair
                     in m_Snapshots)
            {
                if (pair.Key > retainedFrame)
                    m_FramesToRemove.Add(pair.Key);
            }

            RemoveCollectedKeys(m_Snapshots);
        }

        private void PruneHistory()
        {
            var firstRetainedFrame = m_CurrentFrame - m_MaxRollbackFrames;
            if (firstRetainedFrame <= 0)
                return;

            for (var i = 0; i < m_PlayerQueues.Length; i++) {
                var queue = m_PlayerQueues[i];
                PreserveInputBeforeHistory(queue, firstRetainedFrame);
                RemoveKeysBefore(queue.Inputs, firstRetainedFrame);
                RemoveKeysBefore(queue.PredictedInputs, firstRetainedFrame);
                RemoveKeysBefore(queue.UsedInputs, firstRetainedFrame);
            }

            RemoveKeysBefore(m_Snapshots, firstRetainedFrame);
        }

        private void PreserveInputBeforeHistory(
            GgpoInputQueue<TInput> queue,
            int firstRetainedFrame)
        {
            var latestFrame = queue.HasInputBeforeHistory
                ? queue.InputBeforeHistoryFrame
                : -1;
            var latestInput = queue.HasInputBeforeHistory
                ? queue.InputBeforeHistory
                : default(TInput);

            foreach (var pair in queue.Inputs) {
                if (pair.Key < firstRetainedFrame && pair.Key > latestFrame) {
                    latestFrame = pair.Key;
                    latestInput = pair.Value;
                }
            }

            if (latestFrame >= 0) {
                queue.InputBeforeHistoryFrame = latestFrame;
                queue.InputBeforeHistory = latestInput;
                queue.HasInputBeforeHistory = true;
            }
        }

        private void RemoveKeysAtOrAfter<TValue>(
            Dictionary<int, TValue> values,
            int firstFrame)
        {
            m_FramesToRemove.Clear();

            foreach (KeyValuePair<int, TValue> pair in values)
            {
                if (pair.Key >= firstFrame)
                    m_FramesToRemove.Add(pair.Key);
            }

            RemoveCollectedKeys(values);
        }

        private void RemoveKeysBefore<TValue>(
            Dictionary<int, TValue> values,
            int firstRetainedFrame)
        {
            m_FramesToRemove.Clear();

            foreach (var pair in values) {
                if (pair.Key < firstRetainedFrame)
                    m_FramesToRemove.Add(pair.Key);
            }

            RemoveCollectedKeys(values);
        }

        private void RemoveCollectedKeys<TValue>(
            Dictionary<int, TValue> values)
        {
            for (int i = 0; i < m_FramesToRemove.Count; i++)
                values.Remove(m_FramesToRemove[i]);

            m_FramesToRemove.Clear();
        }

        private void SimulateOneFrame(int frame, TInput[] inputs)
        {
            for(int i = 0; i < m_PlayerQueues.Length; i++)
            {
                m_PlayerQueues[i].UsedInputs[m_CurrentFrame] = m_SynchronizedInputs[i];
            }

            m_Callback.AdvanceFrame(m_CurrentFrame, m_SynchronizedInputs);
        }

        private void SynchronizeInputs(int frame, TInput[] inputs)
        {
            for(int i = 0; i < m_PlayerQueues.Length; i++)
            {
                var queue = m_PlayerQueues[i];
                m_SynchronizedInputs[i] = GetInput(queue, m_CurrentFrame);
            }
        }
    }
}

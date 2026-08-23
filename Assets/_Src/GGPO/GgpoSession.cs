using System;
using System.Collections.Generic;
using UnityEngine.Pool;

namespace _Src.GGPO
{
    /// <summary>
    /// Deterministic rollback session with fixed player slots.  Register all
    /// players with AddPlayer before the first input synchronization.
    /// </summary>
    public sealed class GgpoSession<TInput>
    {
        private readonly GgpoCallback<TInput> m_Callback;
        private readonly IGgpoTransport<TInput> m_Transport;
        private readonly int m_MaxRollbackFrames;
        private readonly List<GgpoInputQueue<TInput>> m_PlayerQueues =
            new List<GgpoInputQueue<TInput>>();
        private readonly Dictionary<int, GgpoSavedState> m_Snapshots =
            new Dictionary<int, GgpoSavedState>();

        private TInput[] m_SynchronizedInputs = new TInput[0];
        private int m_CurrentFrame;
        private int m_EarliestRollbackFrame = -1;
        private bool m_ArePlayersLocked;
        private bool m_IsClosed;
        private bool m_IsRollingBack;

        public int CurrentFrame { get { return m_CurrentFrame; } }
        public int PlayerCount { get { return m_PlayerQueues.Count; } }
        public bool IsRollingBack { get { return m_IsRollingBack; } }

        public GgpoSession(
            GgpoCallback<TInput> callback,
            IGgpoTransport<TInput> transport,
            int maxRollbackFrames)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));
            if (transport == null)
                throw new ArgumentNullException(nameof(transport));
            if (callback.SaveGameState == null)
                throw new ArgumentException("SaveGameState is required.", nameof(callback));
            if (callback.LoadGameState == null)
                throw new ArgumentException("LoadGameState is required.", nameof(callback));
            if (callback.AdvanceFrame == null)
                throw new ArgumentException("AdvanceFrame is required.", nameof(callback));
            if (maxRollbackFrames <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxRollbackFrames));

            m_Callback = callback;
            m_Transport = transport;
            m_MaxRollbackFrames = maxRollbackFrames;
            m_Callback.OnSessionStarted?.Invoke();
        }

        /// <summary>
        /// Adds one fixed player slot and returns its permanent index.  The same
        /// slot order must be registered by every peer in the match.
        /// </summary>
        public int AddPlayer(GgpoPlayerType type, int inputDelayFrames)
        {
            ThrowIfClosed();
            if (m_ArePlayersLocked)
                throw new InvalidOperationException("Players cannot be added after the session starts.");
            if (inputDelayFrames < 0)
                throw new ArgumentOutOfRangeException(nameof(inputDelayFrames));

            var playerIndex = m_PlayerQueues.Count;
            m_PlayerQueues.Add(new GgpoInputQueue<TInput>(
                new GgpoPlayerConfig(type, inputDelayFrames)));
            m_SynchronizedInputs = new TInput[m_PlayerQueues.Count];
            return playerIndex;
        }

        public void Idle(int timeoutMilliseconds)
        {
            ThrowIfClosed();
            if (timeoutMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));

            m_Transport.Pump(SetRemoteInput);
            RollbackResimulate();
        }

        public void Close()
        {
            if (m_IsClosed)
                return;

            foreach (var queue in m_PlayerQueues)
            {
                queue.Inputs.Clear();
                queue.PredictedInputs.Clear();
                queue.UsedInputs.Clear();
            }
            m_Snapshots.Clear();
            m_Transport.Dispose();
            m_IsClosed = true;
        }

        public void AddLocalInput(int playerIndex, TInput input)
        {
            ThrowIfClosed();
            LockPlayers();
            if (m_IsRollingBack)
                throw new InvalidOperationException("Cannot add input during rollback.");

            var queue = GetQueue(playerIndex);
            if (queue.Type != GgpoPlayerType.Local)
                throw new InvalidOperationException("The player is not local.");
            if (queue.LastLocalSubmittedFrame != m_CurrentFrame - 1)
                throw new InvalidOperationException("Local input was already submitted for this frame.");

            var appliedFrame = m_CurrentFrame + queue.InputDelayFrames;
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

        /// <summary>
        /// Returns current-frame inputs in permanent player-slot order.  The
        /// caller-supplied array must have exactly PlayerCount elements.
        /// </summary>
        public bool TrySynchronizeInputs(TInput[] inputs)
        {
            ThrowIfClosed();
            LockPlayers();
            if (inputs == null || inputs.Length != m_PlayerQueues.Count)
                throw new ArgumentException("Input array length must equal PlayerCount.", nameof(inputs));
            if (!m_IsRollingBack && !AreAllLocalInputsSubmitted())
                return false;
            if (HasReachedPredictionBarrier())
                return false;

            SynchronizeInputsForFrame(m_CurrentFrame, inputs);
            return true;
        }

        public void AdvanceFrame()
        {
            ThrowIfClosed();
            LockPlayers();
            if (!TrySynchronizeInputs(m_SynchronizedInputs))
                throw new InvalidOperationException("Inputs are unavailable or the prediction barrier was reached.");

            SaveSnapshotIfMissing(m_CurrentFrame);
            SimulateOneFrame(m_CurrentFrame, m_SynchronizedInputs);
            m_CurrentFrame++;
            SaveSnapshotIfMissing(m_CurrentFrame);
            PruneHistory();
        }

        private void SetRemoteInput(int playerIndex, int frame, TInput input)
        {
            ThrowIfClosed();
            LockPlayers();
            if (frame < 0)
                throw new ArgumentOutOfRangeException(nameof(frame));
            if (frame < m_CurrentFrame - m_MaxRollbackFrames)
                throw new InvalidOperationException("Remote input is older than the rollback window.");

            var queue = GetQueue(playerIndex);
            if (queue.Type != GgpoPlayerType.Remote)
                throw new InvalidOperationException("The player is not remote.");

            TInput usedInput = default(TInput);
            var wasSimulated = frame < m_CurrentFrame &&
                               queue.UsedInputs.TryGetValue(frame, out usedInput);

            queue.Inputs[frame] = input;
            AdvanceLastConfirmedRemoteFrame(queue);

            if (wasSimulated && !EqualityComparer<TInput>.Default.Equals(input, usedInput))
                ScheduleRollback(frame);
            else
                queue.PredictedInputs.Remove(frame);
        }

        private void SimulateOneFrame(int frame, TInput[] inputs)
        {
            for (var i = 0; i < m_PlayerQueues.Count; i++)
                m_PlayerQueues[i].UsedInputs[frame] = inputs[i];

            // The buffer is reused every frame; callbacks must not retain it.
            m_Callback.AdvanceFrame(frame, inputs);
        }

        private void RollbackResimulate()
        {
            if (m_EarliestRollbackFrame < 0)
                return;

            var rollbackFrame = m_EarliestRollbackFrame;
            var targetFrame = m_CurrentFrame;
            GgpoSavedState snapshot;
            if (!m_Snapshots.TryGetValue(rollbackFrame, out snapshot))
                throw new InvalidOperationException("Missing snapshot for rollback frame: " + rollbackFrame);

            m_IsRollingBack = true;
            try
            {
                m_Callback.LoadGameState(snapshot.Buffer);
                foreach (var queue in m_PlayerQueues)
                {
                    RemoveKeysAtOrAfter(queue.UsedInputs, rollbackFrame);
                    RemoveKeysAtOrAfter(queue.PredictedInputs, rollbackFrame);
                }
                RemoveSnapshotsAfter(rollbackFrame);
                m_CurrentFrame = rollbackFrame;

                while (m_CurrentFrame < targetFrame)
                {
                    SynchronizeInputsForFrame(m_CurrentFrame, m_SynchronizedInputs);
                    SimulateOneFrame(m_CurrentFrame, m_SynchronizedInputs);
                    m_CurrentFrame++;
                    SaveSnapshotIfMissing(m_CurrentFrame);
                }

                m_EarliestRollbackFrame = -1;
                PruneHistory();
            }
            catch
            {
                Close();
                throw;
            }
            finally
            {
                m_IsRollingBack = false;
            }
        }

        private bool AreAllLocalInputsSubmitted()
        {
            foreach (var queue in m_PlayerQueues)
            {
                if (queue.Type == GgpoPlayerType.Local &&
                    queue.LastLocalSubmittedFrame != m_CurrentFrame)
                    return false;
            }
            return true;
        }

        private bool HasReachedPredictionBarrier()
        {
            var hasRemotePlayer = false;
            var lastConfirmedFrame = int.MaxValue;
            foreach (var queue in m_PlayerQueues)
            {
                if (queue.Type != GgpoPlayerType.Remote)
                    continue;

                hasRemotePlayer = true;
                lastConfirmedFrame = Math.Min(lastConfirmedFrame, queue.LastConfirmedRemoteFrame);
            }

            return hasRemotePlayer &&
                   m_CurrentFrame >= m_MaxRollbackFrames &&
                   m_CurrentFrame - lastConfirmedFrame >= m_MaxRollbackFrames;
        }

        private void SynchronizeInputsForFrame(int frame, TInput[] output)
        {
            for (var i = 0; i < m_PlayerQueues.Count; i++)
                output[i] = GetInput(m_PlayerQueues[i], frame);
        }

        private static TInput GetInput(GgpoInputQueue<TInput> queue, int frame)
        {
            TInput input;
            if (queue.Inputs.TryGetValue(frame, out input))
            {
                queue.PredictedInputs.Remove(frame);
                return input;
            }

            input = FindLatestInput(queue, frame);
            if (queue.Type == GgpoPlayerType.Remote)
                queue.PredictedInputs[frame] = input;
            return input;
        }

        private static TInput FindLatestInput(GgpoInputQueue<TInput> queue, int frame)
        {
            var latestFrame = -1;
            var result = queue.HasInputBeforeHistory
                ? queue.InputBeforeHistory
                : default(TInput);
            foreach (var pair in queue.Inputs)
            {
                if (pair.Key <= frame && pair.Key > latestFrame)
                {
                    latestFrame = pair.Key;
                    result = pair.Value;
                }
            }
            return result;
        }

        private void SaveSnapshotIfMissing(int frame)
        {
            if (m_Snapshots.ContainsKey(frame))
                return;

            var state = m_Callback.SaveGameState(frame);
            if (state == null || state.Buffer == null)
                throw new InvalidOperationException("SaveGameState must return a state with a Buffer.");
            m_Snapshots.Add(frame, state);
        }

        private static void AdvanceLastConfirmedRemoteFrame(GgpoInputQueue<TInput> queue)
        {
            while (queue.Inputs.ContainsKey(queue.LastConfirmedRemoteFrame + 1))
                queue.LastConfirmedRemoteFrame++;
        }

        private void ScheduleRollback(int frame)
        {
            if (m_EarliestRollbackFrame < 0 || frame < m_EarliestRollbackFrame)
                m_EarliestRollbackFrame = frame;
        }

        private void PruneHistory()
        {
            var firstRetainedFrame = m_CurrentFrame - m_MaxRollbackFrames;
            if (firstRetainedFrame <= 0)
                return;

            foreach (var queue in m_PlayerQueues)
            {
                PruneInputs(queue, firstRetainedFrame);
                RemoveKeysBefore(queue.PredictedInputs, firstRetainedFrame);
                RemoveKeysBefore(queue.UsedInputs, firstRetainedFrame);
            }
            RemoveSnapshotsBefore(firstRetainedFrame);
        }

        private static void PruneInputs(GgpoInputQueue<TInput> queue, int firstRetainedFrame)
        {
            var framesToRemove = ListPool<int>.Get();
            try
            {
                var latestRemovedFrame = -1;
                foreach (var pair in queue.Inputs)
                {
                    if (pair.Key >= firstRetainedFrame)
                        continue;

                    framesToRemove.Add(pair.Key);
                    if (pair.Key > latestRemovedFrame)
                    {
                        latestRemovedFrame = pair.Key;
                        queue.InputBeforeHistory = pair.Value;
                        queue.HasInputBeforeHistory = true;
                    }
                }
                foreach (var frame in framesToRemove)
                    queue.Inputs.Remove(frame);
            }
            finally
            {
                ListPool<int>.Release(framesToRemove);
            }
        }

        private void RemoveSnapshotsBefore(int firstRetainedFrame)
        {
            var framesToRemove = ListPool<int>.Get();
            try
            {
                foreach (var pair in m_Snapshots)
                    if (pair.Key < firstRetainedFrame)
                        framesToRemove.Add(pair.Key);
                foreach (var frame in framesToRemove)
                    m_Snapshots.Remove(frame);
            }
            finally
            {
                ListPool<int>.Release(framesToRemove);
            }
        }

        private void RemoveSnapshotsAfter(int frame)
        {
            var framesToRemove = ListPool<int>.Get();
            try
            {
                foreach (var pair in m_Snapshots)
                    if (pair.Key > frame)
                        framesToRemove.Add(pair.Key);
                foreach (var stateFrame in framesToRemove)
                    m_Snapshots.Remove(stateFrame);
            }
            finally
            {
                ListPool<int>.Release(framesToRemove);
            }
        }

        private static void RemoveKeysBefore(Dictionary<int, TInput> values, int firstFrame)
        {
            var keysToRemove = ListPool<int>.Get();
            try
            {
                foreach (var pair in values)
                    if (pair.Key < firstFrame)
                        keysToRemove.Add(pair.Key);
                foreach (var key in keysToRemove)
                    values.Remove(key);
            }
            finally
            {
                ListPool<int>.Release(keysToRemove);
            }
        }

        private static void RemoveKeysAtOrAfter(Dictionary<int, TInput> values, int firstFrame)
        {
            var keysToRemove = ListPool<int>.Get();
            try
            {
                foreach (var pair in values)
                    if (pair.Key >= firstFrame)
                        keysToRemove.Add(pair.Key);
                foreach (var key in keysToRemove)
                    values.Remove(key);
            }
            finally
            {
                ListPool<int>.Release(keysToRemove);
            }
        }

        private GgpoInputQueue<TInput> GetQueue(int playerIndex)
        {
            if (playerIndex < 0 || playerIndex >= m_PlayerQueues.Count)
                throw new ArgumentOutOfRangeException(nameof(playerIndex));
            return m_PlayerQueues[playerIndex];
        }

        private void LockPlayers()
        {
            if (m_PlayerQueues.Count == 0)
                throw new InvalidOperationException("Add at least one player before starting the session.");
            m_ArePlayersLocked = true;
        }

        private void ThrowIfClosed()
        {
            if (m_IsClosed)
                throw new ObjectDisposedException(nameof(GgpoSession<TInput>));
        }
    }
}

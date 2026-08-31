using System;
using System.Collections.Generic;
using _Src.GGPO;
using UnityEngine;

namespace _Src.GGPO_Extension {
    public class Battle<TInput, TPlayerState> {
        private const int ChecksumIntervalFrames = 60;

        public bool IsStartSession { get; private set; }
        public bool IsRollingBack => m_Session != null && m_Session.IsRollingBack;
        public int CurrentFrame => m_CurrentFrame;
        public int RollbackCount => m_Session != null ? m_Session.RollbackCount : 0;
        public int PredictedRemoteInputCount =>
            m_Session != null ? m_Session.PredictedRemoteInputCount : 0;
        public int LastRollbackStartFrame =>
            m_Session != null ? m_Session.LastRollbackStartFrame : -1;
        public int LastRollbackEndFrame =>
            m_Session != null ? m_Session.LastRollbackEndFrame : -1;
        public int LastRollbackDepth =>
            m_Session != null ? m_Session.LastRollbackDepth : 0;
        public bool IsTimeSyncAvailable =>
            m_Session != null && m_Session.IsTimeSyncAvailable;
        public float TimeSyncLocalAdvantage =>
            m_Session != null ? m_Session.TimeSyncLocalAdvantage : 0f;
        public float TimeSyncRemoteAdvantage =>
            m_Session != null ? m_Session.TimeSyncRemoteAdvantage : 0f;
        public int TimeSyncSampleCount =>
            m_Session != null ? m_Session.TimeSyncSampleCount : 0;
        public bool IsSyncTestEnabled =>
            m_Session != null && m_Session.IsSyncTestEnabled;
        public int SyncTestCount =>
            m_Session != null ? m_Session.SyncTestCount : 0;
        public int LastSyncTestFrame =>
            m_Session != null ? m_Session.LastSyncTestFrame : -1;
        public bool IsReliableInputAvailable => m_ReliableInputDiagnostics != null;
        public int PendingLocalInputCount =>
            m_ReliableInputDiagnostics != null
                ? m_ReliableInputDiagnostics.PendingLocalInputCount
                : 0;
        public int ReceivedInputAckCount =>
            m_ReliableInputDiagnostics != null
                ? m_ReliableInputDiagnostics.ReceivedInputAckCount
                : 0;
        public bool IsNetworkReady =>
            m_ConnectionTransport == null || m_ConnectionTransport.IsSynchronized;
        public GgpoConnectionState ConnectionState =>
            m_ConnectionTransport != null
                ? m_ConnectionTransport.ConnectionState
                : GgpoConnectionState.Synchronized;
        public int TimeSyncWaitCount { get; private set; }
        public int RollbackRevision { get; private set; }
        public bool IsChecksumVerificationAvailable => m_ChecksumTransport != null;
        public int LastVerifiedChecksumStateFrame { get; private set; } = -1;
        public int ChecksumMismatchCount { get; private set; }
        public event Action<GgpoRemoteInputDiagnostic<TInput>> RemoteInputObserved;
        public event Action RollbackObserved;
        public event Action TimeSyncWaitObserved;
        
        public void Initialize(
            CoreSetting rules, 
            IGgpoTransport<TInput> transport,
            IGgpoInputProvider<TInput> inputProvider,
            ISimulation<TPlayerState, TInput> simulator) {
            m_Setting = rules;
            m_Transport = transport;
            m_FrameInputs = new TInput[m_Setting.maxPlayerCount];
            m_PlayerRegistration = new PlayerRegistration[m_Setting.maxPlayerCount];
            m_PlayerStates = new TPlayerState[m_Setting.maxPlayerCount];
            m_StateHistory = new Dictionary<int, TPlayerState[]>();
            m_StateFramesToRemove = new List<int>();
            m_InputProvider = inputProvider;
            m_Simulator = simulator;
            m_ChecksumTransport = transport as IGgpoChecksumTransport;
            m_ReliableInputDiagnostics =
                transport as IGgpoReliableInputDiagnostics;
            m_ConnectionTransport = transport as IGgpoConnectionTransport;
            m_LocalChecksums = new Dictionary<int, uint>();
            m_RemoteChecksums = new Dictionary<int, uint>();
            m_ComparedChecksumFrames = new HashSet<int>();
            m_PreRollbackStates = null;
            m_LastRollbackPredictedStates = null;
            m_LastRollbackCorrectedStates = null;
            RollbackRevision = 0;
            TimeSyncWaitCount = 0;
            LastVerifiedChecksumStateFrame = -1;
            ChecksumMismatchCount = 0;
        }

        public void Start() {
            if (IsStartSession) {
                return;
            }
            
            IsStartSession = true;
            var callback = new GgpoCallback<TInput>() {
                SaveGameState = SaveGameState,
                LoadGameState = LoadGameState,
                AdvanceFrame = AdvanceFrame,
            };
            m_Session = new GgpoSession<TInput>(
                callback,
                m_Transport,
                m_Setting.maxPlayerCount,
                m_Setting.maxRollbackFrames,
                m_Setting.inputDelayFrames,
                m_Setting.syncTestRollbackFrames);
            m_Session.ConfirmedFrame += OnConfirmedFrame;
            m_Session.RollbackStarted += OnRollbackStarted;
            m_Session.RollbackCompleted += OnRollbackCompleted;
            m_Session.RemoteInputObserved += OnRemoteInputObserved;
            if (m_ChecksumTransport != null)
                m_ChecksumTransport.RemoteChecksumReceived += ReceiveRemoteChecksum;
            m_TickAcc = 0;
            m_TickInterval = 1f / m_Setting.tickRate;
            m_CurrentFrame = 0;
            m_WasNetworkReady = m_ConnectionTransport == null;
            for (var sessionIndex = 0; sessionIndex < m_PlayerRegistration.Length; sessionIndex++) {
                var registration = m_PlayerRegistration[sessionIndex];
                if (registration == null) {
                    throw new InvalidOperationException("Player is not registered.");
                }
                
                m_Session.AddPlayer(registration.NetType == NetPlayerType.Local ? 
                    GgpoPlayerType.Local : GgpoPlayerType.Remote);
            }

            if (m_ConnectionTransport != null) {
                var localPlayerIndex = -1;
                for (var playerIndex = 0; playerIndex < m_PlayerRegistration.Length; playerIndex++) {
                    if (m_PlayerRegistration[playerIndex].NetType != NetPlayerType.Local)
                        continue;

                    if (localPlayerIndex >= 0) {
                        throw new InvalidOperationException(
                            "Network sessions require exactly one local player.");
                    }

                    localPlayerIndex = playerIndex;
                }

                if (localPlayerIndex < 0)
                    throw new InvalidOperationException(
                        "Network sessions require one local player.");

                m_ConnectionTransport.BeginSynchronization(localPlayerIndex);
            }

            StoreState(0);
        }

        public void Stop() {
            if (!IsStartSession) {
                return;
            }
            
            IsStartSession = false;
            m_Session.ConfirmedFrame -= OnConfirmedFrame;
            m_Session.RollbackStarted -= OnRollbackStarted;
            m_Session.RollbackCompleted -= OnRollbackCompleted;
            m_Session.RemoteInputObserved -= OnRemoteInputObserved;
            if (m_ChecksumTransport != null)
                m_ChecksumTransport.RemoteChecksumReceived -= ReceiveRemoteChecksum;
            m_Session.Dispose();
            m_Transport.Dispose();
            m_Session = null;
            m_Transport = null;
            m_ConnectionTransport = null;
        }

        public void Update() {
            if (!IsStartSession) {
                return;
            }

            // 握手完成前只驱动网络，不积累逻辑时间。后启动的一端因此仍从
            // 状态帧 0 加入，而不会令先启动的一端提前预测若干帧。
            if (!IsNetworkReady) {
                m_Session.Idle(0);
                m_TickAcc = 0f;
                m_WasNetworkReady = false;
                // 每次未就绪都恢复完整的开局稳定时间，确保同步后才开始计时。
                // 上限保护：这段稳定期会冻结逻辑推进（从而本地无法输入），
                // 即使 Inspector 里手滑填了很大的值，也只短暂生效，不会锁死操作。
                m_StartGraceRemainingSeconds =
                    Mathf.Clamp(m_Setting.startGraceSeconds, 0f, 0.25f);
                return;
            }

            // 丢弃握手完成这一渲染帧已经经过的时间，从完整 tick 开始战斗。
            if (!m_WasNetworkReady) {
                m_WasNetworkReady = true;
                m_TickAcc = 0f;
                m_Session.Idle(0);
                return;
            }

            // 开局稳定期：双方都已就绪，但先顶住一段时间只收发网络包、不推进逻辑。
            // 这样对端首批输入落地后，两端几乎同时从状态帧 0 起步，先启动的一端
            // 不会用默认输入预测后启动的一端，避免其开局移动方向变化时的跳变。
            if (m_StartGraceRemainingSeconds > 0f) {
                m_StartGraceRemainingSeconds -= Time.unscaledDeltaTime;
                m_TickAcc = 0f;
                m_Session.Idle(0);
                return;
            }

            m_TickAcc += Time.unscaledDeltaTime;

            // TimeSync 等待期间以及渲染帧率高于逻辑帧率时，网络仍需继续工作。
            if (m_TickAcc < m_TickInterval) {
                m_Session.Idle(0);
                return;
            }

            var tickCount = 0;
            while (m_TickAcc >= m_TickInterval && tickCount < m_Setting.maxTickPerUpdate) {
                if (m_Session.TryConsumeTimeSyncWait()) {
                    // 只暂停正常逻辑推进；收包和已经需要的回滚照常执行。
                    m_Session.Idle(0);

                    // 必须消费这段真实时间，否则下一帧会立刻追赶回来。
                    m_TickAcc -= m_TickInterval;
                    TimeSyncWaitCount++;
                    TimeSyncWaitObserved?.Invoke();
                    break;
                }

                TickOnce();
                m_TickAcc -= m_TickInterval;
                tickCount++;
            }
        }
        
        public void RegisterPlayer(PlayerRegistration registration) {
            m_PlayerRegistration[registration.Index] = registration;
        }

        public bool TryGetLastConfirmedFrame(out int frame) {
            if (m_Session == null) {
                frame = 0;
                return false;
            }

            frame = m_Session.LastConfirmedFrame;
            return true;
        }

        public bool TryGetStateFromHistory(int frame, out TPlayerState[] states) {
            states = null;
            if (!m_StateHistory.TryGetValue(frame, out states)) {
                return false;
            }

            states = (TPlayerState[])states.Clone();
            return true;
        }

        public bool IsLocalPlayer(int playerIndex) {
            return m_PlayerRegistration != null &&
                   playerIndex >= 0 &&
                   playerIndex < m_PlayerRegistration.Length &&
                   m_PlayerRegistration[playerIndex] != null &&
                   m_PlayerRegistration[playerIndex].NetType == NetPlayerType.Local;
        }

        public bool TryGetLastRollbackStateChange(
            out int stateFrame,
            out TPlayerState[] predictedStates,
            out TPlayerState[] correctedStates) {
            stateFrame = m_CurrentFrame;
            predictedStates = null;
            correctedStates = null;
            if (m_LastRollbackPredictedStates == null ||
                m_LastRollbackCorrectedStates == null) {
                return false;
            }

            predictedStates = (TPlayerState[])m_LastRollbackPredictedStates.Clone();
            correctedStates = (TPlayerState[])m_LastRollbackCorrectedStates.Clone();
            return true;
        }

        private CoreSetting m_Setting;
        private GgpoSession<TInput> m_Session;
        private IGgpoTransport<TInput> m_Transport;
        private TPlayerState[] m_PlayerStates;
        private TInput[] m_FrameInputs;
        private IGgpoInputProvider<TInput> m_InputProvider;
        private ISimulation<TPlayerState, TInput> m_Simulator;
        private IGgpoChecksumTransport m_ChecksumTransport;
        private IGgpoReliableInputDiagnostics m_ReliableInputDiagnostics;
        private IGgpoConnectionTransport m_ConnectionTransport;
        private PlayerRegistration[] m_PlayerRegistration;
        private Dictionary<int, TPlayerState[]> m_StateHistory;
        private List<int> m_StateFramesToRemove;
        private Dictionary<int, uint> m_LocalChecksums;
        private Dictionary<int, uint> m_RemoteChecksums;
        private HashSet<int> m_ComparedChecksumFrames;
        private TPlayerState[] m_PreRollbackStates;
        private TPlayerState[] m_LastRollbackPredictedStates;
        private TPlayerState[] m_LastRollbackCorrectedStates;
        private float m_TickAcc;
        private float m_TickInterval;
        private int m_CurrentFrame;
        private bool m_HasSubmittedLocalInputForCurrentFrame;
        private bool m_WasNetworkReady;
        private float m_StartGraceRemainingSeconds;

        private void TickOnce() {
            //收集本地输入
            if (!m_HasSubmittedLocalInputForCurrentFrame && m_InputProvider != null) {
                foreach (var registration in m_PlayerRegistration) {
                    if (registration == null || registration.NetType != NetPlayerType.Local) {
                        continue;
                    }
                    
                    m_Session.AddLocalInput(registration.Index, m_InputProvider.ReadInput(registration.Index));
                }
                
                m_HasSubmittedLocalInputForCurrentFrame = true;
            }
            
            //拉取网络缓冲区中的远端输入,执行逻辑回滚
            m_Session.Idle(0);
            
            //所有玩家输入准备就绪?
            //如果很久没有收到过远端输入了,选择暂停推进
            if (!m_Session.TrySynqhronizeInputs(m_FrameInputs)) {
                return;
            }
            
            //推进一帧
            m_Session.AdvanceFrame();
            m_HasSubmittedLocalInputForCurrentFrame = false;
        }
        
        private void AdvanceFrame(int frame, TInput[] inputs) {
            m_Simulator.Simulate(m_PlayerStates, inputs);
            m_CurrentFrame = frame + 1;
            StoreState(m_CurrentFrame);
        }
        
        private void LoadGameState(byte[] buffer) {
            var stateFrame = m_Simulator.Load(buffer, m_PlayerStates);
            m_CurrentFrame = stateFrame;
            RemoveStateHistoryAfter(stateFrame);
            StoreState(stateFrame);
        }

        private GgpoSavedState SaveGameState(int frame) {
            return m_Simulator.Save(frame, m_PlayerStates);
        }

        private void StoreState(int frame) {
            m_StateHistory[frame] = (TPlayerState[])m_PlayerStates.Clone();
            m_StateHistory.Remove(frame - m_Setting.maxStateHistoryFrames);
        }

        private void OnConfirmedFrame(int frame, TInput[] inputs) {
            var stateFrame = frame + 1;
            if (m_ChecksumTransport == null ||
                stateFrame % ChecksumIntervalFrames != 0 ||
                !TryGetStateFromHistory(stateFrame, out var states)) {
                return;
            }

            var checksum = m_Simulator.CalculateChecksum(states);
            m_LocalChecksums[stateFrame] = checksum;
            m_ChecksumTransport.QueueChecksum(stateFrame, checksum);
            CompareChecksums(stateFrame);
        }

        private void OnRollbackStarted(int stateFrame) {
            m_PreRollbackStates = (TPlayerState[])m_PlayerStates.Clone();
        }

        private void OnRemoteInputObserved(
            GgpoRemoteInputDiagnostic<TInput> diagnostic) {
            RemoteInputObserved?.Invoke(diagnostic);
        }

        private void OnRollbackCompleted(int stateFrame) {
            if (m_PreRollbackStates == null)
                return;

            m_LastRollbackPredictedStates = m_PreRollbackStates;
            m_LastRollbackCorrectedStates = (TPlayerState[])m_PlayerStates.Clone();
            m_PreRollbackStates = null;
            RollbackRevision++;
            RollbackObserved?.Invoke();
        }

        private void ReceiveRemoteChecksum(int stateFrame, uint checksum) {
            m_RemoteChecksums[stateFrame] = checksum;
            CompareChecksums(stateFrame);
        }

        private void CompareChecksums(int stateFrame) {
            if (m_ComparedChecksumFrames.Contains(stateFrame) ||
                !m_LocalChecksums.TryGetValue(stateFrame, out var localChecksum) ||
                !m_RemoteChecksums.TryGetValue(stateFrame, out var remoteChecksum)) {
                return;
            }

            m_ComparedChecksumFrames.Add(stateFrame);
            if (localChecksum == remoteChecksum) {
                LastVerifiedChecksumStateFrame = stateFrame;
                return;
            }

            ChecksumMismatchCount++;
            Debug.LogError(
                $"Checksum mismatch. StateFrame={stateFrame}, " +
                $"Local={localChecksum:X8}, Remote={remoteChecksum:X8}");
        }

        private void RemoveStateHistoryAfter(int frame) {
            m_StateFramesToRemove.Clear();
            foreach (var kvp in m_StateHistory) {
                if (kvp.Key > frame) {
                    m_StateFramesToRemove.Add(kvp.Key);
                }
            }
            for (int i = 0; i < m_StateFramesToRemove.Count; i++) {
                m_StateHistory.Remove(m_StateFramesToRemove[i]);
            }
            
            m_StateFramesToRemove.Clear();
        }
    }
}

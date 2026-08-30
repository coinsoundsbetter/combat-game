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
        public int RollbackRevision { get; private set; }
        public bool IsChecksumVerificationAvailable => m_ChecksumTransport != null;
        public int LastVerifiedChecksumStateFrame { get; private set; } = -1;
        public int ChecksumMismatchCount { get; private set; }
        public event Action<GgpoRemoteInputDiagnostic<TInput>> RemoteInputObserved;
        public event Action RollbackObserved;
        
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
            m_LocalChecksums = new Dictionary<int, uint>();
            m_RemoteChecksums = new Dictionary<int, uint>();
            m_ComparedChecksumFrames = new HashSet<int>();
            m_PreRollbackStates = null;
            m_LastRollbackPredictedStates = null;
            m_LastRollbackCorrectedStates = null;
            RollbackRevision = 0;
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
                m_Setting.inputDelayFrames);
            m_Session.ConfirmedFrame += OnConfirmedFrame;
            m_Session.RollbackStarted += OnRollbackStarted;
            m_Session.RollbackCompleted += OnRollbackCompleted;
            m_Session.RemoteInputObserved += OnRemoteInputObserved;
            if (m_ChecksumTransport != null)
                m_ChecksumTransport.RemoteChecksumReceived += ReceiveRemoteChecksum;
            m_TickAcc = 0;
            m_TickInterval = 1f / m_Setting.tickRate;
            m_CurrentFrame = 0;
            for (var sessionIndex = 0; sessionIndex < m_PlayerRegistration.Length; sessionIndex++) {
                var registration = m_PlayerRegistration[sessionIndex];
                if (registration == null) {
                    throw new InvalidOperationException("Player is not registered.");
                }
                
                m_Session.AddPlayer(registration.NetType == NetPlayerType.Local ? 
                    GgpoPlayerType.Local : GgpoPlayerType.Remote);
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
        }

        public void Update() {
            if (!IsStartSession) {
                return;
            }

            m_TickAcc += Time.unscaledDeltaTime;
            var tickCount = 0;
            while (m_TickAcc >= m_TickInterval && tickCount < m_Setting.maxTickPerUpdate) {
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
                Debug.Log($"Checksum verified. StateFrame={stateFrame}, Value={localChecksum:X8}");
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

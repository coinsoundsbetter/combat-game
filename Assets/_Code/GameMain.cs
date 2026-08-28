using System;
using System.Collections.Generic;
using System.IO;
using _Code.GGPO;
using _Code.Replay;
using _Code.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace _Code {
    public class GameMain : MonoBehaviour {
        private const int MaxPlayerCount = 2;
        private const float TickRate = 1f / 60f;
        private const int PresentationHistoryFrames = 64;
        private const string ReplayLogicVersion = "fighter-logic-1";

        // 回滚窗口
        public int maxRollbackFrames = 8;

        // 输入延迟
        public int inputDelayFrames = 2;

        // 每个渲染帧最多更新多少逻辑帧
        public int maxTickPerUpdate = 8;

        private GgpoSession<FighterInput> m_Session;
        private IGgpoTransport<FighterInput> m_Transport;
        private ReplayRecorder m_ReplayRecorder;
        private ReplayPlayer m_ReplayPlayer;

        private readonly FighterInput[] m_FrameInputs = new FighterInput[MaxPlayerCount];
        private readonly Dictionary<int, int> m_FrameChecksums = new Dictionary<int, int>();
        private readonly List<int> m_LocalPlayerIndices = new List<int>();
        private readonly List<int> m_RemotePlayerIndices = new List<int>();

        private readonly Dictionary<int, PlayerState[]> m_PresentationStateHistory =
            new Dictionary<int, PlayerState[]>();

        private readonly List<int> m_PresentationFramesToRemove = new List<int>();

        private PlayerState[] m_PlayerStates;
        private PlayerState[] m_PreviousRenderPlayerStates;
        private PlayerState[] m_CurrentRenderPlayerStates;
        private float m_TickAccumulator;
        private int m_GameFrame;
        private int m_RegisteredPlayerCount;
        private bool m_HasSubmittedLocalInputForCurrentFrame;
        private bool m_IsRunning;
        private bool m_IsReplayMode;
        private bool m_IsReplayFinished;

        public bool IsReplayMode {
            get { return m_IsReplayMode; }
        }

        public bool IsReplayFinished {
            get { return m_IsReplayMode && m_IsReplayFinished; }
        }

        public int ReplayCurrentFrame {
            get { return m_ReplayPlayer == null ? 0 : m_ReplayPlayer.CurrentFrame; }
        }

        public int ReplayFinalFrame {
            get { return m_ReplayPlayer == null ? -1 : m_ReplayPlayer.FinalFrame; }
        }

        public string DefaultReplayPath {
            get {
                return Path.Combine(
                    Application.persistentDataPath,
                    "Replays",
                    "last-match.fgreplay");
            }
        }

        private void Awake() {
            Application.runInBackground = true;
        }

        private void Update() {
            if (!m_IsRunning)
                return;

            m_TickAccumulator += Time.unscaledDeltaTime;

            var tickCount = 0;
            while (m_TickAccumulator >= TickRate && tickCount < maxTickPerUpdate) {
                Tick();
                m_TickAccumulator -= TickRate;
                tickCount++;
            }
        }

        private void OnDestroy() {
            DisposeSession();
        }

        public void StartLocalVersus() {
            StartSession(new GgpoLocalTransport<FighterInput>(0));
            AddPlayer(GgpoPlayerType.Local);
            AddPlayer(GgpoPlayerType.Local);
            StartRunning();
        }

        public void StartRemoteVersus(
            int localPort,
            string remoteIp,
            int remotePort,
            int localPlayerIndex,
            int simulatedReceiveDelayMilliseconds) {
            if (localPlayerIndex < 0 || localPlayerIndex >= MaxPlayerCount)
                throw new ArgumentOutOfRangeException(nameof(localPlayerIndex));
            if (simulatedReceiveDelayMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(simulatedReceiveDelayMilliseconds));

            Debug.Log(
                $"Start remote versus. LocalPort={localPort}, " +
                $"Remote={remoteIp}:{remotePort}, LocalPlayer=P{localPlayerIndex + 1}, " +
                $"SimulatedReceiveDelay={simulatedReceiveDelayMilliseconds}ms");

            ResetSession();
            StartSession(new GgpoUdpTransport<FighterInput>(
                localPort,
                remoteIp,
                remotePort,
                new FighterInputSerializer(),
                simulatedReceiveDelayMilliseconds));

            for (var playerIndex = 0; playerIndex < MaxPlayerCount; playerIndex++)
                AddPlayer(playerIndex == localPlayerIndex
                    ? GgpoPlayerType.Local
                    : GgpoPlayerType.Remote);
            StartRunning();
        }

        public void ResetSession() {
            DisposeSession();
            m_GameFrame = 0;
            m_RegisteredPlayerCount = 0;
            m_TickAccumulator = 0f;
            m_HasSubmittedLocalInputForCurrentFrame = false;
            m_IsRunning = false;
            m_IsReplayMode = false;
            m_IsReplayFinished = false;
            m_ReplayRecorder = null;
            m_ReplayPlayer = null;
            m_PlayerStates = null;
            m_PreviousRenderPlayerStates = null;
            m_CurrentRenderPlayerStates = null;
            m_FrameChecksums.Clear();
            m_PresentationStateHistory.Clear();
            m_PresentationFramesToRemove.Clear();
            m_LocalPlayerIndices.Clear();
            m_RemotePlayerIndices.Clear();
        }

        private void StartSession(IGgpoTransport<FighterInput> transport) {
            DisposeSession();

            m_GameFrame = 0;
            m_RegisteredPlayerCount = 0;
            m_TickAccumulator = 0f;
            m_HasSubmittedLocalInputForCurrentFrame = false;
            m_IsRunning = false;
            m_IsReplayMode = false;
            m_IsReplayFinished = false;
            m_ReplayPlayer = null;
            m_PlayerStates = new PlayerState[MaxPlayerCount];
            m_PreviousRenderPlayerStates = new PlayerState[MaxPlayerCount];
            m_CurrentRenderPlayerStates = new PlayerState[MaxPlayerCount];
            m_FrameChecksums.Clear();
            m_PresentationStateHistory.Clear();
            m_PresentationFramesToRemove.Clear();
            m_LocalPlayerIndices.Clear();
            m_RemotePlayerIndices.Clear();

            StorePresentationState(0);

            m_Transport = transport;
            var callback = new GgpoCallback<FighterInput> {
                OnSessionStarted = OnSessionStarted,
                SaveGameState = SaveGameState,
                LoadGameState = LoadGameState,
                AdvanceFrame = AdvanceFrame,
            };

            m_Session = new GgpoSession<FighterInput>(
                callback,
                m_Transport,
                MaxPlayerCount,
                maxRollbackFrames,
                inputDelayFrames);
            m_Session.ConfirmedFrame += OnConfirmedFrame;

            m_ReplayRecorder = new ReplayRecorder(new ReplayHeader {
                FormatVersion = ReplayHeader.CurrentFormatVersion,
                LogicVersion = ReplayLogicVersion,
                TickRate = Mathf.RoundToInt(1f / TickRate),
                PlayerCount = MaxPlayerCount,
                InitialStates = (PlayerState[])m_PlayerStates.Clone(),
            });
        }

        private void AddPlayer(GgpoPlayerType playerType) {
            var playerIndex = m_Session.AddPlayer(playerType);
            m_RegisteredPlayerCount++;

            if (playerType == GgpoPlayerType.Local)
                m_LocalPlayerIndices.Add(playerIndex);
            else
                m_RemotePlayerIndices.Add(playerIndex);

            Debug.Log($"Add {playerType} player. Index={playerIndex}");
        }

        private void StartRunning() {
            if (m_LocalPlayerIndices.Count == 0)
                throw new InvalidOperationException("At least one local player is required.");

            m_IsRunning = true;
            m_TickAccumulator = 0f;
            Debug.Log("GGPO match started.");
        }

        private void DisposeSession() {
            if (m_Session == null)
                return;

            m_Session.ConfirmedFrame -= OnConfirmedFrame;
            m_Session.Dispose();
            m_Session = null;
            m_Transport = null;
        }

        /// <summary>
        /// 将当前已确认的对局片段保存为可播放回放。
        /// 远程对局若仍有预测帧，需要等待远端输入确认后再保存。
        /// </summary>
        public bool TrySaveReplay(string path, out string message) {
            message = null;
            if (m_IsReplayMode) {
                message = "回放播放中不能再次导出回放。";
                return false;
            }

            if (m_Session == null || m_ReplayRecorder == null) {
                message = "当前没有可导出的对局。";
                return false;
            }

            var finalFrame = m_GameFrame - 1;
            if (finalFrame < 0) {
                message = "对局尚未推进任何逻辑帧。";
                return false;
            }

            if (m_Session.LastConfirmedFrame < finalFrame) {
                message =
                    $"仍有未确认输入：已确认到 {m_Session.LastConfirmedFrame} 帧，" +
                    $"当前模拟到 {finalFrame} 帧。请等待网络追平后再保存。";
                return false;
            }

            if (m_ReplayRecorder.LastRecordedFrame < finalFrame) {
                message = "确认帧记录尚未完成，请稍后再试。";
                return false;
            }

            try {
                m_ReplayRecorder.RecordCheckpoint(finalFrame, CalculateChecksum());
                var replay = m_ReplayRecorder.CreateReplay(finalFrame, true);
                ReplaySerializer.Save(path, replay);
                message = $"回放已保存：{path}";
                return true;
            }
            catch (Exception exception) {
                message = $"保存回放失败：{exception.Message}";
                Debug.LogException(exception);
                return false;
            }
        }

        public bool TryStartReplay(string path, out string message) {
            message = null;
            try {
                var replay = ReplaySerializer.Load(path);
                if (replay.Header.LogicVersion != ReplayLogicVersion) {
                    message =
                        $"回放逻辑版本不匹配。文件：{replay.Header.LogicVersion}，" +
                        $"当前：{ReplayLogicVersion}";
                    return false;
                }

                if (replay.Header.TickRate != Mathf.RoundToInt(1f / TickRate) ||
                    replay.Header.PlayerCount != MaxPlayerCount) {
                    message = "回放的帧率或玩家数与当前游戏不兼容。";
                    return false;
                }

                ResetSession();
                m_ReplayPlayer = new ReplayPlayer(replay);
                m_PlayerStates = m_ReplayPlayer.PlayerStates;
                m_PreviousRenderPlayerStates = new PlayerState[MaxPlayerCount];
                m_CurrentRenderPlayerStates = new PlayerState[MaxPlayerCount];
                Array.Copy(m_PlayerStates, m_PreviousRenderPlayerStates, MaxPlayerCount);
                Array.Copy(m_PlayerStates, m_CurrentRenderPlayerStates, MaxPlayerCount);
                StorePresentationState(0);
                m_IsReplayMode = true;
                m_IsReplayFinished = m_ReplayPlayer.IsFinished;
                m_IsRunning = true;
                m_TickAccumulator = 0f;
                message = $"开始播放回放：共 {replay.FinalFrame + 1} 帧。";
                Debug.Log(message);
                return true;
            }
            catch (Exception exception) {
                message = $"加载回放失败：{exception.Message}";
                Debug.LogException(exception);
                return false;
            }
        }

        public bool TryGetRenderPlayerState(
            int playerIndex,
            out PlayerState previousState,
            out PlayerState currentState,
            out float interpolationAlpha) {
            previousState = default(PlayerState);
            currentState = default(PlayerState);
            interpolationAlpha = 0f;

            if (!m_IsRunning ||
                playerIndex < 0 || playerIndex >= MaxPlayerCount ||
                m_PreviousRenderPlayerStates == null ||
                m_CurrentRenderPlayerStates == null)
                return false;

            previousState = m_PreviousRenderPlayerStates[playerIndex];
            currentState = m_CurrentRenderPlayerStates[playerIndex];
            interpolationAlpha = Mathf.Clamp01(m_TickAccumulator / TickRate);
            return true;
        }

        public bool IsLocalPlayer(int playerIndex) {
            return m_IsRunning &&
                   (m_IsReplayMode || m_LocalPlayerIndices.Contains(playerIndex));
        }

        public bool TryGetConfirmedPlayerState(
            int playerIndex,
            int displayDelayFrames,
            out PlayerState playerState) {
            playerState = default(PlayerState);
            if (!m_IsRunning || m_Session == null ||
                playerIndex < 0 || playerIndex >= MaxPlayerCount ||
                displayDelayFrames < 0)
                return false;

            // 状态帧 N 表示已经模拟完逻辑帧 N - 1 后的状态。
            var stateFrame = Mathf.Min(
                m_GameFrame,
                m_Session.LastConfirmedFrame + 1 - displayDelayFrames);
            if (stateFrame < 0)
                stateFrame = 0;

            PlayerState[] states;
            if (!m_PresentationStateHistory.TryGetValue(stateFrame, out states))
                return false;

            playerState = states[playerIndex];
            return true;
        }

        public bool TryGetNetworkDiagnostics(
            out int currentFrame,
            out int lastConfirmedFrame,
            out int predictedRemoteInputCount,
            out int rollbackCount) {
            currentFrame = 0;
            lastConfirmedFrame = 0;
            predictedRemoteInputCount = 0;
            rollbackCount = 0;

            if (!m_IsRunning || m_Session == null)
                return false;

            currentFrame = m_Session.CurrentFrame;
            lastConfirmedFrame = m_Session.LastConfirmedFrame;
            predictedRemoteInputCount = m_Session.PredictedRemoteInputCount;
            rollbackCount = m_Session.RollbackCount;
            return true;
        }

        private void Tick() {
            if (m_IsReplayMode) {
                TickReplay();
                return;
            }

            SubmitLocalInputsOnce();

            m_Session.Idle(0);

            if (!m_Session.TrySynqhronizeInputs(m_FrameInputs))
                return;

            m_Session.AdvanceFrame();
            m_HasSubmittedLocalInputForCurrentFrame = false;
        }

        private void TickReplay() {
            if (m_ReplayPlayer == null || m_ReplayPlayer.IsFinished) {
                m_IsReplayFinished = true;
                return;
            }

            Array.Copy(
                m_PlayerStates,
                m_PreviousRenderPlayerStates,
                MaxPlayerCount);

            string error;
            if (!m_ReplayPlayer.TryAdvanceOneFrame(out error)) {
                m_IsReplayFinished = true;
                if (!string.IsNullOrEmpty(error))
                    Debug.LogError(error);
                return;
            }

            Array.Copy(
                m_PlayerStates,
                m_CurrentRenderPlayerStates,
                MaxPlayerCount);
            m_GameFrame = m_ReplayPlayer.CurrentFrame;
            StorePresentationState(m_GameFrame);
            PrunePresentationStateHistory();

            if (m_ReplayPlayer.IsFinished) {
                m_IsReplayFinished = true;
                Debug.Log("Replay playback completed.");
            }
        }

        private void SubmitLocalInputsOnce() {
            if (m_HasSubmittedLocalInputForCurrentFrame)
                return;

            for (var i = 0; i < m_LocalPlayerIndices.Count; i++) {
                var playerIndex = m_LocalPlayerIndices[i];
                m_Session.AddLocalInput(playerIndex, ReadLocalInput(playerIndex));
            }

            m_HasSubmittedLocalInputForCurrentFrame = true;
        }

        private FighterInput ReadLocalInput(int playerIndex) {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return default(FighterInput);

            if (playerIndex == 1) {
                return new FighterInput {
                    MoveX =
                        keyboard.leftArrowKey.isPressed ? -1 :
                        keyboard.rightArrowKey.isPressed ? 1 :
                        0,
                    Attack = keyboard.numpad1Key.wasPressedThisFrame,
                };
            }

            return new FighterInput {
                MoveX =
                    keyboard.aKey.isPressed ? -1 :
                    keyboard.dKey.isPressed ? 1 :
                    0,
                Attack = keyboard.jKey.wasPressedThisFrame,
            };
        }

        private void AdvanceFrame(int frame, FighterInput[] inputs) {
            Array.Copy(
                m_PlayerStates,
                m_PreviousRenderPlayerStates,
                MaxPlayerCount);
            FighterSimulation.SimulateFrame(m_PlayerStates, inputs);
            Array.Copy(
                m_PlayerStates,
                m_CurrentRenderPlayerStates,
                MaxPlayerCount);
            StorePresentationState(frame + 1);
            PrunePresentationStateHistory();

            var checksum = CalculateChecksum();
            m_FrameChecksums[frame] = checksum;
            m_GameFrame = frame + 1;

            if (frame % 30 == 0) {
                Debug.Log(
                    $"Frame={frame}, Checksum={checksum}, " +
                    $"P0=({m_PlayerStates[0].X},{m_PlayerStates[0].AttackCount}), " +
                    $"P1=({m_PlayerStates[1].X},{m_PlayerStates[1].AttackCount})");
            }
        }

        private void OnConfirmedFrame(int frame, FighterInput[] inputs) {
            if (m_ReplayRecorder == null)
                return;

            m_ReplayRecorder.RecordConfirmedFrame(frame, inputs);

            int checksum;
            if (frame % 60 == 0 && m_FrameChecksums.TryGetValue(frame, out checksum))
                m_ReplayRecorder.RecordCheckpoint(frame, checksum);
        }

        private GgpoSavedState SaveGameState(int frame) {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream)) {
                writer.Write(frame);
                writer.Write(m_GameFrame);

                for (var i = 0; i < MaxPlayerCount; i++) {
                    writer.Write(m_PlayerStates[i].X);
                    writer.Write(m_PlayerStates[i].AttackCount);
                }

                return new GgpoSavedState(stream.ToArray());
            }
        }

        private void LoadGameState(byte[] buffer) {
            using (var stream = new MemoryStream(buffer))
            using (var reader = new BinaryReader(stream)) {
                var snapshotFrame = reader.ReadInt32();
                m_GameFrame = reader.ReadInt32();

                for (var i = 0; i < MaxPlayerCount; i++) {
                    m_PlayerStates[i].X = reader.ReadInt32();
                    m_PlayerStates[i].AttackCount = reader.ReadInt32();
                }

                RemovePresentationStatesAfter(snapshotFrame);
                StorePresentationState(snapshotFrame);

                Debug.Log($"Rollback load snapshot. SnapshotFrame={snapshotFrame}, GameFrame={m_GameFrame}");
            }
        }

        private int CalculateChecksum() {
            unchecked {
                var hash = 17;
                for (var i = 0; i < MaxPlayerCount; i++) {
                    hash = hash * 31 + m_PlayerStates[i].X;
                    hash = hash * 31 + m_PlayerStates[i].AttackCount;
                }

                return hash;
            }
        }

        private void StorePresentationState(int stateFrame) {
            var states = new PlayerState[MaxPlayerCount];
            Array.Copy(m_PlayerStates, states, MaxPlayerCount);
            m_PresentationStateHistory[stateFrame] = states;
        }

        private void PrunePresentationStateHistory() {
            var firstRetainedFrame = m_GameFrame - PresentationHistoryFrames;
            if (firstRetainedFrame <= 0)
                return;

            m_PresentationFramesToRemove.Clear();
            foreach (var pair in m_PresentationStateHistory) {
                if (pair.Key < firstRetainedFrame)
                    m_PresentationFramesToRemove.Add(pair.Key);
            }

            for (var i = 0; i < m_PresentationFramesToRemove.Count; i++)
                m_PresentationStateHistory.Remove(m_PresentationFramesToRemove[i]);
            m_PresentationFramesToRemove.Clear();
        }

        private void RemovePresentationStatesAfter(int retainedStateFrame) {
            m_PresentationFramesToRemove.Clear();
            foreach (var pair in m_PresentationStateHistory) {
                if (pair.Key > retainedStateFrame)
                    m_PresentationFramesToRemove.Add(pair.Key);
            }

            for (var i = 0; i < m_PresentationFramesToRemove.Count; i++)
                m_PresentationStateHistory.Remove(m_PresentationFramesToRemove[i]);
            m_PresentationFramesToRemove.Clear();
        }

        private void OnSessionStarted() {
            Debug.Log("GGPO session started.");
        }

        private sealed class FighterInputSerializer
            : IGgpoInputSerializer<FighterInput> {
            public byte[] Encode(FighterInput input) {
                return new[] {
                    unchecked((byte)input.MoveX),
                    input.Attack ? (byte)1 : (byte)0,
                };
            }

            public bool TryDecode(byte[] bytes, out FighterInput input) {
                input = default(FighterInput);
                if (bytes == null || bytes.Length != 2)
                    return false;

                var moveX = unchecked((sbyte)bytes[0]);
                if (moveX < -1 || moveX > 1 || bytes[1] > 1)
                    return false;

                input = new FighterInput {
                    MoveX = moveX,
                    Attack = bytes[1] != 0,
                };
                return true;
            }
        }
    }
}
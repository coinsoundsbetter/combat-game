using System;
using System.Collections.Generic;
using System.IO;
using _Code.GGPO;
using _Code.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace _Code {
    public class GameMain : MonoBehaviour {
        private const int MaxPlayerCount = 2;
        private const float TickRate = 1f / 60f;
        private const int PresentationHistoryFrames = 64;

        // 回滚窗口
        public int maxRollbackFrames = 8;

        // 输入延迟
        public int inputDelayFrames = 2;

        // 每个渲染帧最多更新多少逻辑帧
        public int maxTickPerUpdate = 8;

        private GgpoSession<FighterInput> m_Session;
        private IGgpoTransport<FighterInput> m_Transport;

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

            m_Session.Dispose();
            m_Session = null;
            m_Transport = null;
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
            return m_IsRunning && m_LocalPlayerIndices.Contains(playerIndex);
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
            SubmitLocalInputsOnce();

            m_Session.Idle(0);

            if (!m_Session.TrySynqhronizeInputs(m_FrameInputs))
                return;

            m_Session.AdvanceFrame();
            m_HasSubmittedLocalInputForCurrentFrame = false;
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

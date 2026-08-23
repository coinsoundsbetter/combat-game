using System;
using _Src.GGPO;
using _Src.Serialization;
using UnityEngine;

namespace _Src.Game {
    public class GameMain : MonoBehaviour {
        private const int InputDelay = 2;
        private const int MaxRollback = 8;
        private const float TickRate = 1f / 60f;
        private const int MaxTicksPerUpdate = 8;

        private GgpoSession<PlayerInput> m_Session;
        private PlayerInput[] m_FrameInputs = new PlayerInput[2];
        private bool[] m_HasQueuedInputs = new bool[2];
        private GgpoPlayerType[] m_PlayerTypes = new GgpoPlayerType[2];
        private float m_TickAccumulator;
        private GameState m_State;

        private GameInputManager m_InputManager;

        public bool HasSession => m_Session != null;
        public int CurrentFrame => m_Session?.CurrentFrame ?? 0;
        public int Player1Hp => m_State.P1.Hp;
        public int Player2Hp => m_State.P2.Hp;

        private void Start() {
            m_InputManager = new GameInputManager();
        }

        private void Update() {
            if (m_Session == null)
                return;

            m_Session?.Idle(0);
            m_TickAccumulator += Time.unscaledDeltaTime;

            var ticksThisUpdate = 0;
            while (m_TickAccumulator >= TickRate &&
                   ticksThisUpdate < MaxTicksPerUpdate) {
                if (!TrySimulateOneTick())
                    break;

                m_TickAccumulator -= TickRate;
                ticksThisUpdate++;
            }

            if (ticksThisUpdate == MaxTicksPerUpdate)
                m_TickAccumulator = Mathf.Min(m_TickAccumulator, TickRate);
        }

        private void OnDestroy() {
            m_Session?.Close();
        }

        public void InitSession(PlayMode playMode, ConnectInfo connectInfo) {
            if (playMode == PlayMode.Remote &&
                (connectInfo.LocalPlayerIndex < 0 || connectInfo.LocalPlayerIndex >= 2)) {
                throw new ArgumentOutOfRangeException(nameof(connectInfo));
            }

            m_Session?.Close();
            m_TickAccumulator = 0f;
            Array.Clear(m_HasQueuedInputs, 0, m_HasQueuedInputs.Length);

            IGgpoTransport<PlayerInput> transport;
            if (playMode == PlayMode.Local) {
                transport = new GgpoLocalTransport<PlayerInput>();
            }
            else {
                transport = new GgpoTransport<PlayerInput>(
                    connectInfo.LocalPort,
                    connectInfo.TargetAddress,
                    connectInfo.TargetPort,
                    new PlayerInputSerializer());
            }

            m_Session = new GgpoSession<PlayerInput>(
                new GgpoCallback<PlayerInput> {
                    OnSessionStarted = OnSessionStarted,
                    SaveGameState = OnSerializeGame,
                    LoadGameState = OnDeserializeGame,
                    AdvanceFrame = OnAdvance
                },
                transport,
                MaxRollback);

            for (var playerIndex = 0; playerIndex < 2; playerIndex++) {
                bool isLocal = playMode == PlayMode.Local ||
                               playerIndex == connectInfo.LocalPlayerIndex;
                m_PlayerTypes[playerIndex] = isLocal
                    ? GgpoPlayerType.Local
                    : GgpoPlayerType.Remote;
                m_Session.AddPlayer(m_PlayerTypes[playerIndex], InputDelay);
            }
        }

        private bool TrySimulateOneTick() {
            m_InputManager.Update();

            for (var playerIndex = 0; playerIndex < m_Session.PlayerCount; playerIndex++) {
                if (m_PlayerTypes[playerIndex] != GgpoPlayerType.Local ||
                    m_HasQueuedInputs[playerIndex]) {
                    continue;
                }

                m_Session.AddLocalInput(
                    playerIndex,
                    m_InputManager.GetPlayerInput(playerIndex));
                m_HasQueuedInputs[playerIndex] = true;
            }

            if (!m_Session.TrySynchronizeInputs(m_FrameInputs))
                return false;

            m_Session.AdvanceFrame();
            Array.Clear(m_HasQueuedInputs, 0, m_HasQueuedInputs.Length);
            return true;
        }

        private void OnSessionStarted() {
            m_State = new GameState {
                RandomSeed = 1,
                P1 = new PlayerState { Hp = 100 },
                P2 = new PlayerState { Hp = 100 }
            };
        }

        private void OnAdvance(int frame, PlayerInput[] inputs) {
            m_State.Frame = frame;
            SimulatePlayerInput(ref m_State.P1, inputs[0]);
            SimulatePlayerInput(ref m_State.P2, inputs[1]);
        }

        private static void SimulatePlayerInput(ref PlayerState player, PlayerInput input) {
            if ((input.Buttons & 1) != 0)
                player.Hp = Math.Max(0, player.Hp - 1);
        }

        private GgpoSavedState OnSerializeGame(int frame) {
            var buffer = new byte[16];
            DeterministicBinary.WriteInt32(buffer, 0, m_State.Frame);
            DeterministicBinary.WriteUInt32(buffer, 4, m_State.RandomSeed);
            DeterministicBinary.WriteInt32(buffer, 8, m_State.P1.Hp);
            DeterministicBinary.WriteInt32(buffer, 12, m_State.P2.Hp);
            return new GgpoSavedState {
                Buffer = buffer,
                Checksums = DeterministicBinary.CalculateChecksum(buffer)
            };
        }

        private void OnDeserializeGame(byte[] buffer) {
            if (buffer == null || buffer.Length != 16)
                throw new ArgumentException("Invalid game-state buffer.", nameof(buffer));

            m_State.Frame = DeterministicBinary.ReadInt32(buffer, 0);
            m_State.RandomSeed = DeterministicBinary.ReadUInt32(buffer, 4);
            m_State.P1.Hp = DeterministicBinary.ReadInt32(buffer, 8);
            m_State.P2.Hp = DeterministicBinary.ReadInt32(buffer, 12);
        }

    }
}

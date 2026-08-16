using System;
using GLMFighter.Core;
using GLMFighter.Network;

namespace GLMFighter.Runtime
{
    /// <summary>
    /// Owns match/lobby state transitions. Combat rules remain in
    /// BattleSimulation; this class only decides when a match is configured.
    /// </summary>
    public sealed class BattleSessionController
    {
        private readonly BattleSimulation _simulation;
        private readonly BattleRoleCatalog _roles;
        private readonly BattleNetworkCoordinator _network;
        private readonly BattleInputSync _inputSync;
        private readonly BattleChecksumTracker _checksums;
        private readonly BattleTickDriver _tickDriver;

        public BattleSessionController(
            BattleSimulation simulation,
            BattleRoleCatalog roles,
            BattleNetworkCoordinator network,
            BattleInputSync inputSync,
            BattleChecksumTracker checksums,
            BattleTickDriver tickDriver)
        {
            _simulation = simulation;
            _roles = roles;
            _network = network;
            _inputSync = inputSync;
            _checksums = checksums;
            _tickDriver = tickDriver;
        }

        public event Action<int, int> BattleStarted;
        public event Action PreviewBattleReset;

        public BattleRunMode RunMode { get; private set; } = BattleRunMode.Local;
        public int AssignedPlayerIndex { get; private set; }
        public int LocalCharacterIndex { get; private set; }
        public int RemoteCharacterIndex { get; private set; }
        public bool HasPlayerAssignment { get; private set; }
        public bool LocalReady { get; private set; }
        public bool RemoteReady { get; private set; }
        public bool BattleStartedState { get; private set; }
        public bool HasOpponent => _network.HasOpponent;
        public int CharacterSlotCount => _roles.CharacterSlotCount;

        public void SelectRunMode(BattleRunMode mode)
        {
            if (!BattleStartedState)
            {
                RunMode = mode;
            }
        }

        public void BeginNetworkLobby(int assignedPlayerIndex)
        {
            RunMode = BattleRunMode.P2P;
            BattleStartedState = false;
            AssignedPlayerIndex = assignedPlayerIndex;
            ResetRoomReadyState();
        }

        public void StartBattle(int assignedPlayerIndex)
        {
            AssignedPlayerIndex = assignedPlayerIndex;
            HasPlayerAssignment = true;
            RunMode = BattleRunMode.P2P;
            BattleStartedState = true;
            _tickDriver.Reset();
            ResetSimulationState();

            int playerOneRoleIndex = GetPlayerOneRoleIndex();
            int playerTwoRoleIndex = GetPlayerTwoRoleIndex();
            _simulation.Reset(
                _roles.GetRoleStats(playerOneRoleIndex),
                _roles.GetRoleStats(playerTwoRoleIndex));
            _inputSync.SeedNeutralFrames();

            if (_network.IsListener)
            {
                _network.SendStartBattle(1);
            }

            if (BattleStarted != null)
            {
                BattleStarted(playerOneRoleIndex, playerTwoRoleIndex);
            }
        }

        public void StartLocalBattle()
        {
            _network.Dispose();
            ResetRoomReadyState();
            RunMode = BattleRunMode.Local;
            AssignedPlayerIndex = 0;
            HasPlayerAssignment = true;
            BattleStartedState = true;
            _tickDriver.Reset();
            ResetSimulationState();
            _simulation.Reset(_roles.GetRoleStats(0), _roles.GetRoleStats(0));

            if (BattleStarted != null)
            {
                BattleStarted(0, 0);
            }
        }

        public void ResetPreviewBattle()
        {
            _simulation.Reset(_roles.GetRoleStats(0), _roles.GetRoleStats(0));

            if (PreviewBattleReset != null)
            {
                PreviewBattleReset();
            }
        }

        public void LeaveBattle()
        {
            BattleStartedState = false;
            _network.Dispose();
            ResetRoomReadyState();
            ResetSimulationState();
            ResetPreviewBattle();
        }

        public void AssignLocalPlayer(int assignedPlayerIndex)
        {
            AssignedPlayerIndex = assignedPlayerIndex;
            HasPlayerAssignment = true;
            SendLocalLobbyState();
        }

        public void StoreRemoteLobbyState(int characterIndex, bool ready)
        {
            RemoteCharacterIndex = _roles.ClampCharacterIndex(characterIndex);
            RemoteReady = ready;
            TryStartReadyBattle();
        }

        public void SelectLocalCharacter(int characterIndex)
        {
            int clampedCharacterIndex = _roles.ClampCharacterIndex(characterIndex);

            if (LocalCharacterIndex == clampedCharacterIndex)
            {
                return;
            }

            LocalCharacterIndex = clampedCharacterIndex;
            LocalReady = false;
            SendLocalLobbyState();
        }

        public void SetReady(bool ready)
        {
            if (!_network.HasOpponent || !HasPlayerAssignment)
            {
                return;
            }

            LocalReady = ready;
            SendLocalLobbyState();
            TryStartReadyBattle();
        }

        public int GetPlayerOneRoleIndex()
        {
            return AssignedPlayerIndex == 0 ? LocalCharacterIndex : RemoteCharacterIndex;
        }

        public int GetPlayerTwoRoleIndex()
        {
            return AssignedPlayerIndex == 1 ? LocalCharacterIndex : RemoteCharacterIndex;
        }

        public void ResetRoomReadyState()
        {
            HasPlayerAssignment = false;
            LocalReady = false;
            RemoteReady = false;
            LocalCharacterIndex = 0;
            RemoteCharacterIndex = 0;
        }

        private void TryStartReadyBattle()
        {
            if (RunMode != BattleRunMode.P2P || BattleStartedState || !_network.IsListener || !_network.HasOpponent)
            {
                return;
            }

            if (!HasPlayerAssignment || !LocalReady || !RemoteReady)
            {
                return;
            }

            StartBattle(0);
        }

        private void SendLocalLobbyState()
        {
            if (RunMode != BattleRunMode.P2P || !_network.HasOpponent || !HasPlayerAssignment)
            {
                return;
            }

            _network.SendLobbyState(LocalCharacterIndex, LocalReady);
        }

        private void ResetSimulationState()
        {
            _inputSync.Reset();
            _checksums.Reset();
        }
    }
}

using System;
using GLMFighter.Core;
using UnityEngine;

namespace GLMFighter.Runtime
{
    /// <summary>
    /// Temporary immediate-mode HUD. It reads runtime state and emits session
    /// commands; it does not own simulation or transport data.
    /// </summary>
    public sealed class BattleDebugHud
    {
        public string JoinAddress { get; private set; } = "127.0.0.1";
        public string PortText { get; private set; } = "7777";
        public bool ShowDebugBoxes { get; private set; }

        public void Configure(ushort defaultPort)
        {
            PortText = defaultPort.ToString();
            ShowDebugBoxes = false;
        }

        public void Draw(
            BattleSessionController session,
            BattleNetworkCoordinator network,
            BattleSimulation simulation,
            BattleInputSync inputSync,
            BattleChecksumTracker checksums,
            bool debugLogicWorld,
            bool drawLogicWorldDebugHud,
            bool enabled,
            int inputDelayFrames,
            int inputRedundancyFrames,
            ushort defaultPort,
            FighterInput lastLocalInput,
            Func<string> getLogicWorldDebugText,
            Action startLocalBattle,
            Action resetPreviewBattle,
            Action leaveBattle,
            Action applyViews)
        {
            if (!enabled)
            {
                return;
            }

            if (!session.BattleStartedState)
            {
                DrawLobby(session, network, resetPreviewBattle, startLocalBattle, defaultPort);
            }
            else
            {
                DrawBattleHud(
                    session,
                    network,
                    simulation,
                    inputSync,
                    checksums,
                    debugLogicWorld,
                    drawLogicWorldDebugHud,
                    inputDelayFrames,
                    inputRedundancyFrames,
                    lastLocalInput,
                    getLogicWorldDebugText,
                    leaveBattle,
                    applyViews);
            }
        }

        private void DrawLobby(
            BattleSessionController session,
            BattleNetworkCoordinator network,
            Action resetPreviewBattle,
            Action startLocalBattle,
            ushort defaultPort)
        {
            GUI.Box(new Rect(20, 20, 390, 430), "Battle Mode");

            if (GUI.Button(new Rect(40, 55, 150, 36), session.RunMode == BattleRunMode.Local ? "Local: Selected" : "Local"))
            {
                session.SelectRunMode(BattleRunMode.Local);
            }

            if (GUI.Button(new Rect(225, 55, 150, 36), session.RunMode == BattleRunMode.P2P ? "P2P: Selected" : "P2P"))
            {
                session.SelectRunMode(BattleRunMode.P2P);
            }

            if (session.RunMode == BattleRunMode.Local)
            {
                DrawLocalLobby(startLocalBattle);
            }
            else
            {
                DrawP2PLobby(session, network, resetPreviewBattle, defaultPort);
            }
        }

        private static void DrawLocalLobby(Action startLocalBattle)
        {
            GUI.Label(new Rect(40, 110, 330, 24), "Local duel uses both players on this machine.");

            if (GUI.Button(new Rect(40, 150, 150, 36), "Start Local"))
            {
                startLocalBattle();
            }

            GUI.Label(new Rect(40, 205, 330, 24), "P1: A/D/W/J/L");
            GUI.Label(new Rect(40, 229, 330, 24), "P2: Arrows + N or Keypad 1 + Right Shift");
        }

        private void DrawP2PLobby(
            BattleSessionController session,
            BattleNetworkCoordinator network,
            Action resetPreviewBattle,
            ushort defaultPort)
        {
            GUI.Label(new Rect(40, 110, 120, 24), "Address");
            JoinAddress = GUI.TextField(new Rect(155, 110, 220, 24), JoinAddress);

            GUI.Label(new Rect(40, 145, 120, 24), "Port");
            PortText = GUI.TextField(new Rect(155, 145, 220, 24), PortText);

            if (GUI.Button(new Rect(40, 185, 150, 36), "Listen"))
            {
                ushort port;
                if (TryReadPort(defaultPort, out port))
                {
                    session.BeginNetworkLobby(0);
                    resetPreviewBattle();
                    network.StartListening(port);
                }
            }

            if (GUI.Button(new Rect(225, 185, 150, 36), "Connect"))
            {
                ushort port;
                if (TryReadPort(defaultPort, out port))
                {
                    session.BeginNetworkLobby(1);
                    resetPreviewBattle();
                    network.ConnectToPeer(JoinAddress, port);
                }
            }

            if (network.IsRunning && GUI.Button(new Rect(40, 235, 150, 36), "Disconnect"))
            {
                session.LeaveBattle();
            }

            GUI.Label(new Rect(40, 275, 340, 24), network.Status);
            GUI.Label(new Rect(40, 299, 340, 24), GetLobbyHint(network));
            DrawP2PReadyPanel(session);
        }

        private static void DrawP2PReadyPanel(BattleSessionController session)
        {
            if (!session.HasOpponent)
            {
                return;
            }

            GUI.Label(new Rect(40, 330, 340, 24), "Role: " + (session.HasPlayerAssignment ? "P" + (session.AssignedPlayerIndex + 1) : "Assigning"));
            GUI.Label(new Rect(40, 354, 340, 24), "Character: " + session.LocalCharacterIndex + "    Opponent: " + session.RemoteCharacterIndex);

            bool canChangeCharacter = session.CharacterSlotCount > 1 && !session.LocalReady;

            GUI.enabled = canChangeCharacter;
            if (GUI.Button(new Rect(40, 384, 70, 32), "Prev"))
            {
                session.SelectLocalCharacter(session.LocalCharacterIndex - 1);
            }

            if (GUI.Button(new Rect(118, 384, 70, 32), "Next"))
            {
                session.SelectLocalCharacter(session.LocalCharacterIndex + 1);
            }

            GUI.enabled = session.HasPlayerAssignment;
            string readyLabel = session.LocalReady ? "Unready" : "Ready";
            if (GUI.Button(new Rect(225, 384, 150, 32), readyLabel))
            {
                session.SetReady(!session.LocalReady);
            }

            GUI.enabled = true;
            GUI.Label(new Rect(40, 418, 340, 24), "Ready: " + session.LocalReady + "    Opponent ready: " + session.RemoteReady);
        }

        private void DrawBattleHud(
            BattleSessionController session,
            BattleNetworkCoordinator network,
            BattleSimulation simulation,
            BattleInputSync inputSync,
            BattleChecksumTracker checksums,
            bool debugLogicWorld,
            bool drawLogicWorldDebugHud,
            int inputDelayFrames,
            int inputRedundancyFrames,
            FighterInput lastLocalInput,
            Func<string> getLogicWorldDebugText,
            Action leaveBattle,
            Action applyViews)
        {
            GUI.Label(new Rect(12, 12, 360, 24), "P1 HP " + simulation.PlayerOne.Health + "    P2 HP " + simulation.PlayerTwo.Health);
            GUI.Label(new Rect(12, 36, 420, 24), "Frame " + simulation.Frame + "    " + (simulation.WinnerIndex < 0 ? "Fight" : "Winner P" + (simulation.WinnerIndex + 1)));
            GUI.Label(new Rect(12, 60, 560, 24), "Mode: " + session.RunMode + GetRoleLabel(session, inputDelayFrames, inputRedundancyFrames));
            GUI.Label(new Rect(12, 84, 620, 24), "Focused: " + Application.isFocused + "    P1 input: " + BattleInputReader.Format(lastLocalInput));

            if (session.RunMode == BattleRunMode.P2P)
            {
                GUI.Label(new Rect(12, 108, 620, 24), "Local latest: " + inputSync.LocalLatestInputFrame + "    Remote latest: " + inputSync.RemoteLatestInputFrame + "    Waiting remote: " + inputSync.WaitingForRemoteInput);
                GUI.Label(new Rect(12, 132, 760f, 24), "Checksum frame: " + checksums.LastChecksumFrame + "    Local: " + checksums.LastLocalChecksum + "    Remote: " + checksums.LastRemoteChecksum + "    Desync: " + (checksums.DesyncFrame >= 0 ? checksums.DesyncFrame.ToString() : "none"));
            }

            if (debugLogicWorld && drawLogicWorldDebugHud)
            {
                DrawLogicWorldDebugHud(getLogicWorldDebugText);
            }

            const float actionButtonWidth = 120f;
            const float actionButtonHeight = 32f;
            const float actionButtonGap = 8f;
            const float actionButtonMargin = 12f;
            Rect showBoxesRect = new Rect(Screen.width - actionButtonMargin - actionButtonWidth, actionButtonMargin, actionButtonWidth, actionButtonHeight);
            Rect leaveRect = new Rect(showBoxesRect.x - actionButtonGap - actionButtonWidth, actionButtonMargin, actionButtonWidth, actionButtonHeight);

            if (GUI.Button(leaveRect, "Leave"))
            {
                leaveBattle();
                applyViews();
            }

            if (GUI.Button(showBoxesRect, ShowDebugBoxes ? "Hide Boxes" : "Show Boxes"))
            {
                ShowDebugBoxes = !ShowDebugBoxes;
            }
        }

        private static string GetRoleLabel(BattleSessionController session, int inputDelayFrames, int inputRedundancyFrames)
        {
            if (session.RunMode == BattleRunMode.Local)
            {
                return "    P1/P2 local controls";
            }

            return "    P" + (session.AssignedPlayerIndex + 1) + "    Delay: " + inputDelayFrames + "f    Redundancy: " + inputRedundancyFrames + "f";
        }

        private static void DrawLogicWorldDebugHud(Func<string> getLogicWorldDebugText)
        {
            Rect panel = new Rect(12, Screen.height - 210f, 760f, 198f);
            GUI.Box(panel, "Logic World");
            GUI.Label(new Rect(panel.x + 12f, panel.y + 24f, panel.width - 24f, panel.height - 36f), getLogicWorldDebugText());
        }

        private static string GetLobbyHint(BattleNetworkCoordinator network)
        {
            if (network.IsWaitingForPeer)
            {
                return "Start another instance and connect to this address.";
            }

            if (!network.IsRunning)
            {
                return "One player listens, the other connects.";
            }

            if (network.HasOpponent)
            {
                return "Choose a character and ready up.";
            }

            return "Waiting for connection handshake.";
        }

        private bool TryReadPort(ushort defaultPort, out ushort port)
        {
            if (ushort.TryParse(PortText, out port))
            {
                return true;
            }

            port = defaultPort;
            PortText = defaultPort.ToString();
            return false;
        }
    }
}

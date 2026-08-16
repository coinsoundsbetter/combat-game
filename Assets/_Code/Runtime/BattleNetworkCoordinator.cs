using System;
using GLMFighter.Network;

namespace GLMFighter.Runtime
{
    /// <summary>
    /// Runtime-facing facade over the transport room. Packet interpretation stays
    /// in the battle runner for now; transport lifecycle no longer does.
    /// </summary>
    public sealed class BattleNetworkCoordinator : IDisposable
    {
        private readonly LiteNetLibRoom _room = new LiteNetLibRoom();

        public bool IsListener => _room.IsListener;
        public bool IsRunning => _room.IsRunning;
        public bool HasOpponent => _room.HasOpponent;
        public bool IsWaitingForPeer => _room.IsWaitingForPeer;
        public string Status => _room.Status;

        public void Update(Action<TransportPacket> packetHandler)
        {
            _room.Update();

            TransportPacket packet;
            while (_room.TryReceive(out packet))
            {
                if (packetHandler != null)
                {
                    packetHandler(packet);
                }
            }
        }

        public void StartListening(ushort port)
        {
            _room.StartListening(port);
        }

        public void ConnectToPeer(string address, ushort port)
        {
            _room.ConnectToPeer(address, port);
        }

        public void SendStartBattle(int assignedPlayerIndex)
        {
            _room.SendStartBattleToPeer(assignedPlayerIndex);
        }

        public void SendLobbyState(int characterIndex, bool ready)
        {
            _room.SendLobbyStateToPeer(characterIndex, ready);
        }

        public void SendInputBundle(System.Collections.Generic.IList<InputFrameData> inputs)
        {
            _room.SendInputBundleToPeer(inputs);
        }

        public void SendChecksum(int frame, int checksum)
        {
            _room.SendChecksumToPeer(frame, checksum);
        }

        public void Dispose()
        {
            _room.Dispose();
        }
    }
}

using System;
using System.Collections.Generic;
using GLMFighter.Core;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace GLMFighter.Network
{
    public sealed class LiteNetLibRoom : IDisposable
    {
        private const string ConnectionKey = "GLMFighter";

        private readonly Queue<TransportPacket> _receivedPackets = new Queue<TransportPacket>();
        private readonly NetDataWriter _writer = new NetDataWriter();

        private EventBasedNetListener _listener;
        private NetManager _netManager;
        private NetPeer _peer;

        public bool IsListener { get; private set; }
        public bool IsConnector { get; private set; }
        public string Status { get; private set; } = "Not connected";

        public bool HasOpponent
        {
            get { return _peer != null && _peer.ConnectionState == ConnectionState.Connected; }
        }

        public bool IsRunning
        {
            get { return _netManager != null && _netManager.IsRunning; }
        }

        public bool IsWaitingForPeer
        {
            get { return IsListener && IsRunning && !HasOpponent; }
        }

        public void StartListening(ushort port)
        {
            Dispose();
            CreateManager();

            if (!_netManager.Start(port))
            {
                Status = "Failed to bind port " + port;
                Dispose();
                return;
            }

            IsListener = true;
            IsConnector = false;
            Status = "Waiting for peer on port " + port;
        }

        public void ConnectToPeer(string address, ushort port)
        {
            Dispose();
            CreateManager();

            _netManager.Start();
            _peer = _netManager.Connect(address, port, ConnectionKey);
            IsListener = false;
            IsConnector = true;
            Status = "Connecting to " + address + ":" + port;
        }

        public void Update()
        {
            if (_netManager == null)
            {
                return;
            }

            _netManager.PollEvents();
        }

        public bool TryReceive(out TransportPacket packet)
        {
            if (_receivedPackets.Count > 0)
            {
                packet = _receivedPackets.Dequeue();
                return true;
            }

            packet = new TransportPacket();
            return false;
        }

        public void SendStartBattleToPeer(int assignedPlayerIndex)
        {
            if (!HasOpponent)
            {
                return;
            }

            _writer.Reset();
            LiteNetPacketCodec.WriteStartBattle(_writer, assignedPlayerIndex);
            _peer.Send(_writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendAssignPlayerToPeer(int assignedPlayerIndex)
        {
            if (!HasOpponent)
            {
                return;
            }

            _writer.Reset();
            LiteNetPacketCodec.WriteAssignPlayer(_writer, assignedPlayerIndex);
            _peer.Send(_writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendLobbyStateToPeer(int characterIndex, bool ready)
        {
            if (!HasOpponent)
            {
                return;
            }

            _writer.Reset();
            LiteNetPacketCodec.WriteLobbyState(_writer, characterIndex, ready);
            _peer.Send(_writer, DeliveryMethod.ReliableOrdered);
        }

        public void SendInputBundleToPeer(IList<InputFrameData> inputs)
        {
            if (!HasOpponent || inputs.Count == 0)
            {
                return;
            }

            _writer.Reset();
            LiteNetPacketCodec.WriteInputBundle(_writer, inputs);
            _peer.Send(_writer, DeliveryMethod.Unreliable);
        }

        public void SendChecksumToPeer(int frame, int checksum)
        {
            if (!HasOpponent)
            {
                return;
            }

            _writer.Reset();
            LiteNetPacketCodec.WriteChecksum(_writer, frame, checksum);
            _peer.Send(_writer, DeliveryMethod.ReliableOrdered);
        }

        public void Dispose()
        {
            if (_netManager != null)
            {
                _netManager.Stop();
            }

            _listener = null;
            _netManager = null;
            _peer = null;
            _receivedPackets.Clear();
            IsListener = false;
            IsConnector = false;
            Status = "Not connected";
        }

        private void CreateManager()
        {
            _listener = new EventBasedNetListener();
            _netManager = new NetManager(_listener)
            {
                AutoRecycle = true,
                DisconnectTimeout = 10000,
                UnconnectedMessagesEnabled = false
            };

            _listener.ConnectionRequestEvent += OnConnectionRequest;
            _listener.PeerConnectedEvent += OnPeerConnected;
            _listener.PeerDisconnectedEvent += OnPeerDisconnected;
            _listener.NetworkReceiveEvent += OnNetworkReceive;
        }

        private void OnConnectionRequest(ConnectionRequest request)
        {
            if (!IsListener)
            {
                request.Reject();
                return;
            }

            if (_peer != null && _peer.ConnectionState == ConnectionState.Connected)
            {
                request.Reject();
                Status = "Room full";
                return;
            }

            request.AcceptIfKey(ConnectionKey);
        }

        private void OnPeerConnected(NetPeer peer)
        {
            _peer = peer;
            Status = IsListener ? "Opponent connected" : "Connected";

            if (IsListener)
            {
                _receivedPackets.Enqueue(TransportPacket.AssignPlayer(0));
                SendAssignPlayerToPeer(1);
            }
        }

        private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            if (_peer == peer)
            {
                _peer = null;
            }

            Status = "Disconnected";
        }

        private void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            try
            {
                TransportPacket packet = LiteNetPacketCodec.ReadPacket(reader);
                if (packet.Type != TransportPacketType.None)
                {
                    _receivedPackets.Enqueue(packet);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Failed to read LiteNetLib packet: " + exception.Message);
            }
        }
    }
}

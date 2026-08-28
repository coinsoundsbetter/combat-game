using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace _Src.GGPO {
    /// <summary>
    /// 双实例原型使用的 UDP 输入传输层。
    /// 每次发送都会附带最近输入，以缓解少量 UDP 丢包。
    /// </summary>
    public sealed class GgpoUdpTransport<TInput> : IGgpoTransport<TInput> {
        private const byte ProtocolVersion = 1;
        private const int HeaderSize = 6;
        private const int EntryHeaderSize = 7;
        private const int MaxPacketSize = 1200;
        private const int ResendHistoryCount = 32;

        private readonly UdpClient m_Udp;
        private readonly IPEndPoint m_RemoteEndpoint;
        private readonly IGgpoInputSerializer<TInput> m_Serializer;
        private readonly long m_SimulatedReceiveDelayTicks;
        private readonly SortedDictionary<long, InputEntry> m_History =
            new SortedDictionary<long, InputEntry>();
        private readonly List<PendingPacket> m_PendingPackets =
            new List<PendingPacket>();
        private bool m_Disposed;

        public GgpoUdpTransport(
            int localPort,
            string remoteIp,
            int remotePort,
            IGgpoInputSerializer<TInput> serializer,
            int simulatedReceiveDelayMilliseconds = 0) {
            if (serializer == null)
                throw new ArgumentNullException(nameof(serializer));
            if (localPort < 1 || localPort > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(localPort));
            if (remotePort < 1 || remotePort > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(remotePort));
            if (string.IsNullOrWhiteSpace(remoteIp))
                throw new ArgumentException("Remote IP is required.", nameof(remoteIp));
            if (simulatedReceiveDelayMilliseconds < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(simulatedReceiveDelayMilliseconds));

            IPAddress remoteAddress;
            if (!IPAddress.TryParse(remoteIp, out remoteAddress))
                throw new ArgumentException("Remote IP must be a valid IP address.", nameof(remoteIp));

            m_Serializer = serializer;
            m_SimulatedReceiveDelayTicks =
                (long)simulatedReceiveDelayMilliseconds * Stopwatch.Frequency / 1000L;
            m_RemoteEndpoint = new IPEndPoint(remoteAddress, remotePort);
            m_Udp = new UdpClient(localPort);
            m_Udp.Client.Blocking = false;
        }

        public void QueueLocalInput(int playerIndex, int frame, TInput input) {
            ThrowIfDisposed();
            if (playerIndex < 0 || playerIndex > byte.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(playerIndex));
            if (frame < 0)
                throw new ArgumentOutOfRangeException(nameof(frame));

            var payload = m_Serializer.Encode(input);
            if (payload == null || payload.Length > ushort.MaxValue)
                throw new InvalidOperationException("Invalid encoded input.");

            m_History[MakeHistoryKey(playerIndex, frame)] = new InputEntry {
                PlayerIndex = playerIndex,
                Frame = frame,
                Payload = payload,
            };

            while (m_History.Count > ResendHistoryCount) {
                long oldestKey = 0;
                foreach (var key in m_History.Keys) {
                    oldestKey = key;
                    break;
                }

                m_History.Remove(oldestKey);
            }
        }

        public void Pump(Action<int, int, TInput> onRemoteInput) {
            ThrowIfDisposed();
            if (onRemoteInput == null)
                throw new ArgumentNullException(nameof(onRemoteInput));

            ReceiveAll();
            DeliverPendingPackets(onRemoteInput);
            SendRecentInputs();
        }

        public void Dispose() {
            if (m_Disposed)
                return;

            m_Disposed = true;
            m_History.Clear();
            m_PendingPackets.Clear();
            m_Udp.Close();
        }

        private void ReceiveAll() {
            while (m_Udp.Client.Poll(0, SelectMode.SelectRead)) {
                var source = new IPEndPoint(IPAddress.Any, 0);
                byte[] packet;
                try {
                    packet = m_Udp.Receive(ref source);
                }
                catch (SocketException) {
                    break;
                }

                if (!source.Address.Equals(m_RemoteEndpoint.Address) ||
                    source.Port != m_RemoteEndpoint.Port)
                    continue;

                m_PendingPackets.Add(new PendingPacket {
                    Packet = packet,
                    DeliverAtTimestamp = Stopwatch.GetTimestamp() +
                                         m_SimulatedReceiveDelayTicks,
                });
            }
        }

        private void DeliverPendingPackets(Action<int, int, TInput> onRemoteInput) {
            var now = Stopwatch.GetTimestamp();
            for (var i = m_PendingPackets.Count - 1; i >= 0; i--) {
                var pending = m_PendingPackets[i];
                if (pending.DeliverAtTimestamp > now)
                    continue;

                DecodePacket(pending.Packet, onRemoteInput);
                m_PendingPackets.RemoveAt(i);
            }
        }

        private void SendRecentInputs() {
            if (m_History.Count == 0)
                return;

            var packet = new byte[MaxPacketSize];
            packet[0] = (byte)'G';
            packet[1] = (byte)'G';
            packet[2] = (byte)'P';
            packet[3] = (byte)'O';
            packet[4] = ProtocolVersion;

            var offset = HeaderSize;
            var count = 0;
            var historyKeys = new List<long>(m_History.Keys);
            for (var i = historyKeys.Count - 1; i >= 0; i--) {
                var entry = m_History[historyKeys[i]];
                var entrySize = EntryHeaderSize + entry.Payload.Length;
                if (offset + entrySize > MaxPacketSize)
                    continue;

                packet[offset++] = (byte)entry.PlayerIndex;
                WriteInt32(packet, offset, entry.Frame);
                offset += 4;
                WriteUInt16(packet, offset, (ushort)entry.Payload.Length);
                offset += 2;
                Buffer.BlockCopy(entry.Payload, 0, packet, offset, entry.Payload.Length);
                offset += entry.Payload.Length;
                count++;
            }

            if (count == 0)
                return;

            packet[5] = (byte)count;
            var output = new byte[offset];
            Buffer.BlockCopy(packet, 0, output, 0, offset);
            m_Udp.Send(output, output.Length, m_RemoteEndpoint);
        }

        private void DecodePacket(byte[] packet, Action<int, int, TInput> onRemoteInput) {
            if (packet == null || packet.Length < HeaderSize ||
                packet[0] != (byte)'G' || packet[1] != (byte)'G' ||
                packet[2] != (byte)'P' || packet[3] != (byte)'O' ||
                packet[4] != ProtocolVersion)
                return;

            var count = packet[5];
            var offset = HeaderSize;
            for (var i = 0; i < count; i++) {
                if (offset + EntryHeaderSize > packet.Length)
                    return;

                var playerIndex = packet[offset++];
                var frame = ReadInt32(packet, offset);
                offset += 4;
                var inputLength = ReadUInt16(packet, offset);
                offset += 2;
                if (offset + inputLength > packet.Length)
                    return;

                var inputBytes = new byte[inputLength];
                Buffer.BlockCopy(packet, offset, inputBytes, 0, inputLength);
                offset += inputLength;

                TInput input;
                if (frame >= 0 && m_Serializer.TryDecode(inputBytes, out input))
                    onRemoteInput(playerIndex, frame, input);
            }
        }

        private void ThrowIfDisposed() {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(GgpoUdpTransport<TInput>));
        }

        private static long MakeHistoryKey(int playerIndex, int frame) {
            return ((long)frame << 32) | (uint)playerIndex;
        }

        private static void WriteInt32(byte[] buffer, int offset, int value) {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static int ReadInt32(byte[] buffer, int offset) {
            return buffer[offset] |
                   (buffer[offset + 1] << 8) |
                   (buffer[offset + 2] << 16) |
                   (buffer[offset + 3] << 24);
        }

        private static void WriteUInt16(byte[] buffer, int offset, ushort value) {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        private static ushort ReadUInt16(byte[] buffer, int offset) {
            return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
        }

        private sealed class InputEntry {
            public int PlayerIndex;
            public int Frame;
            public byte[] Payload;
        }

        private struct PendingPacket {
            public byte[] Packet;
            public long DeliverAtTimestamp;
        }
    }
}

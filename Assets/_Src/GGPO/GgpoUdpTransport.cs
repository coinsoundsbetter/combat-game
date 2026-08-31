using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace _Src.GGPO {
    /// <summary>
    /// 双实例原型使用的 UDP 输入传输层。
    /// 未确认输入会持续重发；收到逐帧 ACK 后才释放发送历史。
    /// </summary>
    public sealed class GgpoUdpTransport<TInput> :
        IGgpoTransport<TInput>,
        IGgpoChecksumTransport,
        IGgpoTimeSyncTransport,
        IGgpoReliableInputDiagnostics,
        IGgpoConnectionTransport {
        private const byte ProtocolVersion = 4;
        private const int SequenceOffset = 6;
        private const int SenderFrameOffset = 10;
        private const int SenderAdvantageOffset = 14;
        private const int HeaderSize = 18;
        private const int EntryHeaderSize = 7;
        private const int ChecksumPacketSize = 13;
        private const int InputAckHeaderSize = 6;
        private const int InputAckEntrySize = 5;
        private const int HandshakePacketSize = 6;
        private const int MaxPacketSize = 1200;
        private const int MaxPendingInputCount = 4096;
        private const int ChecksumHistoryCount = 4;

        private readonly UdpClient m_Udp;
        private readonly IPEndPoint m_RemoteEndpoint;
        private readonly IGgpoInputSerializer<TInput> m_Serializer;
        private readonly long m_SimulatedReceiveDelayTicks;
        private readonly SortedDictionary<long, InputEntry> m_History =
            new SortedDictionary<long, InputEntry>();
        private readonly SortedDictionary<int, uint> m_ChecksumHistory =
            new SortedDictionary<int, uint>();
        private readonly List<PendingPacket> m_PendingPackets =
            new List<PendingPacket>();
        private uint m_NextSendSequence;
        private uint m_LastReceivedSequence;
        private bool m_HasReceivedSequence;
        private int m_LocalTimeSyncFrame;
        private int m_LocalAdvantageMilliFrames;
        private int m_ReceivedInputAckCount;
        private int m_LocalPlayerIndex = -1;
        private bool m_HandshakeStarted;
        private bool m_RemoteReadyReceived;
        private bool m_LocalReadyAcknowledged;
        private bool m_Disposed;

        public event Action<int, uint> RemoteChecksumReceived;
        public event Action<GgpoTimeSyncSample> TimeSyncSampleReceived;
        public int PendingLocalInputCount => m_History.Count;
        public int ReceivedInputAckCount => m_ReceivedInputAckCount;
        public GgpoConnectionState ConnectionState { get; private set; } =
            GgpoConnectionState.NotStarted;
        public bool IsSynchronized =>
            ConnectionState == GgpoConnectionState.Synchronized;

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

            var historyKey = MakeHistoryKey(playerIndex, frame);
            if (!m_History.ContainsKey(historyKey) &&
                m_History.Count >= MaxPendingInputCount) {
                throw new InvalidOperationException(
                    "Input acknowledgement stalled; pending history limit reached.");
            }

            m_History[historyKey] = new InputEntry {
                PlayerIndex = playerIndex,
                Frame = frame,
                Payload = payload,
            };
        }

        public void BeginSynchronization(int localPlayerIndex) {
            ThrowIfDisposed();
            if (localPlayerIndex < 0 || localPlayerIndex > byte.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(localPlayerIndex));

            m_LocalPlayerIndex = localPlayerIndex;
            m_HandshakeStarted = true;
            m_RemoteReadyReceived = false;
            m_LocalReadyAcknowledged = false;
            ConnectionState = GgpoConnectionState.WaitingForPeer;
        }

        public void Pump(Action<int, int, TInput> onRemoteInput) {
            ThrowIfDisposed();
            if (onRemoteInput == null)
                throw new ArgumentNullException(nameof(onRemoteInput));

            ReceiveAll();
            DeliverPendingPackets(onRemoteInput);
            SendReadyIfNeeded();
            SendPendingInputs();
            SendRecentChecksums();
        }

        public void QueueChecksum(int stateFrame, uint checksum) {
            ThrowIfDisposed();
            if (stateFrame < 0)
                throw new ArgumentOutOfRangeException(nameof(stateFrame));

            m_ChecksumHistory[stateFrame] = checksum;
            while (m_ChecksumHistory.Count > ChecksumHistoryCount) {
                var oldestFrame = 0;
                foreach (var frame in m_ChecksumHistory.Keys) {
                    oldestFrame = frame;
                    break;
                }

                m_ChecksumHistory.Remove(oldestFrame);
            }
        }

        public void SetLocalTimeSyncState(
            int currentFrame,
            float localFrameAdvantage) {
            ThrowIfDisposed();
            m_LocalTimeSyncFrame = currentFrame;
            var clampedAdvantage = Math.Max(
                -30f,
                Math.Min(30f, localFrameAdvantage));
            m_LocalAdvantageMilliFrames =
                (int)Math.Round(clampedAdvantage * 1000f);
        }

        public void Dispose() {
            if (m_Disposed)
                return;

            m_Disposed = true;
            m_History.Clear();
            m_ChecksumHistory.Clear();
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

        private void SendPendingInputs() {
            if (m_History.Count == 0)
                return;

            var packet = new byte[MaxPacketSize];
            packet[0] = (byte)'G';
            packet[1] = (byte)'G';
            packet[2] = (byte)'P';
            packet[3] = (byte)'O';
            packet[4] = ProtocolVersion;
            WriteUInt32(packet, SequenceOffset, ++m_NextSendSequence);
            WriteInt32(packet, SenderFrameOffset, m_LocalTimeSyncFrame);
            WriteInt32(
                packet,
                SenderAdvantageOffset,
                m_LocalAdvantageMilliFrames);

            var offset = HeaderSize;
            var count = 0;
            // SortedDictionary 按 frame/player 排序。优先发送最老的未确认输入，
            // 避免历史较长时新输入持续挤占数据包，令真正缺失的旧帧饿死。
            foreach (var pair in m_History) {
                var entry = pair.Value;
                var entrySize = EntryHeaderSize + entry.Payload.Length;
                if (offset + entrySize > MaxPacketSize)
                    break;

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

        private void SendRecentChecksums() {
            foreach (var pair in m_ChecksumHistory) {
                var packet = new byte[ChecksumPacketSize];
                packet[0] = (byte)'G';
                packet[1] = (byte)'G';
                packet[2] = (byte)'C';
                packet[3] = (byte)'S';
                packet[4] = ProtocolVersion;
                WriteInt32(packet, 5, pair.Key);
                WriteUInt32(packet, 9, pair.Value);
                m_Udp.Send(packet, packet.Length, m_RemoteEndpoint);
            }
        }

        private void DecodePacket(byte[] packet, Action<int, int, TInput> onRemoteInput) {
            if (IsHandshakePacket(packet, (byte)'R', (byte)'D')) {
                ReceiveReady(packet[5]);
                return;
            }

            if (IsHandshakePacket(packet, (byte)'R', (byte)'A')) {
                ReceiveReadyAcknowledgement(packet[5]);
                return;
            }

            if (packet != null && packet.Length >= InputAckHeaderSize &&
                packet[0] == (byte)'G' && packet[1] == (byte)'G' &&
                packet[2] == (byte)'A' && packet[3] == (byte)'K' &&
                packet[4] == ProtocolVersion) {
                var receivedAckCount = packet[5];
                if (packet.Length !=
                    InputAckHeaderSize + receivedAckCount * InputAckEntrySize)
                    return;

                var receivedAckOffset = InputAckHeaderSize;
                for (var i = 0; i < receivedAckCount; i++) {
                    var playerIndex = packet[receivedAckOffset++];
                    var frame = ReadInt32(packet, receivedAckOffset);
                    receivedAckOffset += 4;
                    if (frame >= 0 &&
                        m_History.Remove(MakeHistoryKey(playerIndex, frame))) {
                        m_ReceivedInputAckCount++;
                    }
                }
                return;
            }

            if (packet != null && packet.Length == ChecksumPacketSize &&
                packet[0] == (byte)'G' && packet[1] == (byte)'G' &&
                packet[2] == (byte)'C' && packet[3] == (byte)'S' &&
                packet[4] == ProtocolVersion) {
                RemoteChecksumReceived?.Invoke(
                    ReadInt32(packet, 5),
                    ReadUInt32(packet, 9));
                return;
            }

            if (packet == null || packet.Length < HeaderSize ||
                packet[0] != (byte)'G' || packet[1] != (byte)'G' ||
                packet[2] != (byte)'P' || packet[3] != (byte)'O' ||
                packet[4] != ProtocolVersion)
                return;

            var count = packet[5];
            var sequence = ReadUInt32(packet, SequenceOffset);
            var senderFrame = ReadInt32(packet, SenderFrameOffset);
            var senderAdvantage =
                ReadInt32(packet, SenderAdvantageOffset) / 1000f;
            var offset = HeaderSize;
            var ackPacket = new byte[
                InputAckHeaderSize + count * InputAckEntrySize];
            ackPacket[0] = (byte)'G';
            ackPacket[1] = (byte)'G';
            ackPacket[2] = (byte)'A';
            ackPacket[3] = (byte)'K';
            ackPacket[4] = ProtocolVersion;
            var ackOffset = InputAckHeaderSize;
            var ackCount = 0;
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
                if (frame >= 0 && m_Serializer.TryDecode(inputBytes, out input)) {
                    onRemoteInput(playerIndex, frame, input);
                    // ACK 本身允许丢失。输入仍在对端历史里时会再次到达，
                    // 我们也会再次确认，直到对端确实释放该帧。
                    ackPacket[ackOffset++] = playerIndex;
                    WriteInt32(ackPacket, ackOffset, frame);
                    ackOffset += 4;
                    ackCount++;
                }
            }

            if (ackCount > 0) {
                ackPacket[5] = (byte)ackCount;
                m_Udp.Send(ackPacket, ackOffset, m_RemoteEndpoint);
            }

            // 冗余输入仍可从旧包补齐，但时间同步只接受最新的包，
            // 避免模拟延迟队列的逆序遍历令时间估计倒退。
            if (!m_HasReceivedSequence ||
                IsSequenceNewer(sequence, m_LastReceivedSequence)) {
                m_HasReceivedSequence = true;
                m_LastReceivedSequence = sequence;
                TimeSyncSampleReceived?.Invoke(
                    new GgpoTimeSyncSample(senderFrame, senderAdvantage));
            }
        }

        private void SendReadyIfNeeded() {
            if (!m_HandshakeStarted || IsSynchronized)
                return;

            SendHandshakePacket((byte)'R', (byte)'D', m_LocalPlayerIndex);
        }

        private void ReceiveReady(int remotePlayerIndex) {
            if (!m_HandshakeStarted)
                return;

            if (remotePlayerIndex == m_LocalPlayerIndex) {
                ConnectionState = GgpoConnectionState.PlayerIndexConflict;
                return;
            }

            m_RemoteReadyReceived = true;
            SendHandshakePacket((byte)'R', (byte)'A', remotePlayerIndex);
            UpdateConnectionState();
        }

        private void ReceiveReadyAcknowledgement(int acknowledgedPlayerIndex) {
            if (!m_HandshakeStarted || acknowledgedPlayerIndex != m_LocalPlayerIndex)
                return;

            m_LocalReadyAcknowledged = true;
            UpdateConnectionState();
        }

        private void UpdateConnectionState() {
            if (ConnectionState == GgpoConnectionState.PlayerIndexConflict)
                return;

            ConnectionState = m_RemoteReadyReceived && m_LocalReadyAcknowledged
                ? GgpoConnectionState.Synchronized
                : GgpoConnectionState.WaitingForPeer;
        }

        private void SendHandshakePacket(byte type0, byte type1, int playerIndex) {
            var packet = new byte[HandshakePacketSize];
            packet[0] = (byte)'G';
            packet[1] = (byte)'G';
            packet[2] = type0;
            packet[3] = type1;
            packet[4] = ProtocolVersion;
            packet[5] = (byte)playerIndex;
            m_Udp.Send(packet, packet.Length, m_RemoteEndpoint);
        }

        private static bool IsHandshakePacket(byte[] packet, byte type0, byte type1) {
            return packet != null && packet.Length == HandshakePacketSize &&
                   packet[0] == (byte)'G' && packet[1] == (byte)'G' &&
                   packet[2] == type0 && packet[3] == type1 &&
                   packet[4] == ProtocolVersion;
        }

        private void ThrowIfDisposed() {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(GgpoUdpTransport<TInput>));
        }

        private static long MakeHistoryKey(int playerIndex, int frame) {
            return ((long)frame << 32) | (uint)playerIndex;
        }

        private static bool IsSequenceNewer(uint candidate, uint current) {
            return unchecked((int)(candidate - current)) > 0;
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

        private static void WriteUInt32(byte[] buffer, int offset, uint value) {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static uint ReadUInt32(byte[] buffer, int offset) {
            return (uint)(buffer[offset] |
                          (buffer[offset + 1] << 8) |
                          (buffer[offset + 2] << 16) |
                          (buffer[offset + 3] << 24));
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

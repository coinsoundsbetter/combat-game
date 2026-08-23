using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using _Src.Serialization;

namespace _Src.GGPO
{
    public sealed class GgpoTransport<TInput> : IGgpoTransport<TInput>
    {
        private const byte Version = 2;
        private const int HeaderSize = 6;      // GGPO + version + count
        private const int EntryHeaderSize = 7; // player index + frame + payload length
        private const int MaxPacketSize = 1200;
        private const int ResendHistoryCount = 32;

        private readonly UdpClient m_Udp;
        private readonly IPEndPoint m_Remote;
        private readonly IGgpoInputSerializer<TInput> m_Codec;
        private readonly SortedDictionary<long, InputEntry> m_History =
            new SortedDictionary<long, InputEntry>();

        private bool m_Disposed;

        public GgpoTransport(
            int localPort,
            string remoteIp,
            int remotePort,
            IGgpoInputSerializer<TInput> codec)
        {
            if (codec == null)
                throw new ArgumentNullException(nameof(codec));

            m_Codec = codec;
            m_Remote = new IPEndPoint(IPAddress.Parse(remoteIp), remotePort);

            m_Udp = new UdpClient(localPort);
            m_Udp.Client.Blocking = false;
        }

        public void QueueLocalInput(int playerIndex, int frame, TInput input)
        {
            ThrowIfDisposed();

            if (playerIndex < 0 || playerIndex > byte.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(playerIndex));
            if (frame < 0)
                throw new ArgumentOutOfRangeException(nameof(frame));

            byte[] bytes = m_Codec.Encode(input);
            if (bytes == null || bytes.Length > ushort.MaxValue)
                throw new InvalidOperationException("Invalid encoded input.");

            m_History[MakeHistoryKey(playerIndex, frame)] = new InputEntry
            {
                PlayerIndex = playerIndex,
                Frame = frame,
                Payload = bytes
            };

            while (m_History.Count > ResendHistoryCount)
            {
                long oldestKey = long.MaxValue;
                foreach (long historyKey in m_History.Keys)
                {
                    oldestKey = historyKey;
                    break;
                }

                m_History.Remove(oldestKey);
            }
        }
        
        public void Pump(Action<int, int, TInput> onRemoteInput)
        {
            ThrowIfDisposed();

            if (onRemoteInput == null)
                throw new ArgumentNullException(nameof(onRemoteInput));

            ReceiveAll(onRemoteInput);
            SendRecentInputs();
        }

        private void ReceiveAll(Action<int, int, TInput> onRemoteInput)
        {
            while (m_Udp.Client.Poll(0, SelectMode.SelectRead))
            {
                IPEndPoint source = new IPEndPoint(IPAddress.Any, 0);
                byte[] packet;

                try
                {
                    packet = m_Udp.Receive(ref source);
                }
                catch (SocketException)
                {
                    break;
                }

                if (!source.Address.Equals(m_Remote.Address) ||
                    source.Port != m_Remote.Port)
                {
                    continue;
                }

                DecodePacket(packet, onRemoteInput);
            }
        }

        private void SendRecentInputs()
        {
            if (m_History.Count == 0)
                return;

            byte[] packet = new byte[MaxPacketSize];
            packet[0] = (byte)'G';
            packet[1] = (byte)'G';
            packet[2] = (byte)'P';
            packet[3] = (byte)'O';
            packet[4] = Version;

            int offset = HeaderSize;
            int count = 0;

            var historyKeys = new List<long>(m_History.Keys);

            // 新帧优先，同时携带旧帧用于 UDP 丢包恢复。
            for (int i = historyKeys.Count - 1; i >= 0; i--)
            {
                InputEntry entry = m_History[historyKeys[i]];
                byte[] input = entry.Payload;
                int entrySize = EntryHeaderSize + input.Length;

                if (offset + entrySize > MaxPacketSize)
                    continue;

                packet[offset] = (byte)entry.PlayerIndex;
                offset++;

                DeterministicBinary.WriteInt32(packet, offset, entry.Frame);
                offset += 4;

                DeterministicBinary.WriteUInt16(packet, offset, (ushort)input.Length);
                offset += 2;

                Buffer.BlockCopy(input, 0, packet, offset, input.Length);
                offset += input.Length;

                count++;
            }

            if (count == 0)
                return;

            packet[5] = (byte)count;

            byte[] output = new byte[offset];
            Buffer.BlockCopy(packet, 0, output, 0, offset);

            m_Udp.Send(output, output.Length, m_Remote);
        }

        private void DecodePacket(byte[] packet, Action<int, int, TInput> onRemoteInput)
        {
            if (packet == null || packet.Length < HeaderSize)
                return;

            if (packet[0] != (byte)'G' ||
                packet[1] != (byte)'G' ||
                packet[2] != (byte)'P' ||
                packet[3] != (byte)'O' ||
                packet[4] != Version)
            {
                return;
            }

            int count = packet[5];
            int offset = HeaderSize;

            for (int i = 0; i < count; i++)
            {
                if (offset + EntryHeaderSize > packet.Length)
                    return;

                int playerIndex = packet[offset];
                offset++;

                int frame = DeterministicBinary.ReadInt32(packet, offset);
                offset += 4;

                int inputLength = DeterministicBinary.ReadUInt16(packet, offset);
                offset += 2;

                if (inputLength < 0 || offset + inputLength > packet.Length)
                    return;

                byte[] inputBytes = new byte[inputLength];
                Buffer.BlockCopy(packet, offset, inputBytes, 0, inputLength);
                offset += inputLength;

                TInput input;
                if (frame >= 0 && m_Codec.TryDecode(inputBytes, out input))
                    onRemoteInput(playerIndex, frame, input);
            }
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;

            m_Disposed = true;
            m_History.Clear();
            m_Udp.Close();
        }

        private void ThrowIfDisposed()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(GgpoTransport<TInput>));
        }

        private static long MakeHistoryKey(int playerIndex, int frame)
        {
            return ((long)frame << 32) | (uint)playerIndex;
        }

        private sealed class InputEntry
        {
            public int PlayerIndex;
            public int Frame;
            public byte[] Payload;
        }
    }
}

using System;
using System.Net;
using System.Net.Sockets;

namespace FightGame
{
    // 回滚网游用高频小包传"输入"，而不是状态同步。所以用裸 UDP 而非 Mirror/Netcode。
    // 同机两 Unity：Host 绑定固定端口，Client 用临时端口发到 Host，Host 从收包学到 Client 端点再回发。
    public enum MsgType : byte { Hello = 0, Welcome = 1, Ready = 2, Start = 3, Input = 4 }

    public class NetworkTransport
    {
        UdpClient udp;
        IPEndPoint remoteEP;

        public bool IsHost { get; private set; }
        public int MyPlayerId { get; private set; }
        public uint Seed { get; private set; }
        public bool Connected;
        public bool LocalReady, RemoteReady;
        public bool Started;

        const int QSIZE = 2048;
        readonly int[] qFrame = new int[QSIZE];
        readonly InputState[] qInput = new InputState[QSIZE];
        int qHead, qTail;

        public void StartHost(int port)
        {
            IsHost = true; MyPlayerId = 0;
            Seed = (uint)(DateTime.Now.Ticks & 0xFFFFFFFF);
            udp = new UdpClient(port);
            udp.Client.Blocking = false;
        }

        public void StartClient(string ip, int port)
        {
            IsHost = false; MyPlayerId = -1;
            udp = new UdpClient();
            udp.Client.Blocking = false;
            remoteEP = new IPEndPoint(IPAddress.Parse(ip), port);
            ResendHello();
        }

        public void ResendHello() => SendRaw(new byte[] { (byte)MsgType.Hello });

        public void SendReady()
        {
            LocalReady = true;
            SendRaw(new byte[] { (byte)MsgType.Ready, (byte)Math.Max(0, MyPlayerId) });
        }

        public void SendStart()
        {
            byte[] p = new byte[5];
            p[0] = (byte)MsgType.Start;
            Buffer.BlockCopy(BitConverter.GetBytes(Seed), 0, p, 1, 4);
            SendRaw(p);
        }

        public void SendInput(int frame, InputState input)
        {
            byte[] msg = new byte[6];
            msg[0] = (byte)MsgType.Input;
            Buffer.BlockCopy(BitConverter.GetBytes(frame), 0, msg, 1, 4);
            msg[5] = input.bits;
            SendRaw(msg);
        }

        void SendRaw(byte[] msg)
        {
            if (remoteEP == null || udp == null) return;
            try { udp.Send(msg, msg.Length, remoteEP); } catch { }
        }

        public bool PollInput(out int frame, out InputState input)
        {
            if (qHead == qTail) { frame = 0; input = default; return false; }
            frame = qFrame[qTail]; input = qInput[qTail];
            qTail = (qTail + 1) % QSIZE;
            return true;
        }

        // 非阻塞轮询。在 FixedUpdate 里调用，避免线程带来的确定性隐患。
        public void Poll()
        {
            if (udp == null) return;
            IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
            while (true)
            {
                byte[] data;
                try { data = udp.Receive(ref from); }
                catch (SocketException) { break; } // 无更多包
                if (data == null || data.Length < 1) break;
                Handle(data, from);
            }
        }

        void Handle(byte[] data, IPEndPoint from)
        {
            switch ((MsgType)data[0])
            {
                case MsgType.Hello:
                    remoteEP = from;       // Host 从首个包学到 Client 端点
                    SendWelcome();
                    break;
                case MsgType.Welcome:
                    MyPlayerId = data[1];
                    Seed = BitConverter.ToUInt32(data, 2);
                    Connected = true;
                    break;
                case MsgType.Ready:
                    RemoteReady = true;
                    Connected = true;
                    break;
                case MsgType.Start:
                    if (data.Length >= 5) Seed = BitConverter.ToUInt32(data, 1);
                    Started = true;
                    break;
                case MsgType.Input:
                    int frame = BitConverter.ToInt32(data, 1);
                    EnqueueInput(frame, new InputState(data[5]));
                    break;
            }
        }

        void SendWelcome()
        {
            byte[] p = new byte[6];
            p[0] = (byte)MsgType.Welcome;
            p[1] = 1; // 告诉 Client 它是 1 号玩家
            Buffer.BlockCopy(BitConverter.GetBytes(Seed), 0, p, 2, 4);
            SendRaw(p);
            Connected = true;
        }

        void EnqueueInput(int frame, InputState input)
        {
            qFrame[qHead] = frame; qInput[qHead] = input;
            qHead = (qHead + 1) % QSIZE;
        }
    }
}

# Unity 双人格斗游戏：GGPO 风格分层与完整参考代码

> 用途：架构参考与手动重写。本文代码复用项目中的命名空间 `_Src.Game`、`_Src.GGPO` 和 `_Src.Serialization`。
>
> 重要：这是一个便于学习和继续开发的“GGPO 风格回滚核心”，不是官方 C++ GGPO SDK 的完整移植。输入预测、状态快照和回滚重演位于 Session；握手、ACK、TimeSync、断线检测、观战等属于后续网络协议层。

快速定位：`GgpoSession<TInput>` 的完整实现位于“14. GGPO 核心类型与 Session 实现”；它采用构造时固定 `playerCount`、一次性分配玩家槽位和同步输入数组的设计。

## 1. 最终架构

```text
Unity Input / UI / View
          ↓
       GameMain
          ↓
   MatchFactory ───── 创建具体 Transport
          ↓
    MatchRuntime
          ↓
     GgpoSession ───── IGgpoTransport
          ↓ callbacks
RollbackGameAdapter
      ↓           ↓
FighterSimulation GameStateCodec
```

---

## 2. 基础对局模式

### PlayMode.cs

```csharp
namespace _Src.Game
{
    public enum PlayMode
    {
        Local,
        Remote
    }
}
```

## 3. 原型传输实现

### GgpoLocalTransport.cs

```csharp
using System;

namespace _Src.GGPO
{
    public sealed class GgpoLocalTransport<TInput> : IGgpoTransport<TInput>
    {
        private bool m_Disposed;

        public void QueueLocalInput(int playerIndex, int frame, TInput input)
        {
            ThrowIfDisposed();
        }

        public void Pump(Action<int, int, TInput> onRemoteInput)
        {
            ThrowIfDisposed();
            if (onRemoteInput == null)
                throw new ArgumentNullException(nameof(onRemoteInput));
        }

        public void Dispose()
        {
            m_Disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(GgpoLocalTransport<TInput>));
        }
    }
}
```

### GgpoTransport.cs

这是用于原型的 UDP 输入传输，不等于官方 GGPO 的完整网络协议。

```csharp
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
        private const int HeaderSize = 6;
        private const int EntryHeaderSize = 7;
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
            if (codec == null) throw new ArgumentNullException(nameof(codec));
            if (localPort < 1 || localPort > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(localPort));
            if (remotePort < 1 || remotePort > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(remotePort));
            if (string.IsNullOrWhiteSpace(remoteIp))
                throw new ArgumentException("Remote IP is required.", nameof(remoteIp));

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
            if (frame < 0) throw new ArgumentOutOfRangeException(nameof(frame));

            byte[] payload = m_Codec.Encode(input);
            if (payload == null || payload.Length > ushort.MaxValue)
                throw new InvalidOperationException("Invalid encoded input.");

            m_History[MakeHistoryKey(playerIndex, frame)] = new InputEntry
            {
                PlayerIndex = playerIndex,
                Frame = frame,
                Payload = payload
            };

            while (m_History.Count > ResendHistoryCount)
            {
                long oldestKey = long.MaxValue;
                foreach (long key in m_History.Keys)
                {
                    oldestKey = key;
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

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            m_History.Clear();
            m_Udp.Close();
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
                    continue;

                DecodePacket(packet, onRemoteInput);
            }
        }

        private void SendRecentInputs()
        {
            if (m_History.Count == 0) return;

            byte[] packet = new byte[MaxPacketSize];
            packet[0] = (byte)'G';
            packet[1] = (byte)'G';
            packet[2] = (byte)'P';
            packet[3] = (byte)'O';
            packet[4] = Version;

            int offset = HeaderSize;
            int count = 0;
            var historyKeys = new List<long>(m_History.Keys);

            // 新输入优先，同时携带旧输入以抵抗少量 UDP 丢包。
            for (int i = historyKeys.Count - 1; i >= 0; i--)
            {
                InputEntry entry = m_History[historyKeys[i]];
                int entrySize = EntryHeaderSize + entry.Payload.Length;
                if (offset + entrySize > MaxPacketSize) continue;

                packet[offset++] = (byte)entry.PlayerIndex;
                DeterministicBinary.WriteInt32(packet, offset, entry.Frame);
                offset += 4;
                DeterministicBinary.WriteUInt16(
                    packet, offset, (ushort)entry.Payload.Length);
                offset += 2;
                Buffer.BlockCopy(
                    entry.Payload, 0, packet, offset, entry.Payload.Length);
                offset += entry.Payload.Length;
                count++;
            }

            if (count == 0) return;
            packet[5] = (byte)count;

            byte[] output = new byte[offset];
            Buffer.BlockCopy(packet, 0, output, 0, offset);
            m_Udp.Send(output, output.Length, m_Remote);
        }

        private void DecodePacket(
            byte[] packet, Action<int, int, TInput> onRemoteInput)
        {
            if (packet == null || packet.Length < HeaderSize) return;
            if (packet[0] != (byte)'G' ||
                packet[1] != (byte)'G' ||
                packet[2] != (byte)'P' ||
                packet[3] != (byte)'O' ||
                packet[4] != Version)
                return;

            int count = packet[5];
            int offset = HeaderSize;
            for (int i = 0; i < count; i++)
            {
                if (offset + EntryHeaderSize > packet.Length) return;

                int playerIndex = packet[offset++];
                int frame = DeterministicBinary.ReadInt32(packet, offset);
                offset += 4;
                int inputLength = DeterministicBinary.ReadUInt16(packet, offset);
                offset += 2;
                if (offset + inputLength > packet.Length) return;

                byte[] inputBytes = new byte[inputLength];
                Buffer.BlockCopy(packet, offset, inputBytes, 0, inputLength);
                offset += inputLength;

                TInput input;
                if (frame >= 0 && m_Codec.TryDecode(inputBytes, out input))
                    onRemoteInput(playerIndex, frame, input);
            }
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
```

---

## 4. 确定性二进制工具

### DeterministicBinary.cs

```csharp
using System;

namespace _Src.Serialization
{
    public static class DeterministicBinary
    {
        public static void WriteInt32(byte[] buffer, int offset, int value)
        {
            ValidateRange(buffer, offset, 4);
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        public static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            ValidateRange(buffer, offset, 4);
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        public static void WriteUInt16(byte[] buffer, int offset, ushort value)
        {
            ValidateRange(buffer, offset, 2);
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        public static int ReadInt32(byte[] buffer, int offset)
        {
            ValidateRange(buffer, offset, 4);
            return buffer[offset] |
                   (buffer[offset + 1] << 8) |
                   (buffer[offset + 2] << 16) |
                   (buffer[offset + 3] << 24);
        }

        public static uint ReadUInt32(byte[] buffer, int offset)
        {
            ValidateRange(buffer, offset, 4);
            return (uint)(buffer[offset] |
                          (buffer[offset + 1] << 8) |
                          (buffer[offset + 2] << 16) |
                          (buffer[offset + 3] << 24));
        }

        public static ushort ReadUInt16(byte[] buffer, int offset)
        {
            ValidateRange(buffer, offset, 2);
            return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
        }

        // FNV-1a 只用于确定性诊断，不用于加密或安全认证。
        public static ulong CalculateChecksum(byte[] buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            uint hash = 2166136261;
            foreach (byte value in buffer)
            {
                hash ^= value;
                hash *= 16777619;
            }
            return hash;
        }

        private static void ValidateRange(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset > buffer.Length - count)
                throw new ArgumentOutOfRangeException(nameof(offset));
        }
    }
}
```

---

## 5. 关键运行流程

### Session 创建

```text
new GgpoSession(playerCount: 2)
    ├─ 一次性分配 PlayerQueue[2]
    ├─ 一次性分配 SynchronizedInput[2]
    ├─ AddPlayer(P1) → 固定 slot 0
    ├─ AddPlayer(P2) → 固定 slot 1
    └─ 第一次 Idle/AddInput/Sync
         ├─ 检查 registered == playerCount
         └─ 永久锁定玩家列表
```

### 正常帧

```text
MatchRuntime.Update
  ├─ Session.Idle
  ├─ SamplePlayerInput
  ├─ Session.AddLocalInput
  ├─ Session.TrySynchronizeInputs
  └─ Session.AdvanceFrame
       ├─ 保存当前帧快照
       ├─ RollbackGameAdapter.AdvanceFrame
       │    └─ FighterSimulation.Step
       └─ 保存下一帧快照
```

### 回滚帧

```text
Session.Idle
  ├─ Transport 收到真实远端输入
  ├─ 对比 UsedInputs，发现预测错误
  ├─ 找到最早错误帧
  ├─ LoadGameState(错误帧快照)
  └─ 循环重演到原当前帧
       ├─ 获取该历史帧输入
       ├─ FighterSimulation.Step
       └─ 重新保存快照
```

## 6. 必须继续补充的生产能力

本文 UDP Transport 只适合原型。正式网络对战至少还需要：

- 握手、Session ID、玩家身份和协议版本协商。
- Packet Sequence、ACK/ACK Bitfield 和明确的输入确认。
- RTT、抖动、丢包率和发送队列统计。
- TimeSync：一端领先过多时主动减速，而不是无限追帧。
- 连接中断、恢复、超时和退出协议。
- Checksum 跨 Peer 交换与 Desync 日志。
- 恶意/错误包校验、输入帧范围限制和速率限制。
- 录像、观战和重连策略。

## 7. 重写时最值得保留的边界

```text
FighterSimulation
    不引用 Unity / GGPO / Socket

RollbackGameAdapter
    只把 Save / Load / Advance 映射到 Simulation 与 Codec

GgpoSession
    不理解 HP、招式、动画或 GameObject

Transport
    只传递 playerIndex + frame + input

Presenter
    只读取 State，不能把 Transform/Animator 状态写回模拟
```

不要在 `FighterSimulation.Step` 内播放声音、生成粒子或调用 Animator，因为回滚重演会让同一帧执行多次。应由模拟产生确定性事件，再由表现层按 `frame + eventId` 去重播放。


## 8. Match 配置、装配和运行

### ConnectInfo.cs

```csharp
namespace _Src.Game
{
    public struct ConnectInfo
    {
        public int LocalPort;
        public string TargetAddress;
        public int TargetPort;

        // 网络对局中，本机控制固定槽位 0 或 1。
        public int LocalPlayerIndex;
    }
}
```

### MatchConfig.cs

```csharp
namespace _Src.Game
{
    public sealed class MatchConfig
    {
        public const int PlayerCount = 2;

        public PlayMode Mode;
        public ConnectInfo Connection;
        public int InputDelayFrames;
        public int MaxRollbackFrames;
        public uint RandomSeed;
        public FighterRules Rules;

        public static MatchConfig CreateLocal()
        {
            return new MatchConfig
            {
                Mode = PlayMode.Local,
                InputDelayFrames = 0,
                MaxRollbackFrames = 8,
                RandomSeed = 1,
                Rules = FighterRules.CreateDefault()
            };
        }

        public static MatchConfig CreateNetwork(ConnectInfo connection)
        {
            return new MatchConfig
            {
                Mode = PlayMode.Remote,
                Connection = connection,
                InputDelayFrames = 2,
                MaxRollbackFrames = 8,
                RandomSeed = 1,
                Rules = FighterRules.CreateDefault()
            };
        }
    }
}
```

### MatchRuntime.cs

```csharp
using System;
using _Src.GGPO;

namespace _Src.Game
{
    public sealed class MatchRuntime : IDisposable
    {
        public const float TickDuration = 1f / 60f;
        private const int MaxTicksPerUpdate = 8;

        private readonly GgpoSession<PlayerInput> m_Session;
        private readonly FighterSimulation m_Simulation;
        private readonly IPlayerInputSource m_InputSource;
        private readonly GgpoPlayerType[] m_PlayerTypes;
        private readonly bool[] m_HasQueuedInput;
        private readonly PlayerInput[] m_SynchronizedInputs;

        private float m_Accumulator;
        private bool m_Disposed;

        public int CurrentFrame { get { return m_Session.CurrentFrame; } }
        public GameState State { get { return m_Simulation.State; } }
        public bool IsRollingBack { get { return m_Session.IsRollingBack; } }

        public MatchRuntime(
            GgpoSession<PlayerInput> session,
            FighterSimulation simulation,
            IPlayerInputSource inputSource,
            GgpoPlayerType[] playerTypes)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (simulation == null) throw new ArgumentNullException(nameof(simulation));
            if (inputSource == null) throw new ArgumentNullException(nameof(inputSource));
            if (playerTypes == null || playerTypes.Length != session.PlayerCount)
                throw new ArgumentException(
                    "Player type count must equal the Session player count.",
                    nameof(playerTypes));

            m_Session = session;
            m_Simulation = simulation;
            m_InputSource = inputSource;
            m_PlayerTypes = (GgpoPlayerType[])playerTypes.Clone();
            m_HasQueuedInput = new bool[playerTypes.Length];
            m_SynchronizedInputs = new PlayerInput[playerTypes.Length];
        }

        public void Update(float unscaledDeltaTime)
        {
            ThrowIfDisposed();
            if (unscaledDeltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(unscaledDeltaTime));

            // Pump 可能加载旧快照并重演多个逻辑帧。
            m_Session.Idle(0);
            m_Accumulator += unscaledDeltaTime;

            int tickCount = 0;
            while (m_Accumulator >= TickDuration && tickCount < MaxTicksPerUpdate)
            {
                if (!TryAdvanceOneTick()) break;
                m_Accumulator -= TickDuration;
                tickCount++;
            }

            if (tickCount == MaxTicksPerUpdate && m_Accumulator > TickDuration)
                m_Accumulator = TickDuration;
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            m_Session.Close();
        }

        private bool TryAdvanceOneTick()
        {
            QueueMissingLocalInputs();

            if (!m_Session.TrySynchronizeInputs(m_SynchronizedInputs))
                return false;

            m_Session.AdvanceFrame();
            Array.Clear(m_HasQueuedInput, 0, m_HasQueuedInput.Length);
            return true;
        }

        private void QueueMissingLocalInputs()
        {
            for (int playerIndex = 0;
                 playerIndex < m_PlayerTypes.Length;
                 playerIndex++)
            {
                if (m_PlayerTypes[playerIndex] != GgpoPlayerType.Local ||
                    m_HasQueuedInput[playerIndex])
                    continue;

                PlayerInput input = m_InputSource.SamplePlayerInput(playerIndex);
                m_Session.AddLocalInput(playerIndex, input);
                m_HasQueuedInput[playerIndex] = true;
            }
        }

        private void ThrowIfDisposed()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(MatchRuntime));
        }
    }
}
```

### MatchFactory.cs

`playerCount` 在创建 Session 时固定。`AddPlayer` 只填写已经预留的槽位，不扩容 Session。

```csharp
using System;
using _Src.GGPO;

namespace _Src.Game
{
    public static class MatchFactory
    {
        public static MatchRuntime Create(
            MatchConfig config,
            IPlayerInputSource inputSource)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (inputSource == null) throw new ArgumentNullException(nameof(inputSource));
            Validate(config);

            FighterSimulation simulation = new FighterSimulation(config.Rules);
            GameStateCodec codec = new GameStateCodec();
            RollbackGameAdapter adapter = new RollbackGameAdapter(
                simulation, codec, config.RandomSeed);
            IGgpoTransport<PlayerInput> transport = CreateTransport(config);

            GgpoSession<PlayerInput> session = null;
            try
            {
                session = new GgpoSession<PlayerInput>(
                    adapter.CreateCallbacks(),
                    transport,
                    MatchConfig.PlayerCount,
                    config.MaxRollbackFrames);

                GgpoPlayerType[] playerTypes = CreatePlayerTypes(config);
                for (int playerIndex = 0;
                     playerIndex < MatchConfig.PlayerCount;
                     playerIndex++)
                {
                    int assignedIndex = session.AddPlayer(
                        playerTypes[playerIndex],
                        config.InputDelayFrames);
                    if (assignedIndex != playerIndex)
                        throw new InvalidOperationException(
                            "Unexpected fixed player-slot assignment.");
                }

                return new MatchRuntime(
                    session, simulation, inputSource, playerTypes);
            }
            catch
            {
                if (session != null) session.Close();
                else transport.Dispose();
                throw;
            }
        }

        private static IGgpoTransport<PlayerInput> CreateTransport(MatchConfig config)
        {
            if (config.Mode == PlayMode.Local)
                return new GgpoLocalTransport<PlayerInput>();

            return new GgpoTransport<PlayerInput>(
                config.Connection.LocalPort,
                config.Connection.TargetAddress,
                config.Connection.TargetPort,
                new PlayerInputSerializer());
        }

        private static GgpoPlayerType[] CreatePlayerTypes(MatchConfig config)
        {
            GgpoPlayerType[] result =
                new GgpoPlayerType[MatchConfig.PlayerCount];

            for (int playerIndex = 0; playerIndex < result.Length; playerIndex++)
            {
                bool isLocal = config.Mode == PlayMode.Local ||
                               config.Connection.LocalPlayerIndex == playerIndex;
                result[playerIndex] = isLocal
                    ? GgpoPlayerType.Local
                    : GgpoPlayerType.Remote;
            }
            return result;
        }

        private static void Validate(MatchConfig config)
        {
            if (config.InputDelayFrames < 0)
                throw new ArgumentOutOfRangeException(nameof(config.InputDelayFrames));
            if (config.MaxRollbackFrames <= 0)
                throw new ArgumentOutOfRangeException(nameof(config.MaxRollbackFrames));
            if (config.Mode != PlayMode.Remote) return;

            if (config.Connection.LocalPlayerIndex < 0 ||
                config.Connection.LocalPlayerIndex >= MatchConfig.PlayerCount)
                throw new ArgumentOutOfRangeException(
                    nameof(config.Connection.LocalPlayerIndex));
            if (config.Connection.LocalPort < 1 ||
                config.Connection.LocalPort > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(config.Connection.LocalPort));
            if (config.Connection.TargetPort < 1 ||
                config.Connection.TargetPort > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(config.Connection.TargetPort));
            if (string.IsNullOrWhiteSpace(config.Connection.TargetAddress))
                throw new ArgumentException(
                    "Target address is required.",
                    nameof(config.Connection.TargetAddress));
        }
    }
}
```

---

## 9. Unity 表现和入口

### FighterPresenter.cs

```csharp
using UnityEngine;

namespace _Src.Game
{
    public sealed class FighterPresenter : MonoBehaviour
    {
        private const float LogicUnitsPerUnityUnit = 1000f;

        [SerializeField] private Transform m_PlayerOne;
        [SerializeField] private Transform m_PlayerTwo;

        public void Present(GameState state)
        {
            PresentFighter(m_PlayerOne, state.P1);
            PresentFighter(m_PlayerTwo, state.P2);
        }

        private static void PresentFighter(Transform target, FighterState state)
        {
            if (target == null) return;

            Vector3 position = target.position;
            position.x = state.PositionX / LogicUnitsPerUnityUnit;
            target.position = position;

            Vector3 scale = target.localScale;
            float absoluteScaleX = Mathf.Abs(scale.x);
            scale.x = state.Facing >= 0 ? absoluteScaleX : -absoluteScaleX;
            target.localScale = scale;
        }
    }
}
```

### GameMain.cs

```csharp
using System;
using UnityEngine;

namespace _Src.Game
{
    public sealed class GameMain : MonoBehaviour
    {
        [SerializeField] private FighterPresenter m_Presenter;

        private IPlayerInputSource m_InputSource;
        private MatchRuntime m_Runtime;

        public bool HasSession { get { return m_Runtime != null; } }
        public int CurrentFrame { get { return m_Runtime != null ? m_Runtime.CurrentFrame : 0; } }
        public int Player1Hp { get { return m_Runtime != null ? m_Runtime.State.P1.Hp : 0; } }
        public int Player2Hp { get { return m_Runtime != null ? m_Runtime.State.P2.Hp : 0; } }
        public int Winner { get { return m_Runtime != null ? m_Runtime.State.Winner : -1; } }

        private void Awake()
        {
            m_InputSource = new UnityPlayerInputSource();
        }

        private void Update()
        {
            if (m_Runtime == null) return;
            m_Runtime.Update(Time.unscaledDeltaTime);
            if (m_Presenter != null) m_Presenter.Present(m_Runtime.State);
        }

        private void OnDestroy()
        {
            StopMatch();
        }

        public void InitSession(PlayMode playMode, ConnectInfo connectInfo)
        {
            MatchConfig config = playMode == PlayMode.Local
                ? MatchConfig.CreateLocal()
                : MatchConfig.CreateNetwork(connectInfo);
            StartMatch(config);
        }

        public void StartLocalMatch()
        {
            StartMatch(MatchConfig.CreateLocal());
        }

        public void StartNetworkMatch(ConnectInfo connectInfo)
        {
            StartMatch(MatchConfig.CreateNetwork(connectInfo));
        }

        public void StopMatch()
        {
            if (m_Runtime == null) return;
            m_Runtime.Dispose();
            m_Runtime = null;
        }

        private void StartMatch(MatchConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            StopMatch();
            m_Runtime = MatchFactory.Create(config, m_InputSource);
            if (m_Presenter != null) m_Presenter.Present(m_Runtime.State);
        }
    }
}
```


职责：

- `FighterSimulation`：唯一权威战斗状态和确定性逻辑。
- `GameStateCodec`：完整状态的保存、恢复和校验。
- `GgpoSession`：输入队列、预测、快照、回滚与重演。
- `IGgpoTransport`：输入数据的收发，不理解战斗状态。
- `RollbackGameAdapter`：Session callback 与模拟/Codec 之间的胶水。
- `MatchRuntime`：每帧提交输入并驱动 Session。
- `MatchFactory`：决定本地/网络实现并装配对象。
- `FighterPresenter`：读取状态并更新 Unity 表现，不反向修改模拟。

## 10. 建议目录

```text
_Src/
├─ Game/
│  ├─ Simulation/
│  │  ├─ PlayerInput.cs
│  │  ├─ FighterState.cs
│  │  ├─ GameState.cs
│  │  ├─ FighterRules.cs
│  │  └─ FighterSimulation.cs
│  ├─ RollbackIntegration/
│  │  ├─ GameStateCodec.cs
│  │  └─ RollbackGameAdapter.cs
│  ├─ Input/
│  │  ├─ IPlayerInputSource.cs
│  │  └─ UnityPlayerInputSource.cs
│  ├─ Match/
│  │  ├─ PlayMode.cs
│  │  ├─ ConnectInfo.cs
│  │  ├─ MatchConfig.cs
│  │  ├─ MatchFactory.cs
│  │  └─ MatchRuntime.cs
│  ├─ Network/
│  │  └─ PlayerInputSerializer.cs
│  ├─ Presentation/
│  │  └─ FighterPresenter.cs
│  └─ GameMain.cs
├─ GGPO/
│  ├─ GgpoPlayer.cs
│  ├─ GgpoSavedState.cs
│  ├─ GgpoCallbacks.cs
│  ├─ IGgpoInputSerializer.cs
│  ├─ IGgpoTransport.cs
│  ├─ GgpoLocalTransport.cs
│  ├─ GgpoTransport.cs
│  └─ GgpoSession.cs
└─ Serialization/
   └─ DeterministicBinary.cs
```

建议让 Simulation 和 GGPO Assembly 不引用 UnityEngine；Unity API 只出现在 Input、Presentation 和入口层。

---

## 11. 战斗模拟层

### PlayerInput.cs

```csharp
using System;

namespace _Src.Game
{
    [Flags]
    public enum PlayerButtons : byte
    {
        None = 0,
        Attack = 1 << 0,
        Jump = 1 << 1,
        Block = 1 << 2
    }

    public struct PlayerInput : IEquatable<PlayerInput>
    {
        public sbyte MoveX;
        public byte Buttons;

        public bool IsHeld(PlayerButtons button)
        {
            return (Buttons & (byte)button) != 0;
        }

        public bool Equals(PlayerInput other)
        {
            return MoveX == other.MoveX && Buttons == other.Buttons;
        }

        public override bool Equals(object obj)
        {
            return obj is PlayerInput && Equals((PlayerInput)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (MoveX * 397) ^ Buttons;
            }
        }
    }
}
```

### FighterState.cs

```csharp
namespace _Src.Game
{
    public enum FighterAction
    {
        Idle = 0,
        Walking = 1,
        Attack = 2,
        Hitstun = 3,
        KnockedOut = 4
    }

    public struct FighterState
    {
        // 逻辑整数坐标，例如 1000 单位等于 Unity 中 1 米。
        public int PositionX;
        public int VelocityX;

        public int Hp;
        public int Facing;

        public FighterAction Action;
        public int ActionFrame;

        public int HitstunFrames;
        public int AttackCooldownFrames;

        // 必须回滚，用于确定性地计算“刚刚按下”。
        public int PreviousButtons;
    }
}
```

### GameState.cs

```csharp
namespace _Src.Game
{
    public struct GameState
    {
        // 表示下一次准备模拟的帧。
        public int Frame;

        // 确定性随机数生成器的内部状态。
        public uint RandomState;

        public int RoundTimerFrames;

        // -1 未结束；0 平局；1 P1；2 P2。
        public int Winner;

        public FighterState P1;
        public FighterState P2;
    }
}
```

### FighterRules.cs

```csharp
namespace _Src.Game
{
    public struct FighterRules
    {
        public int MaxHp;
        public int RoundFrames;
        public int WalkSpeed;

        public int ArenaMinX;
        public int ArenaMaxX;

        public int AttackRange;
        public int AttackDamage;
        public int AttackActiveFrame;
        public int AttackTotalFrames;
        public int AttackCooldownFrames;
        public int HitstunFrames;

        public static FighterRules CreateDefault()
        {
            return new FighterRules
            {
                MaxHp = 100,
                RoundFrames = 99 * 60,
                WalkSpeed = 80,
                ArenaMinX = -8000,
                ArenaMaxX = 8000,
                AttackRange = 1800,
                AttackDamage = 10,
                AttackActiveFrame = 2,
                AttackTotalFrames = 12,
                AttackCooldownFrames = 16,
                HitstunFrames = 10
            };
        }
    }
}
```

### FighterSimulation.cs

```csharp
using System;

namespace _Src.Game
{
    public sealed class FighterSimulation
    {
        private readonly FighterRules m_Rules;
        private GameState m_State;

        public GameState State
        {
            get { return m_State; }
        }

        public FighterSimulation(FighterRules rules)
        {
            ValidateRules(rules);
            m_Rules = rules;
        }

        public void Initialize(uint seed)
        {
            m_State = new GameState
            {
                Frame = 0,
                RandomState = seed,
                RoundTimerFrames = m_Rules.RoundFrames,
                Winner = -1,
                P1 = CreateFighter(-2500, 1),
                P2 = CreateFighter(2500, -1)
            };
        }

        public void Restore(GameState state)
        {
            m_State = state;
        }

        public void Step(int frame, PlayerInput p1Input, PlayerInput p2Input)
        {
            if (frame != m_State.Frame)
            {
                throw new InvalidOperationException(
                    "Simulation frame mismatch. Expected " + m_State.Frame +
                    ", received " + frame + ".");
            }

            if (m_State.Winner < 0)
            {
                BeginFighterFrame(ref m_State.P1);
                BeginFighterFrame(ref m_State.P2);

                ApplyInput(ref m_State.P1, p1Input);
                ApplyInput(ref m_State.P2, p2Input);
                UpdateFacing();

                // 先计算双方是否命中，再同时应用，避免执行顺序影响相杀。
                bool p1Hits = IsAttackHitting(m_State.P1, m_State.P2);
                bool p2Hits = IsAttackHitting(m_State.P2, m_State.P1);

                if (p1Hits)
                    ApplyHit(ref m_State.P2, m_State.P1.Facing);
                if (p2Hits)
                    ApplyHit(ref m_State.P1, m_State.P2.Facing);

                EndFighterFrame(ref m_State.P1, p1Input);
                EndFighterFrame(ref m_State.P2, p2Input);
                UpdateRoundState();
            }

            m_State.Frame = frame + 1;
        }

        private FighterState CreateFighter(int positionX, int facing)
        {
            return new FighterState
            {
                PositionX = positionX,
                VelocityX = 0,
                Hp = m_Rules.MaxHp,
                Facing = facing,
                Action = FighterAction.Idle,
                ActionFrame = 0,
                HitstunFrames = 0,
                AttackCooldownFrames = 0,
                PreviousButtons = 0
            };
        }

        private static void BeginFighterFrame(ref FighterState fighter)
        {
            if (fighter.AttackCooldownFrames > 0)
                fighter.AttackCooldownFrames--;

            if (fighter.HitstunFrames > 0)
            {
                fighter.HitstunFrames--;
                if (fighter.HitstunFrames == 0 && fighter.Hp > 0)
                {
                    fighter.Action = FighterAction.Idle;
                    fighter.ActionFrame = 0;
                }
            }
        }

        private void ApplyInput(ref FighterState fighter, PlayerInput input)
        {
            if (fighter.Hp <= 0)
            {
                fighter.VelocityX = 0;
                fighter.Action = FighterAction.KnockedOut;
                return;
            }

            if (fighter.HitstunFrames > 0 || fighter.Action == FighterAction.Attack)
            {
                fighter.VelocityX = 0;
                return;
            }

            bool attackPressed = WasPressed(
                fighter.PreviousButtons, input.Buttons, PlayerButtons.Attack);

            if (attackPressed && fighter.AttackCooldownFrames == 0)
            {
                fighter.Action = FighterAction.Attack;
                fighter.ActionFrame = 0;
                fighter.AttackCooldownFrames = m_Rules.AttackCooldownFrames;
                fighter.VelocityX = 0;
                return;
            }

            int movement = input.MoveX;
            if (movement < -1) movement = -1;
            if (movement > 1) movement = 1;

            fighter.VelocityX = movement * m_Rules.WalkSpeed;
            fighter.PositionX = Clamp(
                fighter.PositionX + fighter.VelocityX,
                m_Rules.ArenaMinX,
                m_Rules.ArenaMaxX);
            fighter.Action = movement == 0
                ? FighterAction.Idle
                : FighterAction.Walking;
            fighter.ActionFrame = 0;
        }

        private void UpdateFacing()
        {
            if (m_State.P1.PositionX < m_State.P2.PositionX)
            {
                m_State.P1.Facing = 1;
                m_State.P2.Facing = -1;
            }
            else if (m_State.P1.PositionX > m_State.P2.PositionX)
            {
                m_State.P1.Facing = -1;
                m_State.P2.Facing = 1;
            }
        }

        private bool IsAttackHitting(FighterState attacker, FighterState defender)
        {
            if (attacker.Action != FighterAction.Attack ||
                attacker.ActionFrame != m_Rules.AttackActiveFrame)
                return false;

            int distance = attacker.PositionX - defender.PositionX;
            if (distance < 0) distance = -distance;
            if (distance > m_Rules.AttackRange) return false;

            int directionToDefender = defender.PositionX >= attacker.PositionX ? 1 : -1;
            return directionToDefender == attacker.Facing;
        }

        private void ApplyHit(ref FighterState defender, int knockbackDirection)
        {
            if (defender.Hp <= 0) return;

            defender.Hp -= m_Rules.AttackDamage;
            if (defender.Hp < 0) defender.Hp = 0;

            defender.VelocityX = knockbackDirection * 120;
            defender.PositionX = Clamp(
                defender.PositionX + defender.VelocityX,
                m_Rules.ArenaMinX,
                m_Rules.ArenaMaxX);

            if (defender.Hp == 0)
            {
                defender.Action = FighterAction.KnockedOut;
                defender.ActionFrame = 0;
                defender.HitstunFrames = 0;
            }
            else
            {
                defender.Action = FighterAction.Hitstun;
                defender.ActionFrame = 0;
                defender.HitstunFrames = m_Rules.HitstunFrames;
            }
        }

        private void EndFighterFrame(ref FighterState fighter, PlayerInput input)
        {
            if (fighter.Action == FighterAction.Attack)
            {
                fighter.ActionFrame++;
                if (fighter.ActionFrame >= m_Rules.AttackTotalFrames)
                {
                    fighter.Action = FighterAction.Idle;
                    fighter.ActionFrame = 0;
                }
            }

            fighter.PreviousButtons = input.Buttons;
        }

        private void UpdateRoundState()
        {
            if (m_State.RoundTimerFrames > 0)
                m_State.RoundTimerFrames--;

            if (m_State.P1.Hp <= 0 && m_State.P2.Hp <= 0)
                m_State.Winner = 0;
            else if (m_State.P1.Hp <= 0)
                m_State.Winner = 2;
            else if (m_State.P2.Hp <= 0)
                m_State.Winner = 1;
            else if (m_State.RoundTimerFrames == 0)
                m_State.Winner = m_State.P1.Hp == m_State.P2.Hp
                    ? 0
                    : (m_State.P1.Hp > m_State.P2.Hp ? 1 : 2);
        }

        private static bool WasPressed(
            int previousButtons, int currentButtons, PlayerButtons button)
        {
            int mask = (int)button;
            return (previousButtons & mask) == 0 && (currentButtons & mask) != 0;
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            if (value < minimum) return minimum;
            if (value > maximum) return maximum;
            return value;
        }

        private static void ValidateRules(FighterRules rules)
        {
            if (rules.MaxHp <= 0 || rules.RoundFrames <= 0)
                throw new ArgumentException("Invalid fighter rules.", nameof(rules));
            if (rules.ArenaMaxX <= rules.ArenaMinX)
                throw new ArgumentException("Invalid arena bounds.", nameof(rules));
            if (rules.AttackTotalFrames <= 0 ||
                rules.AttackActiveFrame < 0 ||
                rules.AttackActiveFrame >= rules.AttackTotalFrames)
                throw new ArgumentException("Invalid attack frame data.", nameof(rules));
        }
    }
}
```

---

## 12. 状态快照与胶水层

### GameStateCodec.cs

```csharp
using System;
using _Src.Serialization;

namespace _Src.Game
{
    public sealed class GameStateCodec
    {
        private const int HeaderSize = 16;
        private const int FighterSize = 9 * 4;
        public const int SerializedSize = HeaderSize + FighterSize * 2;

        public byte[] Serialize(GameState state)
        {
            byte[] buffer = new byte[SerializedSize];
            int offset = 0;

            WriteInt32(buffer, ref offset, state.Frame);
            WriteUInt32(buffer, ref offset, state.RandomState);
            WriteInt32(buffer, ref offset, state.RoundTimerFrames);
            WriteInt32(buffer, ref offset, state.Winner);
            WriteFighter(buffer, ref offset, state.P1);
            WriteFighter(buffer, ref offset, state.P2);

            if (offset != SerializedSize)
                throw new InvalidOperationException("Unexpected serialized state size.");
            return buffer;
        }

        public GameState Deserialize(byte[] buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (buffer.Length != SerializedSize)
                throw new ArgumentException("Invalid game-state buffer size.", nameof(buffer));

            int offset = 0;
            GameState state = new GameState
            {
                Frame = ReadInt32(buffer, ref offset),
                RandomState = ReadUInt32(buffer, ref offset),
                RoundTimerFrames = ReadInt32(buffer, ref offset),
                Winner = ReadInt32(buffer, ref offset),
                P1 = ReadFighter(buffer, ref offset),
                P2 = ReadFighter(buffer, ref offset)
            };

            if (offset != SerializedSize)
                throw new InvalidOperationException("Unexpected deserialized state size.");
            return state;
        }

        public ulong CalculateChecksum(byte[] buffer)
        {
            return DeterministicBinary.CalculateChecksum(buffer);
        }

        private static void WriteFighter(
            byte[] buffer, ref int offset, FighterState fighter)
        {
            WriteInt32(buffer, ref offset, fighter.PositionX);
            WriteInt32(buffer, ref offset, fighter.VelocityX);
            WriteInt32(buffer, ref offset, fighter.Hp);
            WriteInt32(buffer, ref offset, fighter.Facing);
            WriteInt32(buffer, ref offset, (int)fighter.Action);
            WriteInt32(buffer, ref offset, fighter.ActionFrame);
            WriteInt32(buffer, ref offset, fighter.HitstunFrames);
            WriteInt32(buffer, ref offset, fighter.AttackCooldownFrames);
            WriteInt32(buffer, ref offset, fighter.PreviousButtons);
        }

        private static FighterState ReadFighter(byte[] buffer, ref int offset)
        {
            return new FighterState
            {
                PositionX = ReadInt32(buffer, ref offset),
                VelocityX = ReadInt32(buffer, ref offset),
                Hp = ReadInt32(buffer, ref offset),
                Facing = ReadInt32(buffer, ref offset),
                Action = (FighterAction)ReadInt32(buffer, ref offset),
                ActionFrame = ReadInt32(buffer, ref offset),
                HitstunFrames = ReadInt32(buffer, ref offset),
                AttackCooldownFrames = ReadInt32(buffer, ref offset),
                PreviousButtons = ReadInt32(buffer, ref offset)
            };
        }

        private static void WriteInt32(byte[] buffer, ref int offset, int value)
        {
            DeterministicBinary.WriteInt32(buffer, offset, value);
            offset += 4;
        }

        private static void WriteUInt32(byte[] buffer, ref int offset, uint value)
        {
            DeterministicBinary.WriteUInt32(buffer, offset, value);
            offset += 4;
        }

        private static int ReadInt32(byte[] buffer, ref int offset)
        {
            int value = DeterministicBinary.ReadInt32(buffer, offset);
            offset += 4;
            return value;
        }

        private static uint ReadUInt32(byte[] buffer, ref int offset)
        {
            uint value = DeterministicBinary.ReadUInt32(buffer, offset);
            offset += 4;
            return value;
        }
    }
}
```

### RollbackGameAdapter.cs

```csharp
using System;
using _Src.GGPO;

namespace _Src.Game
{
    public sealed class RollbackGameAdapter
    {
        private readonly FighterSimulation m_Simulation;
        private readonly GameStateCodec m_Codec;
        private readonly uint m_InitialSeed;

        public RollbackGameAdapter(
            FighterSimulation simulation, GameStateCodec codec, uint initialSeed)
        {
            m_Simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
            m_Codec = codec ?? throw new ArgumentNullException(nameof(codec));
            m_InitialSeed = initialSeed;
        }

        public GgpoCallback<PlayerInput> CreateCallbacks()
        {
            return new GgpoCallback<PlayerInput>
            {
                OnSessionStarted = OnSessionStarted,
                SaveGameState = SaveGameState,
                LoadGameState = LoadGameState,
                AdvanceFrame = AdvanceFrame
            };
        }

        private void OnSessionStarted()
        {
            m_Simulation.Initialize(m_InitialSeed);
        }

        private GgpoSavedState SaveGameState(int frame)
        {
            GameState state = m_Simulation.State;
            if (state.Frame != frame)
                throw new InvalidOperationException(
                    "Cannot save frame " + frame +
                    " because simulation is at frame " + state.Frame + ".");

            byte[] buffer = m_Codec.Serialize(state);
            return new GgpoSavedState
            {
                Buffer = buffer,
                Checksums = m_Codec.CalculateChecksum(buffer)
            };
        }

        private void LoadGameState(byte[] buffer)
        {
            m_Simulation.Restore(m_Codec.Deserialize(buffer));
        }

        private void AdvanceFrame(int frame, PlayerInput[] inputs)
        {
            if (inputs == null || inputs.Length != 2)
                throw new ArgumentException(
                    "Exactly two player inputs are required.", nameof(inputs));

            // Session 会复用数组，不能保存 inputs 引用。
            PlayerInput p1 = inputs[0];
            PlayerInput p2 = inputs[1];
            m_Simulation.Step(frame, p1, p2);
        }
    }
}
```

---

## 13. 输入与输入序列化

### IPlayerInputSource.cs

```csharp
namespace _Src.Game
{
    public interface IPlayerInputSource
    {
        PlayerInput SamplePlayerInput(int playerIndex);
    }
}
```

### UnityPlayerInputSource.cs

```csharp
using System;
using UnityEngine;

namespace _Src.Game
{
    public sealed class UnityPlayerInputSource : IPlayerInputSource
    {
        public PlayerInput SamplePlayerInput(int playerIndex)
        {
            switch (playerIndex)
            {
                case 0: return SamplePlayerOne();
                case 1: return SamplePlayerTwo();
                default: throw new ArgumentOutOfRangeException(nameof(playerIndex));
            }
        }

        private static PlayerInput SamplePlayerOne()
        {
            int moveX = 0;
            if (Input.GetKey(KeyCode.A)) moveX--;
            if (Input.GetKey(KeyCode.D)) moveX++;

            PlayerButtons buttons = PlayerButtons.None;
            if (Input.GetKey(KeyCode.J)) buttons |= PlayerButtons.Attack;
            if (Input.GetKey(KeyCode.K)) buttons |= PlayerButtons.Jump;
            if (Input.GetKey(KeyCode.L)) buttons |= PlayerButtons.Block;

            return new PlayerInput
            {
                MoveX = NormalizeAxis(moveX),
                Buttons = (byte)buttons
            };
        }

        private static PlayerInput SamplePlayerTwo()
        {
            int moveX = 0;
            if (Input.GetKey(KeyCode.LeftArrow)) moveX--;
            if (Input.GetKey(KeyCode.RightArrow)) moveX++;

            PlayerButtons buttons = PlayerButtons.None;
            if (Input.GetKey(KeyCode.Keypad1)) buttons |= PlayerButtons.Attack;
            if (Input.GetKey(KeyCode.Keypad2)) buttons |= PlayerButtons.Jump;
            if (Input.GetKey(KeyCode.Keypad3)) buttons |= PlayerButtons.Block;

            return new PlayerInput
            {
                MoveX = NormalizeAxis(moveX),
                Buttons = (byte)buttons
            };
        }

        private static sbyte NormalizeAxis(int value)
        {
            if (value < 0) return -1;
            if (value > 0) return 1;
            return 0;
        }
    }
}
```

### PlayerInputSerializer.cs

```csharp
using _Src.GGPO;

namespace _Src.Game
{
    public sealed class PlayerInputSerializer
        : IGgpoInputSerializer<PlayerInput>
    {
        public byte[] Encode(PlayerInput input)
        {
            return new[]
            {
                unchecked((byte)input.MoveX),
                input.Buttons
            };
        }

        public bool TryDecode(byte[] bytes, out PlayerInput input)
        {
            input = default(PlayerInput);
            if (bytes == null || bytes.Length != 2) return false;

            input.MoveX = unchecked((sbyte)bytes[0]);
            input.Buttons = bytes[1];
            return input.MoveX >= -1 && input.MoveX <= 1;
        }
    }
}
```

---

## 14. GGPO 核心类型与 Session 实现

### GgpoPlayer.cs

Session 在构造时确定玩家总数并一次性分配槽位。`AddPlayer` 只依次填写这些固定槽位。

```csharp
using System;
using System.Collections.Generic;

namespace _Src.GGPO
{
    public enum GgpoPlayerType
    {
        Local,
        Remote
    }

    [Serializable]
    public struct GgpoPlayerConfig
    {
        public GgpoPlayerType Type;
        public int InputDelayFrames;

        public GgpoPlayerConfig(GgpoPlayerType type, int inputDelayFrames)
        {
            Type = type;
            InputDelayFrames = inputDelayFrames;
        }
    }

    internal sealed class GgpoInputQueue<TInput>
    {
        public readonly GgpoPlayerType Type;
        public readonly int InputDelayFrames;
        public readonly Dictionary<int, TInput> Inputs =
            new Dictionary<int, TInput>();
        public readonly Dictionary<int, TInput> PredictedInputs =
            new Dictionary<int, TInput>();
        public readonly Dictionary<int, TInput> UsedInputs =
            new Dictionary<int, TInput>();

        public int LastLocalSubmittedFrame = -1;
        public int LastConfirmedRemoteFrame;
        public TInput InputBeforeHistory;
        public bool HasInputBeforeHistory;

        public GgpoInputQueue(GgpoPlayerConfig config)
        {
            Type = config.Type;
            InputDelayFrames = config.InputDelayFrames;
            // 输入延迟之前的帧是确定的 default(TInput)。
            LastConfirmedRemoteFrame = config.InputDelayFrames - 1;
        }
    }
}
```

### GgpoSavedState.cs

```csharp
namespace _Src.GGPO
{
    public sealed class GgpoSavedState
    {
        public byte[] Buffer;
        public ulong Checksums;
    }
}
```

### GgpoCallbacks.cs

```csharp
using System;

namespace _Src.GGPO
{
    public sealed class GgpoCallback<TInput>
    {
        public Action OnSessionStarted;
        public Func<int, GgpoSavedState> SaveGameState;
        public Action<byte[]> LoadGameState;

        // 数组按固定玩家槽位排列并由 Session 复用；回调不能保留或修改它。
        public Action<int, TInput[]> AdvanceFrame;
    }
}
```

### IGgpoInputSerializer.cs

```csharp
namespace _Src.GGPO
{
    public interface IGgpoInputSerializer<TInput>
    {
        byte[] Encode(TInput input);
        bool TryDecode(byte[] encoded, out TInput input);
    }
}
```

### IGgpoTransport.cs

```csharp
using System;

namespace _Src.GGPO
{
    public interface IGgpoTransport<TInput> : IDisposable
    {
        void QueueLocalInput(int playerIndex, int frame, TInput input);
        void Pump(Action<int, int, TInput> onRemoteInput);
    }
}
```

### GgpoSession.cs

```csharp
using System;
using System.Collections.Generic;

namespace _Src.GGPO
{
    /// <summary>
    /// 固定玩家数量的确定性回滚会话。
    /// 构造时决定玩家总数；AddPlayer 只填写预留槽位。
    /// </summary>
    public sealed class GgpoSession<TInput> : IDisposable
    {
        private readonly GgpoCallback<TInput> m_Callback;
        private readonly IGgpoTransport<TInput> m_Transport;
        private readonly int m_MaxRollbackFrames;

        private readonly GgpoInputQueue<TInput>[] m_PlayerQueues;
        private readonly TInput[] m_SynchronizedInputs;
        private readonly Dictionary<int, GgpoSavedState> m_Snapshots =
            new Dictionary<int, GgpoSavedState>();
        private readonly List<int> m_FramesToRemove = new List<int>();

        private int m_RegisteredPlayerCount;
        private int m_CurrentFrame;
        private int m_EarliestRollbackFrame = -1;
        private bool m_ArePlayersLocked;
        private bool m_HasSynchronizedCurrentFrame;
        private bool m_IsRollingBack;
        private bool m_IsClosed;

        public int CurrentFrame { get { return m_CurrentFrame; } }
        public int PlayerCount { get { return m_PlayerQueues.Length; } }
        public int RegisteredPlayerCount { get { return m_RegisteredPlayerCount; } }
        public bool IsRollingBack { get { return m_IsRollingBack; } }

        public GgpoSession(
            GgpoCallback<TInput> callback,
            IGgpoTransport<TInput> transport,
            int playerCount,
            int maxRollbackFrames)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (transport == null) throw new ArgumentNullException(nameof(transport));
            if (callback.SaveGameState == null)
                throw new ArgumentException("SaveGameState is required.", nameof(callback));
            if (callback.LoadGameState == null)
                throw new ArgumentException("LoadGameState is required.", nameof(callback));
            if (callback.AdvanceFrame == null)
                throw new ArgumentException("AdvanceFrame is required.", nameof(callback));
            if (playerCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(playerCount));
            if (maxRollbackFrames <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxRollbackFrames));

            m_Callback = callback;
            m_Transport = transport;
            m_MaxRollbackFrames = maxRollbackFrames;
            m_PlayerQueues = new GgpoInputQueue<TInput>[playerCount];
            m_SynchronizedInputs = new TInput[playerCount];

            m_Callback.OnSessionStarted?.Invoke();
        }

        public int AddPlayer(GgpoPlayerType type, int inputDelayFrames)
        {
            ThrowIfClosed();
            if (m_ArePlayersLocked)
                throw new InvalidOperationException(
                    "Players cannot be added after synchronization starts.");
            if (inputDelayFrames < 0)
                throw new ArgumentOutOfRangeException(nameof(inputDelayFrames));
            if (m_RegisteredPlayerCount >= m_PlayerQueues.Length)
                throw new InvalidOperationException(
                    "All reserved player slots are already registered.");

            int playerIndex = m_RegisteredPlayerCount;
            m_PlayerQueues[playerIndex] = new GgpoInputQueue<TInput>(
                new GgpoPlayerConfig(type, inputDelayFrames));
            m_RegisteredPlayerCount++;
            return playerIndex;
        }

        public void Idle(int timeoutMilliseconds)
        {
            ThrowIfClosed();
            LockPlayers();
            if (timeoutMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            if (m_HasSynchronizedCurrentFrame)
                throw new InvalidOperationException(
                    "AdvanceFrame must be called before Idle.");

            m_Transport.Pump(SetRemoteInput);
            RollbackResimulate();
        }

        public void AddLocalInput(int playerIndex, TInput input)
        {
            ThrowIfClosed();
            LockPlayers();
            if (m_IsRollingBack)
                throw new InvalidOperationException(
                    "Cannot submit local input during rollback.");
            if (m_HasSynchronizedCurrentFrame)
                throw new InvalidOperationException(
                    "Local input must be submitted before synchronization.");

            GgpoInputQueue<TInput> queue = GetQueue(playerIndex);
            if (queue.Type != GgpoPlayerType.Local)
                throw new InvalidOperationException("The player is not local.");
            if (queue.LastLocalSubmittedFrame != m_CurrentFrame - 1)
                throw new InvalidOperationException(
                    "Local input was already submitted, or a previous frame was skipped.");

            int appliedFrame = m_CurrentFrame + queue.InputDelayFrames;
            if (queue.Inputs.ContainsKey(appliedFrame))
                throw new InvalidOperationException(
                    "An input already exists for frame " + appliedFrame + ".");

            queue.Inputs.Add(appliedFrame, input);
            try
            {
                m_Transport.QueueLocalInput(playerIndex, appliedFrame, input);
            }
            catch
            {
                queue.Inputs.Remove(appliedFrame);
                throw;
            }

            queue.LastLocalSubmittedFrame = m_CurrentFrame;
        }

        public bool TrySynchronizeInputs(TInput[] output)
        {
            ThrowIfClosed();
            LockPlayers();
            if (output == null || output.Length != m_PlayerQueues.Length)
                throw new ArgumentException(
                    "Input array length must equal PlayerCount.", nameof(output));
            if (m_HasSynchronizedCurrentFrame)
                throw new InvalidOperationException(
                    "Current frame has already been synchronized.");
            if (!AreAllLocalInputsSubmitted()) return false;
            if (HasReachedPredictionBarrier()) return false;

            SynchronizeInputsForFrame(m_CurrentFrame, m_SynchronizedInputs);
            Array.Copy(m_SynchronizedInputs, output, m_SynchronizedInputs.Length);
            m_HasSynchronizedCurrentFrame = true;
            return true;
        }

        public void AdvanceFrame()
        {
            ThrowIfClosed();
            LockPlayers();
            if (!m_HasSynchronizedCurrentFrame)
                throw new InvalidOperationException(
                    "TrySynchronizeInputs must succeed before AdvanceFrame.");

            SaveSnapshotIfMissing(m_CurrentFrame);
            SimulateOneFrame(m_CurrentFrame, m_SynchronizedInputs);
            m_CurrentFrame++;
            m_HasSynchronizedCurrentFrame = false;
            SaveSnapshotIfMissing(m_CurrentFrame);
            PruneHistory();
        }

        public void Close()
        {
            if (m_IsClosed) return;

            for (int i = 0; i < m_PlayerQueues.Length; i++)
            {
                GgpoInputQueue<TInput> queue = m_PlayerQueues[i];
                if (queue == null) continue;
                queue.Inputs.Clear();
                queue.PredictedInputs.Clear();
                queue.UsedInputs.Clear();
            }

            m_Snapshots.Clear();
            m_FramesToRemove.Clear();
            m_Transport.Dispose();
            m_IsClosed = true;
        }

        public void Dispose()
        {
            Close();
        }

        private void SetRemoteInput(int playerIndex, int frame, TInput input)
        {
            ThrowIfClosed();
            if (m_HasSynchronizedCurrentFrame)
                throw new InvalidOperationException(
                    "Remote input cannot be pumped between synchronization and advance.");
            if (frame < 0) throw new ArgumentOutOfRangeException(nameof(frame));

            int firstRetainedFrame = m_CurrentFrame - m_MaxRollbackFrames;
            if (frame < firstRetainedFrame)
                throw new InvalidOperationException(
                    "Remote input is older than the rollback history.");

            GgpoInputQueue<TInput> queue = GetQueue(playerIndex);
            if (queue.Type != GgpoPlayerType.Remote)
                throw new InvalidOperationException(
                    "Received remote input for a local player.");

            TInput usedInput;
            bool wasSimulated = frame < m_CurrentFrame &&
                                queue.UsedInputs.TryGetValue(frame, out usedInput);
            queue.Inputs[frame] = input;
            AdvanceLastConfirmedRemoteFrame(queue);

            if (wasSimulated &&
                !EqualityComparer<TInput>.Default.Equals(input, usedInput))
                ScheduleRollback(frame);
            else
                queue.PredictedInputs.Remove(frame);
        }

        private void SimulateOneFrame(int frame, TInput[] inputs)
        {
            for (int i = 0; i < m_PlayerQueues.Length; i++)
                m_PlayerQueues[i].UsedInputs[frame] = inputs[i];
            m_Callback.AdvanceFrame(frame, inputs);
        }

        private void RollbackResimulate()
        {
            if (m_EarliestRollbackFrame < 0) return;

            int rollbackFrame = m_EarliestRollbackFrame;
            int targetFrame = m_CurrentFrame;
            GgpoSavedState snapshot;
            if (!m_Snapshots.TryGetValue(rollbackFrame, out snapshot) ||
                snapshot == null || snapshot.Buffer == null)
                throw new InvalidOperationException(
                    "Missing snapshot for rollback frame " + rollbackFrame + ".");

            m_IsRollingBack = true;
            m_HasSynchronizedCurrentFrame = false;
            try
            {
                m_Callback.LoadGameState(snapshot.Buffer);

                for (int i = 0; i < m_PlayerQueues.Length; i++)
                {
                    RemoveKeysAtOrAfter(
                        m_PlayerQueues[i].UsedInputs, rollbackFrame);
                    RemoveKeysAtOrAfter(
                        m_PlayerQueues[i].PredictedInputs, rollbackFrame);
                }
                RemoveSnapshotsAfter(rollbackFrame);
                m_CurrentFrame = rollbackFrame;

                while (m_CurrentFrame < targetFrame)
                {
                    SynchronizeInputsForFrame(
                        m_CurrentFrame, m_SynchronizedInputs);
                    SimulateOneFrame(m_CurrentFrame, m_SynchronizedInputs);
                    m_CurrentFrame++;
                    SaveSnapshotIfMissing(m_CurrentFrame);
                }

                m_EarliestRollbackFrame = -1;
                PruneHistory();
            }
            catch
            {
                Close();
                throw;
            }
            finally
            {
                m_IsRollingBack = false;
                m_HasSynchronizedCurrentFrame = false;
            }
        }

        private bool AreAllLocalInputsSubmitted()
        {
            for (int i = 0; i < m_PlayerQueues.Length; i++)
            {
                GgpoInputQueue<TInput> queue = m_PlayerQueues[i];
                if (queue.Type == GgpoPlayerType.Local &&
                    queue.LastLocalSubmittedFrame != m_CurrentFrame)
                    return false;
            }
            return true;
        }

        private bool HasReachedPredictionBarrier()
        {
            bool hasRemotePlayer = false;
            int lastConfirmedFrame = int.MaxValue;

            for (int i = 0; i < m_PlayerQueues.Length; i++)
            {
                GgpoInputQueue<TInput> queue = m_PlayerQueues[i];
                if (queue.Type != GgpoPlayerType.Remote) continue;
                hasRemotePlayer = true;
                if (queue.LastConfirmedRemoteFrame < lastConfirmedFrame)
                    lastConfirmedFrame = queue.LastConfirmedRemoteFrame;
            }

            return hasRemotePlayer &&
                   m_CurrentFrame - lastConfirmedFrame >= m_MaxRollbackFrames;
        }

        private void SynchronizeInputsForFrame(int frame, TInput[] output)
        {
            for (int i = 0; i < m_PlayerQueues.Length; i++)
                output[i] = GetInput(m_PlayerQueues[i], frame);
        }

        private static TInput GetInput(GgpoInputQueue<TInput> queue, int frame)
        {
            TInput actualInput;
            if (queue.Inputs.TryGetValue(frame, out actualInput))
            {
                queue.PredictedInputs.Remove(frame);
                return actualInput;
            }

            TInput predictedInput = FindLatestInput(queue, frame);
            if (queue.Type == GgpoPlayerType.Remote)
                queue.PredictedInputs[frame] = predictedInput;
            return predictedInput;
        }

        private static TInput FindLatestInput(
            GgpoInputQueue<TInput> queue, int frame)
        {
            int latestFrame = -1;
            TInput result = queue.HasInputBeforeHistory
                ? queue.InputBeforeHistory
                : default(TInput);

            foreach (KeyValuePair<int, TInput> pair in queue.Inputs)
            {
                if (pair.Key <= frame && pair.Key > latestFrame)
                {
                    latestFrame = pair.Key;
                    result = pair.Value;
                }
            }
            return result;
        }

        private void SaveSnapshotIfMissing(int frame)
        {
            if (m_Snapshots.ContainsKey(frame)) return;
            GgpoSavedState state = m_Callback.SaveGameState(frame);
            if (state == null || state.Buffer == null)
                throw new InvalidOperationException(
                    "SaveGameState must return a valid buffer.");
            m_Snapshots.Add(frame, state);
        }

        private static void AdvanceLastConfirmedRemoteFrame(
            GgpoInputQueue<TInput> queue)
        {
            while (queue.Inputs.ContainsKey(queue.LastConfirmedRemoteFrame + 1))
                queue.LastConfirmedRemoteFrame++;
        }

        private void ScheduleRollback(int frame)
        {
            if (m_EarliestRollbackFrame < 0 ||
                frame < m_EarliestRollbackFrame)
                m_EarliestRollbackFrame = frame;
        }

        private void PruneHistory()
        {
            int firstRetainedFrame = m_CurrentFrame - m_MaxRollbackFrames;
            if (firstRetainedFrame <= 0) return;

            for (int i = 0; i < m_PlayerQueues.Length; i++)
            {
                PruneInputs(m_PlayerQueues[i], firstRetainedFrame);
                RemoveKeysBefore(
                    m_PlayerQueues[i].PredictedInputs, firstRetainedFrame);
                RemoveKeysBefore(
                    m_PlayerQueues[i].UsedInputs, firstRetainedFrame);
            }
            RemoveSnapshotsBefore(firstRetainedFrame);
        }

        private void PruneInputs(
            GgpoInputQueue<TInput> queue, int firstRetainedFrame)
        {
            m_FramesToRemove.Clear();
            int latestRemovedFrame = int.MinValue;
            foreach (KeyValuePair<int, TInput> pair in queue.Inputs)
            {
                if (pair.Key >= firstRetainedFrame) continue;
                m_FramesToRemove.Add(pair.Key);
                if (pair.Key > latestRemovedFrame)
                {
                    latestRemovedFrame = pair.Key;
                    queue.InputBeforeHistory = pair.Value;
                    queue.HasInputBeforeHistory = true;
                }
            }
            RemoveCollectedKeys(queue.Inputs);
        }

        private void RemoveSnapshotsBefore(int firstRetainedFrame)
        {
            m_FramesToRemove.Clear();
            foreach (KeyValuePair<int, GgpoSavedState> pair in m_Snapshots)
                if (pair.Key < firstRetainedFrame)
                    m_FramesToRemove.Add(pair.Key);
            RemoveCollectedKeys(m_Snapshots);
        }

        private void RemoveSnapshotsAfter(int retainedFrame)
        {
            m_FramesToRemove.Clear();
            foreach (KeyValuePair<int, GgpoSavedState> pair in m_Snapshots)
                if (pair.Key > retainedFrame)
                    m_FramesToRemove.Add(pair.Key);
            RemoveCollectedKeys(m_Snapshots);
        }

        private void RemoveKeysBefore<TValue>(
            Dictionary<int, TValue> values, int firstFrame)
        {
            m_FramesToRemove.Clear();
            foreach (KeyValuePair<int, TValue> pair in values)
                if (pair.Key < firstFrame)
                    m_FramesToRemove.Add(pair.Key);
            RemoveCollectedKeys(values);
        }

        private void RemoveKeysAtOrAfter<TValue>(
            Dictionary<int, TValue> values, int firstFrame)
        {
            m_FramesToRemove.Clear();
            foreach (KeyValuePair<int, TValue> pair in values)
                if (pair.Key >= firstFrame)
                    m_FramesToRemove.Add(pair.Key);
            RemoveCollectedKeys(values);
        }

        private void RemoveCollectedKeys<TValue>(Dictionary<int, TValue> values)
        {
            for (int i = 0; i < m_FramesToRemove.Count; i++)
                values.Remove(m_FramesToRemove[i]);
            m_FramesToRemove.Clear();
        }

        private GgpoInputQueue<TInput> GetQueue(int playerIndex)
        {
            if (playerIndex < 0 || playerIndex >= m_PlayerQueues.Length)
                throw new ArgumentOutOfRangeException(nameof(playerIndex));
            GgpoInputQueue<TInput> queue = m_PlayerQueues[playerIndex];
            if (queue == null)
                throw new InvalidOperationException(
                    "Player slot " + playerIndex + " is not registered.");
            return queue;
        }

        private void LockPlayers()
        {
            if (m_ArePlayersLocked) return;
            if (m_RegisteredPlayerCount != m_PlayerQueues.Length)
                throw new InvalidOperationException(
                    "Cannot start synchronization. Expected " +
                    m_PlayerQueues.Length + " players, but only " +
                    m_RegisteredPlayerCount + " were registered.");

            for (int i = 0; i < m_PlayerQueues.Length; i++)
                if (m_PlayerQueues[i] == null)
                    throw new InvalidOperationException(
                        "Player slot " + i + " is not registered.");
            m_ArePlayersLocked = true;
        }

        private void ThrowIfClosed()
        {
            if (m_IsClosed)
                throw new ObjectDisposedException(nameof(GgpoSession<TInput>));
        }
    }
}
```

---

## 15. 可选测试启动 UI

### TestGUI.cs

```csharp
using System;
using UnityEngine;

namespace _Src.Game
{
    /// <summary>
    /// 原型阶段用于启动本地双人或两个进程 UDP 对局。
    /// 与 GameMain 放在同一个 GameObject 上。
    /// </summary>
    public sealed class TestGUI : MonoBehaviour
    {
        private GameMain m_Main;
        private string m_LocalPort = "7000";
        private string m_TargetEndpoint = "127.0.0.1:7001";
        private int m_LocalPlayerIndex;
        private string m_Error;

        private void Awake()
        {
            m_Main = GetComponent<GameMain>();
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(
                new Rect(16f, 16f, 320f, 330f),
                GUI.skin.box);
            GUILayout.Label("GGPO Test Launcher");

            if (m_Main == null)
            {
                GUILayout.Label("GameMain is required.");
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label("Local UDP port");
            m_LocalPort = GUILayout.TextField(m_LocalPort);

            if (GUILayout.Button("Start local two-player match"))
                TryStart(PlayMode.Local, 0, null, 0);

            GUILayout.Space(10f);
            GUILayout.Label("Remote endpoint (IPv4:port)");
            m_TargetEndpoint = GUILayout.TextField(m_TargetEndpoint);
            GUILayout.Label("This instance controls");
            m_LocalPlayerIndex = GUILayout.SelectionGrid(
                m_LocalPlayerIndex,
                new[] { "Player 1", "Player 2" },
                2);

            if (GUILayout.Button("Start network match"))
            {
                string address;
                int port;
                if (!TryParseEndpoint(m_TargetEndpoint, out address, out port))
                    m_Error = "Endpoint must use IPv4:port.";
                else
                    TryStart(
                        PlayMode.Remote,
                        m_LocalPlayerIndex,
                        address,
                        port);
            }

            GUILayout.Space(10f);
            if (m_Main.HasSession)
            {
                GUILayout.Label("Frame: " + m_Main.CurrentFrame);
                GUILayout.Label("P1 HP: " + m_Main.Player1Hp);
                GUILayout.Label("P2 HP: " + m_Main.Player2Hp);
                GUILayout.Label("Winner: " + m_Main.Winner);
            }
            else
            {
                GUILayout.Label("No session started.");
            }

            if (!string.IsNullOrEmpty(m_Error))
                GUILayout.Label(m_Error);

            GUILayout.EndArea();
        }

        private void TryStart(
            PlayMode mode,
            int localPlayerIndex,
            string targetAddress,
            int targetPort)
        {
            int localPort;
            if (!int.TryParse(m_LocalPort, out localPort) ||
                localPort < 1 || localPort > ushort.MaxValue)
            {
                m_Error = "Local port must be between 1 and 65535.";
                return;
            }

            try
            {
                m_Main.InitSession(mode, new ConnectInfo
                {
                    LocalPort = localPort,
                    TargetAddress = targetAddress,
                    TargetPort = targetPort,
                    LocalPlayerIndex = localPlayerIndex
                });
                m_Error = null;
            }
            catch (Exception exception)
            {
                m_Error = exception.Message;
                Debug.LogException(exception);
            }
        }

        private static bool TryParseEndpoint(
            string text,
            out string address,
            out int port)
        {
            address = null;
            port = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            int separator = text.LastIndexOf(':');
            if (separator <= 0 || separator == text.Length - 1)
                return false;

            address = text.Substring(0, separator).Trim();
            return !string.IsNullOrEmpty(address) &&
                   int.TryParse(text.Substring(separator + 1), out port) &&
                   port >= 1 && port <= ushort.MaxValue;
        }
    }
}
```

本文代码的推荐使用方式是逐层手写并测试，而不是一次性全部复制进项目。优先顺序：`FighterSimulation` → `GameStateCodec` → 本地 Session → SyncTest → UDP Transport → 表现事件去重。

## 16. 官方参考资料

- GGPO 官方仓库：<https://github.com/pond3r/ggpo>
- Developer Guide：<https://github.com/pond3r/ggpo/blob/master/doc/DeveloperGuide.md>
- 官方 API 与 callback 定义：<https://github.com/pond3r/ggpo/blob/master/src/include/ggponet.h>
- VectorWar 示例：<https://github.com/pond3r/ggpo/blob/master/src/apps/vectorwar/vectorwar.cpp>

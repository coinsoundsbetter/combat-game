# Unity 双人格斗游戏：GGPO 风格分层与完整参考代码

> 用途：架构参考与手动重写。本文代码复用项目中的命名空间 `_Src.Game`、`_Src.GGPO` 和 `_Src.Serialization`。
>
> 重要：这是一个便于学习和继续开发的“GGPO 风格回滚核心”，不是官方 C++ GGPO SDK 的完整移植。输入预测、状态快照和回滚重演位于 Session；握手、ACK、TimeSync、断线检测、观战等属于后续网络协议层。

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

## 6. Match 配置、装配和运行

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

## 7. Unity 表现和入口

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

## 2. 建议目录

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

## 3. 战斗模拟层

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

## 4. 状态快照与胶水层

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

## 5. 输入与输入序列化

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

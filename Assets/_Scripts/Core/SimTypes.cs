using System;

namespace FightGame
{
    // 5-bit 输入压成 1 字节：网络传输与回放录制都极小，且天然确定性。
    [Flags]
    public enum InputBits : byte
    {
        Left   = 1,
        Right  = 2,
        Up     = 4,
        Punch  = 8,
        Block  = 16,
    }

    // 值类型：拷贝即全量拷贝，回滚 save/load 可整块复制。
    [Serializable]
    public struct InputState : IEquatable<InputState>
    {
        public byte bits;

        public bool Left  => (bits & (byte)InputBits.Left)  != 0;
        public bool Right => (bits & (byte)InputBits.Right) != 0;
        public bool Up    => (bits & (byte)InputBits.Up)    != 0;
        public bool Punch => (bits & (byte)InputBits.Punch) != 0;
        public bool Block => (bits & (byte)InputBits.Block) != 0;

        public InputState(byte b) { bits = b; }
        public bool Equals(InputState o) => bits == o.bits;
        public override bool Equals(object o) => o is InputState s && Equals(s);
        public override int GetHashCode() => bits;
        public static bool operator ==(InputState a, InputState b) => a.bits == b.bits;
        public static bool operator !=(InputState a, InputState b) => a.bits != b.bits;
    }

    public enum MoveId { Idle, Walk, Jump, Punch, Block, Hit, Knockdown }

    // 全是值字段：结构体拷贝 = 独立副本，回滚快照无引用共享问题。
    public struct FighterState
    {
        public int health;
        public int x, y;          // 位置，单位 cm（世界坐标 = cm * 0.01）
        public int vx, vy;        // 速度，cm/帧
        public MoveId move;
        public int moveFrame;     // 当前动作第几帧
        public int hitstun, blockstun, hitstop;
        public bool facingRight;
        public bool onGround;
        public bool hasHitThisMove; // 本招是否已命中（防多段）
    }

    // 用 class（引用）便于整体替换快照；Clone 深拷贝结构体数组。
    public class GameState
    {
        public int frame;
        public FighterState[] fighters = new FighterState[2];
        public uint rng;
        public int roundTimer;

        public GameState Clone()
        {
            var c = new GameState { frame = frame, rng = rng, roundTimer = roundTimer };
            c.fighters = (FighterState[])fighters.Clone(); // 结构体数组 → 逐元素值拷贝
            return c;
        }
    }

    // 招式数据表（数据驱动，不是代码）：调参不改逻辑、确定性、回放可复现。
    public struct MoveDef
    {
        public MoveId id;
        public int startup, active, recovery;
        public int damage, hitstun, blockstun, knockbackX, knockbackY, hitstop;
        public int Total => startup + active + recovery;
    }
}

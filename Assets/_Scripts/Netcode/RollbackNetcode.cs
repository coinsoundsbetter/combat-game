using System;

namespace FightGame
{
    // 自实现的 1v1 回滚引擎（GGPO 思路）：
    //  - 环形缓冲存状态快照 + 两玩家每帧输入
    //  - 输入延迟：本地输入延迟 N 帧进入模拟，给网络 N 帧余量收远端输入
    //  - 预测：远端输入未到时复用"最近一次确认的远端输入"
    //  - 回滚：远端真实输入迟到且与预测不符 → 回到最近确认帧，用正确输入重模拟
    public class RollbackNetcode
    {
        readonly int capacity;
        readonly int inputDelay;
        readonly int maxRollback;

        readonly GameState[] snapshots;     // 每帧状态快照
        readonly InputState[] localInputs;   // 本地输入（按帧）
        readonly InputState[] remoteInputs;  // 远端输入（确认或预测值）
        readonly InputState[] remoteUsed;    // 实际模拟时用的远端值（用于检测纠正）
        readonly bool[] remoteConfirmed;     // 该帧远端输入是否已确认
        readonly bool[] remoteUsedSet;

        public int SimFrame { get; set; } = -1;
        public int RemoteLatestConfirmed { get; private set; } = -1;

        public RollbackNetcode(int inputDelay = 2, int maxRollback = 60, int capacity = 256)
        {
            this.inputDelay = inputDelay;
            this.maxRollback = maxRollback;
            this.capacity = capacity;
            snapshots     = new GameState[capacity];
            localInputs   = new InputState[capacity];
            remoteInputs  = new InputState[capacity];
            remoteUsed    = new InputState[capacity];
            remoteConfirmed = new bool[capacity];
            remoteUsedSet   = new bool[capacity];
        }

        int Idx(int frame) { int i = frame % capacity; return i < 0 ? i + capacity : i; }

        public void SetLocalInput(int frame, InputState input) => localInputs[Idx(frame)] = input;
        public InputState GetLocal(int frame) => localInputs[Idx(frame)];

        public void SetRemoteInput(int frame, InputState input)
        {
            int i = Idx(frame);
            remoteInputs[i] = input;
            remoteConfirmed[i] = true;
            if (frame > RemoteLatestConfirmed) RemoteLatestConfirmed = frame;
        }

        public bool IsConfirmed(int frame) => remoteConfirmed[Idx(frame)];
        public InputState GetRemote(int frame) => remoteInputs[Idx(frame)];
        public InputState GetUsed(int frame) => remoteUsed[Idx(frame)];
        public bool IsUsed(int frame) => remoteUsedSet[Idx(frame)];
        public void SetUsed(int frame, InputState input) { int i = Idx(frame); remoteUsed[i] = input; remoteUsedSet[i] = true; }

        // 预测：未确认则复用最近一次确认的远端输入；都没有则默认（无输入）。
        public InputState PredictRemote(int frame)
        {
            if (IsConfirmed(frame)) return GetRemote(frame);
            for (int f = frame - 1; f >= 0; f--)
                if (IsConfirmed(f)) return GetRemote(f);
            return default;
        }

        public void SaveSnapshot(int frame, GameState st) => snapshots[Idx(frame)] = st.Clone();
        public GameState LoadSnapshot(int frame) => snapshots[Idx(frame)].Clone();

        // 本帧应推进到哪：受输入延迟约束，同时限制预测超前量，避免失控回滚。
        public int TargetFrame(int realTick)
        {
            int byDelay  = realTick - inputDelay;
            int byRemote = RemoteLatestConfirmed + maxRollback;
            return Math.Min(byDelay, byRemote);
        }
    }
}

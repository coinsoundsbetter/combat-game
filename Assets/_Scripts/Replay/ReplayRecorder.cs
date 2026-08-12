using System.Collections.Generic;
using System.IO;

namespace FightGame
{
    // 回放 = 重模拟：录制每帧两玩家的输入，回放时从初始状态重跑同一个 AdvanceFrame。
    // 用 Dictionary 按帧覆盖：回滚重模拟会重复写同一帧，后写覆盖先写，最终留下"确认值"。
    public class ReplayRecorder
    {
        readonly Dictionary<int, (InputState l, InputState r)> map = new Dictionary<int, (InputState, InputState)>();
        public bool Recording { get; private set; }

        public void StartRecording() { map.Clear(); Recording = true; }
        public void Stop() { Recording = false; }

        public void Record(int frame, InputState local, InputState remote)
        {
            if (Recording) map[frame] = (local, remote);
        }

        public void Save(string path)
        {
            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                var keys = new List<int>(map.Keys); keys.Sort();
                w.Write(keys.Count);
                foreach (var k in keys)
                {
                    w.Write(k);
                    w.Write(map[k].l.bits);
                    w.Write(map[k].r.bits);
                }
            }
        }

        public static ReplayRecorder Load(string path)
        {
            var r = new ReplayRecorder();
            using (var fs = File.OpenRead(path))
            using (var rd = new BinaryReader(fs))
            {
                int n = rd.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    int f = rd.ReadInt32();
                    r.map[f] = (new InputState(rd.ReadByte()), new InputState(rd.ReadByte()));
                }
            }
            return r;
        }

        public List<int> SortedFrames() { var k = new List<int>(map.Keys); k.Sort(); return k; }
        public (InputState l, InputState r) Get(int f) => map[f];
        public int Count => map.Count;
    }
}

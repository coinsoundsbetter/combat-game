using System;
using System.IO;
using _Src.GGPO_Extension;
using _Src.GGPO;

namespace _Src.Test {
    /// <summary>
    /// 测试:逻辑层
    /// </summary>
    public class TestSimulator : ISimulation<FighterState, FighterInput> {
        private CoreSetting m_CoreSetting;
        private float m_MoveSpeed = 10f;
        
        //这里的1000是为了浮点转整数,舍弃精度(简化,没做定点数包装)
        public const int PositionUnitsPerWorldUnits = 1000;
        
        public TestSimulator(CoreSetting setting) {
            m_CoreSetting = setting;
        } 
        
        public void Simulate(FighterState[] states, FighterInput[] inputs) {
            //每帧移动量=每秒移动量/每秒帧数(帧率)
            var moveUnitPerTick = (int)Math.Round(m_MoveSpeed * PositionUnitsPerWorldUnits / m_CoreSetting.tickRate);
            for (var i = 0; i < states.Length; i++) {
                states[i].PosX += inputs[i].MoveX * moveUnitPerTick;
            }
        }
        
        public int Load(byte[] buffer, FighterState[] states) {
            using var stream = new MemoryStream(buffer);
            using var reader = new BinaryReader(stream);
            var stateFrame = reader.ReadInt32();
            var playerCount = reader.ReadInt32();

            if (playerCount != states.Length) {
                throw new InvalidDataException("玩家数量不匹配");
            }

            for (var i = 0; i < states.Length; i++) {
                states[i].PosX = reader.ReadInt32();
            }

            return stateFrame;
        }
        
        public GgpoSavedState Save(int frame, FighterState[] states) {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(frame);
            writer.Write(states.Length);

            for (var i = 0; i < states.Length; i++) {
                writer.Write(states[i].PosX);
            }
                
            return new GgpoSavedState(stream.ToArray());
        }

        public uint CalculateChecksum(FighterState[] states) {
            unchecked {
                var hash = 2166136261u;
                for (var i = 0; i < states.Length; i++) {
                    hash = (hash ^ (uint)states[i].PosX) * 16777619u;
                }

                return hash;
            }
        }
    }
}

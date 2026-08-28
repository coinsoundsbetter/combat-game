using System;
using System.Collections.Generic;
using _Code.Simulation;

namespace _Code.Replay {
    /// <summary>
    /// 回放文件的内存模型。回放只保存已经确认的最终输入，不保存预测输入。
    /// </summary>
    [Serializable]
    public sealed class MatchReplay {
        public ReplayHeader Header;
        public readonly List<ReplayFrame> Frames = new List<ReplayFrame>();
        public readonly List<ReplayCheckpoint> Checkpoints = new List<ReplayCheckpoint>();
        public int FinalFrame = -1;
        public bool IsVerified;
    }

    [Serializable]
    public sealed class ReplayHeader {
        public const int CurrentFormatVersion = 1;

        public int FormatVersion = CurrentFormatVersion;
        public string LogicVersion;
        public int TickRate;
        public int PlayerCount;
        public PlayerState[] InitialStates;
    }

    [Serializable]
    public sealed class ReplayFrame {
        public int Frame;
        public FighterInput[] Inputs;
    }

    [Serializable]
    public struct ReplayCheckpoint {
        public int Frame;
        public int Checksum;
    }

    public static class ReplayChecksum {
        public static int Calculate(PlayerState[] playerStates) {
            if (playerStates == null)
                throw new ArgumentNullException(nameof(playerStates));

            unchecked {
                var hash = 17;
                for (var i = 0; i < playerStates.Length; i++) {
                    hash = hash * 31 + playerStates[i].X;
                    hash = hash * 31 + playerStates[i].AttackCount;
                }

                return hash;
            }
        }
    }
}

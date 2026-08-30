/*
using System;
using System.Collections.Generic;
using _Code.Simulation;
using _Src.Test;

namespace _Code.Replay {
    /// <summary>
    /// 不依赖网络的离线回放播放器。每次 AdvanceOneFrame 推进一步固定逻辑帧。
    /// </summary>
    public sealed class ReplayPlayer {
        private readonly MatchReplay m_Replay;
        private readonly FighterState[] m_PlayerStates;
        private readonly Dictionary<int, int> m_Checksums =
            new Dictionary<int, int>();
        private int m_NextFrame;

        public ReplayPlayer(MatchReplay replay) {
            if (replay == null || replay.Header == null)
                throw new ArgumentNullException(nameof(replay));

            m_Replay = replay;
            m_PlayerStates = (FighterState[])replay.Header.InitialStates.Clone();
            for (var i = 0; i < replay.Checkpoints.Count; i++)
                m_Checksums[replay.Checkpoints[i].Frame] = replay.Checkpoints[i].Checksum;
        }

        public FighterState[] PlayerStates {
            get { return m_PlayerStates; }
        }

        public int CurrentFrame {
            get { return m_NextFrame; }
        }

        public int FinalFrame {
            get { return m_Replay.FinalFrame; }
        }

        public bool IsFinished {
            get { return m_NextFrame > m_Replay.FinalFrame; }
        }

        public bool TryAdvanceOneFrame(out string error) {
            error = null;
            if (IsFinished)
                return false;

            var replayFrame = m_Replay.Frames[m_NextFrame];
            if (replayFrame.Frame != m_NextFrame) {
                error = $"Replay frame sequence is invalid at {m_NextFrame}.";
                return false;
            }

            FighterSimulation.SimulateFrame(m_PlayerStates, replayFrame.Inputs);

            int expectedChecksum;
            if (m_Checksums.TryGetValue(m_NextFrame, out expectedChecksum)) {
                var actualChecksum = ReplayChecksum.Calculate(m_PlayerStates);
                if (actualChecksum != expectedChecksum) {
                    error =
                        $"Replay desync at frame {m_NextFrame}. " +
                        $"Expected {expectedChecksum}, got {actualChecksum}.";
                    return false;
                }
            }

            m_NextFrame++;
            return true;
        }
    }
}
*/

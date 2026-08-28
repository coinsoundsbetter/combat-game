using System;
using _Code.Simulation;

namespace _Code.Replay {
    /// <summary>
    /// 将 GGPO 已确认帧转为可持久化的确定性回放。
    /// </summary>
    public sealed class ReplayRecorder {
        private readonly MatchReplay m_Replay;
        private int m_NextFrame;

        public ReplayRecorder(ReplayHeader header) {
            if (header == null)
                throw new ArgumentNullException(nameof(header));
            if (header.PlayerCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(header.PlayerCount));
            if (header.InitialStates == null ||
                header.InitialStates.Length != header.PlayerCount)
                throw new ArgumentException(
                    "Initial states must match the player count.",
                    nameof(header));

            m_Replay = new MatchReplay {
                Header = CloneHeader(header),
            };
        }

        public int LastRecordedFrame {
            get { return m_NextFrame - 1; }
        }

        public void RecordConfirmedFrame(int frame, FighterInput[] inputs) {
            if (frame != m_NextFrame)
                throw new InvalidOperationException(
                    $"Replay frame sequence is invalid. Expected {m_NextFrame}, got {frame}.");
            if (inputs == null || inputs.Length != m_Replay.Header.PlayerCount)
                throw new ArgumentException(
                    "Input count must match the replay player count.",
                    nameof(inputs));

            m_Replay.Frames.Add(new ReplayFrame {
                Frame = frame,
                Inputs = CloneInputs(inputs),
            });
            m_NextFrame++;
        }

        public void RecordCheckpoint(int frame, int checksum) {
            if (frame < 0 || frame >= m_NextFrame)
                throw new ArgumentOutOfRangeException(nameof(frame));

            m_Replay.Checkpoints.Add(new ReplayCheckpoint {
                Frame = frame,
                Checksum = checksum,
            });
        }

        public MatchReplay CreateReplay(int finalFrame, bool isVerified) {
            if (finalFrame < -1 || finalFrame >= m_NextFrame)
                throw new ArgumentOutOfRangeException(nameof(finalFrame));

            var copy = new MatchReplay {
                Header = CloneHeader(m_Replay.Header),
                FinalFrame = finalFrame,
                IsVerified = isVerified,
            };

            for (var i = 0; i <= finalFrame; i++) {
                var frame = m_Replay.Frames[i];
                copy.Frames.Add(new ReplayFrame {
                    Frame = frame.Frame,
                    Inputs = CloneInputs(frame.Inputs),
                });
            }

            for (var i = 0; i < m_Replay.Checkpoints.Count; i++) {
                var checkpoint = m_Replay.Checkpoints[i];
                if (checkpoint.Frame <= finalFrame)
                    copy.Checkpoints.Add(checkpoint);
            }

            return copy;
        }

        private static ReplayHeader CloneHeader(ReplayHeader source) {
            return new ReplayHeader {
                FormatVersion = source.FormatVersion,
                LogicVersion = source.LogicVersion,
                TickRate = source.TickRate,
                PlayerCount = source.PlayerCount,
                InitialStates = (PlayerState[])source.InitialStates.Clone(),
            };
        }

        private static FighterInput[] CloneInputs(FighterInput[] source) {
            var copy = new FighterInput[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }
    }
}

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

    // One queue per fixed player slot.  Session owns all mutations.
    internal sealed class GgpoInputQueue<TInput>
    {
        public readonly GgpoPlayerType Type;
        public readonly int InputDelayFrames;
        public readonly Dictionary<int, TInput> Inputs = new Dictionary<int, TInput>();
        public readonly Dictionary<int, TInput> PredictedInputs = new Dictionary<int, TInput>();
        public readonly Dictionary<int, TInput> UsedInputs = new Dictionary<int, TInput>();

        public int LastLocalSubmittedFrame = -1;
        public int LastConfirmedRemoteFrame;
        public TInput InputBeforeHistory;
        public bool HasInputBeforeHistory;

        public GgpoInputQueue(GgpoPlayerConfig config)
        {
            Type = config.Type;
            InputDelayFrames = config.InputDelayFrames;
            // The pre-delay default-input frames are deterministic and known.
            LastConfirmedRemoteFrame = config.InputDelayFrames - 1;
        }
    }
}

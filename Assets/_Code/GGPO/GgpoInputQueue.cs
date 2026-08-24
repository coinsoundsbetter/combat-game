using System.Collections.Generic;

namespace _Code.GGPO {
    
    public sealed class GgpoInputQueue<TInput> {
        public GgpoPlayerType PlayerType;
        public readonly int InputDelayFrames;
        public readonly Dictionary<int, TInput> Inputs = new Dictionary<int, TInput>();
        public readonly Dictionary<int, TInput> PredictedInputs = new Dictionary<int, TInput>();
        public readonly Dictionary<int, TInput> UsedInputs = new Dictionary<int, TInput>();
        public int LastLocalSubmittedFrame = -1;
        public int LastConfirmedRemoteFrame;
        public TInput InputBeforeHistory;
        public bool HasInputBeforeHistory;

        public GgpoInputQueue(GgpoPlayerType type, int inputDelayFrames) {
            PlayerType = type;
            InputDelayFrames = inputDelayFrames;
        }
    }
}

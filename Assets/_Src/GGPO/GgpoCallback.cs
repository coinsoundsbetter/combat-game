using System;

namespace _Src.GGPO {
    public sealed class GgpoCallback<TInput> {
        public Action OnSessionStarted;
        public Func<int, GgpoSavedState> SaveGameState;
        public Action<byte[]> LoadGameState;
        public Action<int, TInput[]> AdvanceFrame;
    }
}

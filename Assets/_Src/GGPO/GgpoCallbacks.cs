using System;

namespace _Src.GGPO {
    public sealed class GgpoCallback<TInput> {
        public Action OnSessionStarted;
        public Func<int, GgpoSavedState> SaveGameState;
        public Action<byte[]> LoadGameState;
        // The array is ordered by fixed player slot and is reused by the
        // session.  AdvanceFrame must not retain or mutate it.
        public Action<int, TInput[]> AdvanceFrame;
    }
}

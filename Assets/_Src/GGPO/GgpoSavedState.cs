using System;

namespace _Src.GGPO {
    public sealed class GgpoSavedState {
        public byte[] Buffer;

        public GgpoSavedState(byte[] buffer) {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            Buffer = buffer;
        }
    }
}

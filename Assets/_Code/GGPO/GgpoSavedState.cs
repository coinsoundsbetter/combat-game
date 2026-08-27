using System;

namespace _Code.GGPO {
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

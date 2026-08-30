using _Code.Simulation;
using _Src.GGPO;
using _Src.Test;

namespace _Src.Input {
    public sealed class TestInputCodec : IGgpoInputSerializer<FighterInput> {
        public byte[] Encode(FighterInput input) {
            return new[] {
                unchecked((byte)input.MoveX),
                input.Attack ? (byte)1 : (byte)0,
            };
        }

        public bool TryDecode(byte[] bytes, out FighterInput input) {
            input = default(FighterInput);
            if (bytes == null || bytes.Length != 2)
                return false;

            var moveX = unchecked((sbyte)bytes[0]);
            if (moveX < -1 || moveX > 1 || bytes[1] > 1)
                return false;

            input = new FighterInput {
                MoveX = moveX,
                Attack = bytes[1] != 0,
            };
            return true;
        }
    }
}

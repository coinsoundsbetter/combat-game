using System;
using _Src.GGPO;

namespace _Src.Game {
    public struct PlayerInput : IEquatable<PlayerInput> {
        public byte Buttons;
        public sbyte MoveX;

        public bool Equals(PlayerInput other) {
            return Buttons == other.Buttons && MoveX == other.MoveX;
        }
    }

    public sealed class PlayerInputSerializer : IGgpoInputSerializer<PlayerInput> {
        public byte[] Encode(PlayerInput input)
        {
            return new[] { input.Buttons, (byte)input.MoveX };
        }

        public bool TryDecode(byte[] bytes, out PlayerInput input)
        {
            input = default(PlayerInput);

            if (bytes == null || bytes.Length != 2)
                return false;

            input.Buttons = bytes[0];
            input.MoveX = unchecked((sbyte)bytes[1]);
            return true;
        }
    }
}

using System;

namespace _Src.Serialization
{
    /// <summary>
    /// Explicit little-endian primitives for deterministic game-state buffers.
    /// </summary>
    public static class DeterministicBinary
    {
        public static void WriteInt32(byte[] buffer, int offset, int value)
        {
            ValidateRange(buffer, offset, 4);
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        public static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            ValidateRange(buffer, offset, 4);
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        public static void WriteUInt16(byte[] buffer, int offset, ushort value)
        {
            ValidateRange(buffer, offset, 2);
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        public static int ReadInt32(byte[] buffer, int offset)
        {
            ValidateRange(buffer, offset, 4);
            return buffer[offset] |
                   (buffer[offset + 1] << 8) |
                   (buffer[offset + 2] << 16) |
                   (buffer[offset + 3] << 24);
        }

        public static uint ReadUInt32(byte[] buffer, int offset)
        {
            ValidateRange(buffer, offset, 4);
            return (uint)(buffer[offset] |
                          (buffer[offset + 1] << 8) |
                          (buffer[offset + 2] << 16) |
                          (buffer[offset + 3] << 24));
        }

        public static ushort ReadUInt16(byte[] buffer, int offset)
        {
            ValidateRange(buffer, offset, 2);
            return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
        }

        // FNV-1a is a deterministic desync diagnostic, not cryptography.
        public static ulong CalculateChecksum(byte[] buffer)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            uint hash = 2166136261;
            foreach (var value in buffer)
            {
                hash ^= value;
                hash *= 16777619;
            }
            return hash;
        }

        private static void ValidateRange(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset > buffer.Length - count)
                throw new ArgumentOutOfRangeException(nameof(offset));
        }
    }
}

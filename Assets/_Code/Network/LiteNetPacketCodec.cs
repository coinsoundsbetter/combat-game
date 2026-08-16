using GLMFighter.Core;
using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;

namespace GLMFighter.Network
{
    public static class LiteNetPacketCodec
    {
        public static void WriteStartBattle(NetDataWriter writer, int assignedPlayerIndex)
        {
            writer.Put((byte)TransportPacketType.StartBattle);
            writer.Put(assignedPlayerIndex);
        }

        public static void WriteAssignPlayer(NetDataWriter writer, int assignedPlayerIndex)
        {
            writer.Put((byte)TransportPacketType.AssignPlayer);
            writer.Put(assignedPlayerIndex);
        }

        public static void WriteLobbyState(NetDataWriter writer, int characterIndex, bool ready)
        {
            writer.Put((byte)TransportPacketType.LobbyState);
            writer.Put(characterIndex);
            writer.Put(ready ? (byte)1 : (byte)0);
        }

        public static void WriteInput(NetDataWriter writer, int frame, FighterInput input)
        {
            writer.Put((byte)TransportPacketType.Input);
            writer.Put(frame);
            WriteInputPayload(writer, input);
        }

        public static void WriteInputBundle(NetDataWriter writer, IList<InputFrameData> inputs)
        {
            writer.Put((byte)TransportPacketType.InputBundle);
            writer.Put(inputs.Count);

            for (int i = 0; i < inputs.Count; i++)
            {
                writer.Put(inputs[i].Frame);
                WriteInputPayload(writer, inputs[i].Input);
            }
        }

        public static void WriteChecksum(NetDataWriter writer, int frame, int checksum)
        {
            writer.Put((byte)TransportPacketType.Checksum);
            writer.Put(frame);
            writer.Put(checksum);
        }

        public static TransportPacket ReadPacket(NetPacketReader reader)
        {
            TransportPacketType type = (TransportPacketType)reader.GetByte();

            switch (type)
            {
                case TransportPacketType.StartBattle:
                    return TransportPacket.StartBattle(reader.GetInt());

                case TransportPacketType.AssignPlayer:
                    return TransportPacket.AssignPlayer(reader.GetInt());

                case TransportPacketType.LobbyState:
                    return TransportPacket.LobbyState(reader.GetInt(), reader.GetByte() != 0);

                case TransportPacketType.Input:
                    return TransportPacket.InputPacket(reader.GetInt(), ReadInputPayload(reader));

                case TransportPacketType.InputBundle:
                    return TransportPacket.InputBundle(ReadInputBundle(reader));

                case TransportPacketType.Checksum:
                    return TransportPacket.ChecksumPacket(reader.GetInt(), reader.GetInt());

                default:
                    return new TransportPacket { Type = TransportPacketType.None };
            }
        }

        private static InputFrameData[] ReadInputBundle(NetPacketReader reader)
        {
            int count = reader.GetInt();

            if (count < 0 || count > 64)
            {
                return new InputFrameData[0];
            }

            InputFrameData[] inputs = new InputFrameData[count];

            for (int i = 0; i < count; i++)
            {
                inputs[i] = new InputFrameData(reader.GetInt(), ReadInputPayload(reader));
            }

            return inputs;
        }

        private static FighterInput ReadInputPayload(NetPacketReader reader)
        {
            return new FighterInput
            {
                Horizontal = reader.GetInt(),
                Jump = reader.GetByte() != 0,
                Crouch = reader.GetByte() != 0,
                Light = reader.GetByte() != 0,
                Heavy = reader.GetByte() != 0,
                Guard = reader.GetByte() != 0
            };
        }

        private static void WriteInputPayload(NetDataWriter writer, FighterInput input)
        {
            writer.Put(input.Horizontal);
            writer.Put(input.Jump ? (byte)1 : (byte)0);
            writer.Put(input.Crouch ? (byte)1 : (byte)0);
            writer.Put(input.Light ? (byte)1 : (byte)0);
            writer.Put(input.Heavy ? (byte)1 : (byte)0);
            writer.Put(input.Guard ? (byte)1 : (byte)0);
        }

    }
}

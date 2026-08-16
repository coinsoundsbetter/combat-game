using GLMFighter.Core;

namespace GLMFighter.Network
{
    public enum TransportPacketType
    {
        None = 0,
        StartBattle = 1,
        Input = 2,
        InputBundle = 3,
        Checksum = 4,
        AssignPlayer = 5,
        LobbyState = 6
    }

    public struct InputFrameData
    {
        public int Frame;
        public FighterInput Input;

        public InputFrameData(int frame, FighterInput input)
        {
            Frame = frame;
            Input = input;
        }
    }

    public struct TransportPacket
    {
        public TransportPacketType Type;
        public int AssignedPlayerIndex;
        public int InputFrame;
        public FighterInput Input;
        public InputFrameData[] InputFrames;
        public int ChecksumFrame;
        public int ChecksumValue;
        public int CharacterIndex;
        public bool Ready;

        public static TransportPacket StartBattle(int assignedPlayerIndex)
        {
            return new TransportPacket
            {
                Type = TransportPacketType.StartBattle,
                AssignedPlayerIndex = assignedPlayerIndex
            };
        }

        public static TransportPacket InputPacket(int frame, FighterInput input)
        {
            return new TransportPacket
            {
                Type = TransportPacketType.Input,
                InputFrame = frame,
                Input = input
            };
        }

        public static TransportPacket InputBundle(InputFrameData[] inputFrames)
        {
            return new TransportPacket
            {
                Type = TransportPacketType.InputBundle,
                InputFrames = inputFrames
            };
        }

        public static TransportPacket ChecksumPacket(int frame, int checksum)
        {
            return new TransportPacket
            {
                Type = TransportPacketType.Checksum,
                ChecksumFrame = frame,
                ChecksumValue = checksum
            };
        }

        public static TransportPacket AssignPlayer(int assignedPlayerIndex)
        {
            return new TransportPacket
            {
                Type = TransportPacketType.AssignPlayer,
                AssignedPlayerIndex = assignedPlayerIndex
            };
        }

        public static TransportPacket LobbyState(int characterIndex, bool ready)
        {
            return new TransportPacket
            {
                Type = TransportPacketType.LobbyState,
                CharacterIndex = characterIndex,
                Ready = ready
            };
        }
    }
}

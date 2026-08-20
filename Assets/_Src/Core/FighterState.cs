namespace _Src.Core
{
    public struct FighterState
    {
        public int PositionX;
        public int PositionY;
        public int VelocityX;
        public int VelocityY;
        public int Health;
        public int Energy;
        public int Facing;

        public FighterState Clone()
        {
            return new FighterState();
        }
    }
}

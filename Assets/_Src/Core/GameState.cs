namespace _Src.Core
{
    public struct GameState
    {
        public int SimulationFrame;
        public int RoundTimer;
        public uint RandomSeed;
        
        public GameState Clone()
        {
            return new GameState();
        }
    }
}
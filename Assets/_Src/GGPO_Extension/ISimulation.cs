using _Src.GGPO;

namespace _Src.GGPO_Extension {
    public interface ISimulation<TPlayerState, TInput> {
        void Simulate(TPlayerState[] states, TInput[] inputs);
        int Load(byte[] buffer, TPlayerState[] states);
        GgpoSavedState Save(int frame, TPlayerState[] states);
        uint CalculateChecksum(TPlayerState[] states);
    } 
}

namespace _Src.GGPO {
    public interface IGgpoInputProvider<T> {
        T ReadInput(int playerIndex);
    }
}

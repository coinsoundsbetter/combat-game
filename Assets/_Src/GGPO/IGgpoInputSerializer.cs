namespace _Src.GGPO {
    public interface IGgpoInputSerializer<TInput> {
        byte[] Encode(TInput input);
        bool TryDecode(byte[] encoded, out TInput input);
    }
}

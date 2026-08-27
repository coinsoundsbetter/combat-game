namespace _Code.GGPO {
    /// <summary>将确定性输入编码为可在网络上传输的字节。</summary>
    public interface IGgpoInputSerializer<TInput> {
        byte[] Encode(TInput input);
        bool TryDecode(byte[] bytes, out TInput input);
    }
}

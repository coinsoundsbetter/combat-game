namespace _Src.GGPO_Extension {

    public enum NetPlayerType {
        Local,
        Remote,
    }

    [System.Serializable]
    public class CoreSetting {
        public int tickRate = 60;
        public int inputDelayFrames = 1;
        public int maxPlayerCount = 2;
        public int maxRollbackFrames = 8;
        public int maxTickPerUpdate = 8;
        public int maxStateHistoryFrames = 64;
        // 0=关闭；大于0时每帧回退指定距离并重演，用于检测非确定性状态。
        public int syncTestRollbackFrames = 0;
        // 握手完成后、正式推进逻辑前的一段真实时间(秒)。这段时间只收发网络包、
        // 不推进逻辑，用来让两端几乎同时从状态帧 0 起步并交换首批输入，
        // 避免先启动的一端用默认输入预测后启动的一端造成开局移动跳变。
        public float startGraceSeconds = 1f;
    }

    public class PlayerRegistration {
        public int Index;
        public NetPlayerType NetType;
    }
}

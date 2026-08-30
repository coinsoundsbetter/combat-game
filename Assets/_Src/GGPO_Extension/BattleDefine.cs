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
    }

    public class PlayerRegistration {
        public int Index;
        public NetPlayerType NetType;
    }
}

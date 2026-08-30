namespace _Src.GGPO {
    public sealed class GgpoTimeSync {
        private float m_LocalAdvantage;
        private float m_RemoteAdvantage;
        private int m_Cooldown;
        public float LocalAdvantage => m_LocalAdvantage;
        public float RemoteAdvantage => m_RemoteAdvantage;

        public void OnRemoteFrame(int localFrame, int remoteFrame, float remoteReportedAdvantage) {
            float observedAdvantage = localFrame - remoteFrame;
            m_LocalAdvantage = m_LocalAdvantage * 0.9f + observedAdvantage * 0.1f;
            m_RemoteAdvantage = m_RemoteAdvantage * 0.9f + remoteReportedAdvantage * 0.1f;
        }

        public bool ShouldWait() {
            if (m_Cooldown > 0) {
                m_Cooldown--;
                return false;
            }

            bool bothSidesAgree = m_LocalAdvantage > 3.0f && m_RemoteAdvantage < -3.0f;
            if (!bothSidesAgree) {
                return false;
            }

            m_Cooldown = 10;
            return true;
        }
    }
}

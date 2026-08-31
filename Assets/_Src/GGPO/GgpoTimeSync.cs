using System;

namespace _Src.GGPO {
    /// <summary>
    /// 时间同步只调整逻辑帧节奏，不进入游戏状态，也不参与回滚或校验。
    /// 双方都确认本机领先时，本机才会分散地少推进几个逻辑帧。
    /// </summary>
    public sealed class GgpoTimeSync {
        private const float SmoothingFactor = 0.1f;
        private const float WaitThresholdFrames = 2f;
        private const int MinimumSamples = 15;
        private const int MaximumWaitFrames = 3;
        private const int WaitSpacingFrames = 2;
        private const int WaitCooldownFrames = 30;

        private int m_SampleCount;
        private int m_PendingWaitFrames;
        private int m_WaitSpacingRemaining;
        private int m_CooldownRemaining;

        public float LocalAdvantage { get; private set; }
        public float RemoteAdvantage { get; private set; }
        public int SampleCount => m_SampleCount;

        public void AddSample(int localFrame, GgpoTimeSyncSample sample) {
            var observedLocalAdvantage = Clamp(
                localFrame - sample.RemoteFrame,
                -30f,
                30f);
            var observedRemoteAdvantage = Clamp(
                sample.RemoteFrameAdvantage,
                -30f,
                30f);

            if (m_SampleCount == 0) {
                LocalAdvantage = observedLocalAdvantage;
                RemoteAdvantage = observedRemoteAdvantage;
            }
            else {
                LocalAdvantage = Lerp(
                    LocalAdvantage,
                    observedLocalAdvantage,
                    SmoothingFactor);
                RemoteAdvantage = Lerp(
                    RemoteAdvantage,
                    observedRemoteAdvantage,
                    SmoothingFactor);
            }

            m_SampleCount++;
        }

        /// <summary>
        /// 每次准备推进正常逻辑帧前调用。返回 true 时，本次只等待模拟，
        /// 但仍应继续收发网络包和执行已经需要的回滚。
        /// </summary>
        public bool TryConsumeWait() {
            if (m_CooldownRemaining > 0)
                m_CooldownRemaining--;

            // 帧差已经恢复时，不继续消费旧的等待建议。
            if (LocalAdvantage < 1f) {
                m_PendingWaitFrames = 0;
                m_WaitSpacingRemaining = 0;
            }

            if (m_PendingWaitFrames > 0) {
                if (m_WaitSpacingRemaining > 0) {
                    m_WaitSpacingRemaining--;
                    return false;
                }

                m_PendingWaitFrames--;
                m_WaitSpacingRemaining = WaitSpacingFrames;
                return true;
            }

            if (m_SampleCount < MinimumSamples || m_CooldownRemaining > 0)
                return false;

            // 普通传输延迟会令双方都短暂认为自己领先。只有本机认为
            // 自己领先、同时远端认为自己落后，才是真正的模拟帧漂移。
            var bothSidesAgreeLocalIsAhead =
                LocalAdvantage >= WaitThresholdFrames &&
                RemoteAdvantage <= -WaitThresholdFrames;
            if (!bothSidesAgreeLocalIsAhead)
                return false;

            var recommendedWaitCount = (int)Math.Floor(
                (LocalAdvantage - 1f) / 2f);
            recommendedWaitCount = Clamp(
                recommendedWaitCount,
                1,
                MaximumWaitFrames);

            // 当前调用立即等待一次，其余建议分散执行，避免连续停顿。
            m_PendingWaitFrames = recommendedWaitCount - 1;
            m_WaitSpacingRemaining = WaitSpacingFrames;
            m_CooldownRemaining = WaitCooldownFrames;
            return true;
        }

        private static float Lerp(float from, float to, float amount) {
            return from + (to - from) * amount;
        }

        private static float Clamp(float value, float minimum, float maximum) {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static int Clamp(int value, int minimum, int maximum) {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}

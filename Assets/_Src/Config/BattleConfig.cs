using UnityEngine;

namespace _Src.Config {
    
    [CreateAssetMenu(menuName = "GGPO_Practice", fileName = "BattleConfig")]
    public class BattleConfig : ScriptableObject {
        /// <summary>
        /// 逻辑更新频率
        /// </summary>
        public float tickRate = 1f / 60f;
        
        /// <summary>
        /// 渲染更新频率
        /// </summary>
        public float rendererRate = 1f / 120f;
        
        /// <summary>
        /// 最大可回滚历史窗口
        /// </summary>
        public int maxRollbackFrames = 8;
        
        /// <summary>
        /// 默认输入延迟
        /// </summary>
        public int inputDelayFrames = 2;
        
        /// <summary>
        /// 逻辑Tick由Unity的Update()驱动
        /// 因此我们需要限制一个单帧最大Tick数,防止因卡顿导致越来越卡
        /// </summary>
        public int maxTickPerUnityUpdate = 8;
        
        /// <summary>
        /// 战斗中有多少玩家
        /// </summary>
        public int playerNum = 2;
    }
}
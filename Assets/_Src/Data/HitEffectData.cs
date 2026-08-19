using UnityEngine;

namespace _Src.Data
{
    /// <summary>
    /// 命中效果定义
    /// </summary>
    public class HitEffectData : ScriptableObject
    {
        public int damage;
        public int hitstun;
        public int blockstun;
        public int hitstop;
        public Vector2 pushback;
    }
}
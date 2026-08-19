using System;
using UnityEngine;

namespace _Src.Data
{
    /// <summary>
    /// 动作
    /// </summary>
    public class ActionData : ScriptableObject
    {
        public string actionId;
        public int totalFrames;
        public bool isLoop;
        public ActionFrameData[] frames;
    }

    /// <summary>
    /// 动作关键帧发生的事情
    /// </summary>
    [System.Serializable]
    public class ActionFrameData
    {
        public Vector2 movementDelta;
        public HurtBox hurtBox;
        public HitBox[] hitBoxes;
    }

    [System.Serializable]
    public class HurtBox
    {
        public Box2d box;
    }

    [System.Serializable]
    public class HitBox
    {
        public Box2d box;
        public HitEffectData hitEffect;
    }
}
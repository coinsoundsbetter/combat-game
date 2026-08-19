using UnityEngine;

namespace _Src.Data
{
    /// <summary>
    /// 定义一个角色基础的数据
    /// </summary>
    public class FighterDefineSO : ScriptableObject
    {
        public Box2d standBox;
        public Box2d crouchBox;
    }
}

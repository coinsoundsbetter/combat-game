using _Src.GGPO_Extension;
using UnityEngine;

namespace _Src.Config {
    
    [CreateAssetMenu(menuName = "GGPO_Practice", fileName = "BattleConfig")]
    public class BattleConfig : ScriptableObject {
        public CoreSetting coreSetting;
    }
}
using _Src.GGPO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace _Src.Test {
    public class TestInput : IGgpoInputProvider<FighterInput> {

        public FighterInput ReadInput(int playerIndex) {
            var res = new  FighterInput();
            var keyboard = Keyboard.current;
            if (keyboard == null) {
                return default;
            }
            
            switch (playerIndex) {
                case 0: // P1
                    res.MoveX = keyboard.aKey.isPressed ? -1 :
                        keyboard.dKey.isPressed ? 1 :
                        0;
                    break;

                case 1: // P2
                    res.MoveX = keyboard.leftArrowKey.isPressed ? -1 :
                        keyboard.rightArrowKey.isPressed ? 1 :
                        0;
                    break;
                default:
                    Debug.LogError($"未定义 P{playerIndex} 的输入映射");
                    break;
            }
            
            return res;
        }
    }
}

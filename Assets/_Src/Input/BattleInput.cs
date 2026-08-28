using _Code.Simulation;
using UnityEngine.InputSystem;

namespace _Src.Input {
    public class BattleInput {
        
        public FighterInput ReadInput(int playerIndex) {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return default(FighterInput);

            if (playerIndex == 1) {
                return new FighterInput {
                    MoveX =
                        keyboard.leftArrowKey.isPressed ? -1 :
                        keyboard.rightArrowKey.isPressed ? 1 :
                        0,
                    Attack = keyboard.numpad1Key.wasPressedThisFrame,
                };
            }

            return new FighterInput {
                MoveX =
                    keyboard.aKey.isPressed ? -1 :
                    keyboard.dKey.isPressed ? 1 :
                    0,
                Attack = keyboard.jKey.wasPressedThisFrame,
            };
        }
    }
}
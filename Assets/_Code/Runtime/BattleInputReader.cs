using GLMFighter.Core;
using UnityEngine;

namespace GLMFighter.Runtime
{
    /// <summary>
    /// Temporary keyboard input mapping. Replacing this later does not affect
    /// simulation, input buffering, or network transport.
    /// </summary>
    public static class BattleInputReader
    {
        public static FighterInput ReadPlayerOne()
        {
            int horizontal = 0;

            if (Input.GetKey(KeyCode.A))
            {
                horizontal--;
            }

            if (Input.GetKey(KeyCode.D))
            {
                horizontal++;
            }

            return new FighterInput
            {
                Horizontal = horizontal,
                Jump = Input.GetKey(KeyCode.W),
                Crouch = Input.GetKey(KeyCode.S),
                Guard = Input.GetKey(KeyCode.L),
                Light = Input.GetKey(KeyCode.J)
            };
        }

        public static FighterInput ReadPlayerTwo()
        {
            int horizontal = 0;

            if (Input.GetKey(KeyCode.LeftArrow))
            {
                horizontal--;
            }

            if (Input.GetKey(KeyCode.RightArrow))
            {
                horizontal++;
            }

            return new FighterInput
            {
                Horizontal = horizontal,
                Jump = Input.GetKey(KeyCode.UpArrow),
                Crouch = Input.GetKey(KeyCode.DownArrow),
                Guard = Input.GetKey(KeyCode.RightShift),
                Light = Input.GetKey(KeyCode.Keypad1) || Input.GetKey(KeyCode.N)
            };
        }

        public static string Format(FighterInput input)
        {
            string horizontal = input.Horizontal < 0 ? "Left" : input.Horizontal > 0 ? "Right" : "Neutral";
            return horizontal +
                   " J:" + input.Jump +
                   " C:" + input.Crouch +
                   " L:" + input.Light +
                   " G:" + input.Guard;
        }
    }
}

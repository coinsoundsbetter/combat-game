using System;

namespace _Code.Simulation {
    /// <summary>
    /// 不依赖 Unity 的确定性格斗逻辑入口。
    /// 回滚重演与正常帧推进都会调用这里。
    /// </summary>
    public static class FighterSimulation {
        public static void SimulateFrame(
            PlayerState[] playerStates,
            FighterInput[] playerInputs) {
            if (playerStates == null)
                throw new ArgumentNullException(nameof(playerStates));
            if (playerInputs == null)
                throw new ArgumentNullException(nameof(playerInputs));
            if (playerStates.Length != playerInputs.Length)
                throw new ArgumentException(
                    "Player state and input counts must match.",
                    nameof(playerInputs));

            for (var playerIndex = 0; playerIndex < playerStates.Length; playerIndex++)
                ApplyInput(ref playerStates[playerIndex], playerInputs[playerIndex]);
        }

        private static void ApplyInput(ref PlayerState state, FighterInput input) {
            state.X += input.MoveX;

            if (input.Attack)
                state.AttackCount++;
        }
    }
}

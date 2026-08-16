using System;
using GLMFighter.Core;

namespace GLMFighter.Runtime
{
    /// <summary>
    /// Converts render time into deterministic simulation ticks. A blocked
    /// network tick still consumes its scheduled slot, matching the prototype's
    /// existing delay-based behavior.
    /// </summary>
    public sealed class BattleTickDriver
    {
        private float _accumulator;

        public void Reset()
        {
            _accumulator = 0f;
        }

        public void Advance(float deltaTime, Action tick)
        {
            _accumulator += deltaTime;
            float frameDuration = 1f / BattleSimulation.FramesPerSecond;

            while (_accumulator >= frameDuration)
            {
                if (tick != null)
                {
                    tick();
                }

                _accumulator -= frameDuration;
            }
        }
    }
}

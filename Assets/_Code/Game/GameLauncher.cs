using Framework;
using Gameplay;
using UnityEngine;

namespace Game
{

    public class GameLauncher : MonoBehaviour
    {
        private PhaseMachine _phase;

        void Start()
        {
            _phase = new PhaseMachine();
            _phase.ChangeTo(new BootPhase(_phase));
        }

        void OnDestroy()
        {
            _phase.Shutdown();
        }

        void Update()
        {
            _phase.Update(Time.deltaTime);
        }
    }
}
using Framework;
using UnityEngine.SceneManagement;

namespace Gameplay
{
    public class MenuPhase : IPhase
    {
        private PhaseMachine m_machine;

        public MenuPhase(PhaseMachine machine)
        {
            m_machine = machine;
        }

        public void Enter()
        {
            SceneManager.LoadSceneAsync("Menu", LoadSceneMode.Additive);
        }

        public void Exit()
        {
            
        }

        public void Update(float dt)
        {
            
        }
    }
}


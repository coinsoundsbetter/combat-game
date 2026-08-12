using Framework;

namespace Gameplay
{
    public class BootPhase : IPhase
    {
        private PhaseMachine m_machine;
        private bool m_isReady;

        public BootPhase(PhaseMachine machine)
        {
            m_machine = machine;
        }


        public void Enter()
        {
            //todo:注册UI、音效、网络等全局服务
            m_isReady = true;
        }

        public void Exit()
        {

        }

        public void Update(float dt)
        {
            if (m_isReady)
            {
                m_machine.ChangeTo(new MenuPhase(m_machine));
            }
        }
    }
}


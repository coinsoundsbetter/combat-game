
namespace Framework
{
    public interface IPhase
    {
        void Enter();
        void Exit();
        void Update(float dt);
    }

    public class PhaseMachine
    {
        private IPhase _current;
        public void ChangeTo(IPhase next) { _current?.Exit(); _current = next; _current.Enter(); }
        public void Update(float dt) => _current?.Update(dt);
        public void Shutdown()
        {
            _current?.Exit();
            _current = null;
        }
    }
}
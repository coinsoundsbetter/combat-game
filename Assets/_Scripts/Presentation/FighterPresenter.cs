using UnityEngine;

namespace FightGame
{
    // 表现层：只读 GameState，驱动 3D 模型 Transform + Animator。
    // 关键约束：只对"最终落定"的状态做差分反应，绝不在回滚重模拟的中间帧触发特效。
    public class FighterPresenter : MonoBehaviour
    {
        public Animator animator;
        [HideInInspector] public int playerId;

        GameManager gm;
        string curState;

        public void Init(GameManager gm, int id) { this.gm = gm; playerId = id; }

        void Update()
        {
            if (gm == null || gm.State == null) return;
            var fs = gm.State.fighters[playerId];

            // 逻辑坐标(cm) → 世界坐标(m)
            transform.position = new Vector3(fs.x * 0.01f, fs.y * 0.01f, 0f);
            transform.rotation = Quaternion.Euler(0, fs.facingRight ? 0 : 180, 0);

            // 动画按"动作名"切换，不用 Animator 时间轴驱动战斗逻辑（非确定性）。
            string state = AnimName(fs.move);
            if (state != curState)
            {
                if (animator != null) animator.Play(state, 0, 0f);
                curState = state;
            }
        }

        string AnimName(MoveId m)
        {
            switch (m)
            {
                case MoveId.Walk:  return "Walk";
                case MoveId.Jump:  return "Jump";
                case MoveId.Punch: return "Punch";
                case MoveId.Block: return "Block";
                case MoveId.Hit:   return "Hit";
                default:           return "Idle";
            }
        }
    }
}

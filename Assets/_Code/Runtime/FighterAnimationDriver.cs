using GLMFighter.Core;
using UnityEngine;

namespace GLMFighter.Runtime
{
    [DisallowMultipleComponent]
    public sealed class FighterAnimationDriver : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private float transitionDuration = 0.05f;

        [Header("State Names")]
        [SerializeField] private string idleState = "Idle";
        [SerializeField] private string walkForwardState = "WalkForward";
        [SerializeField] private string walkBackwardState = "WalkBackward";
        [SerializeField] private string jumpStartupState = "Jump";
        [SerializeField] private string jumpState = "Jump";
        [SerializeField] private string fallState = "Jump";
        [SerializeField] private string landingState = "Jump";
        [SerializeField] private string guardState = "Guard";
        [SerializeField] private string crouchState = "Crouch";
        [SerializeField] private string lightAttackState = "LightAttack";
        [SerializeField] private string hitstunState = "Hitstun";
        [SerializeField] private string blockstunState = "Defense";
        [SerializeField] private string koState = "KO";

        private string _currentState;

        public void BindAnimator(Animator targetAnimator)
        {
            if (animator == null)
            {
                animator = targetAnimator;
            }
        }

        public void Apply(FighterState state)
        {
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                return;
            }

            string stateName = ResolveStateName(state);
            CombatMoveData moveData = ResolveMoveData(state);

            if (moveData.HasFrames)
            {
                PlayMoveFrame(stateName, moveData, state.MotionFrame);
            }
            else
            {
                if (_timelineLocked)
                {
                    animator.speed = 1f;
                    _timelineLocked = false;
                }

                Play(stateName);
            }
        }

        private bool _timelineLocked;

        private string ResolveStateName(FighterState state)
        {
            if (state.Phase == FighterPhase.KO || state.Health <= 0)
            {
                return koState;
            }

            if (state.Phase == FighterPhase.Hitstun)
            {
                return hitstunState;
            }

            if (state.Phase == FighterPhase.Blockstun)
            {
                return blockstunState;
            }

            if (state.CurrentAttack == AttackKind.Light)
            {
                return lightAttackState;
            }

            if (state.Phase == FighterPhase.Guard)
            {
                return guardState;
            }

            if (state.Phase == FighterPhase.Crouch)
            {
                return crouchState;
            }

            if (state.Phase == FighterPhase.JumpStartup)
            {
                return jumpStartupState;
            }

            if (state.Phase == FighterPhase.Jump)
            {
                return jumpState;
            }

            if (state.Phase == FighterPhase.Fall)
            {
                return fallState;
            }

            if (state.Phase == FighterPhase.Landing)
            {
                return landingState;
            }

            if (state.Phase == FighterPhase.Walk)
            {
                int moveDirection = state.Velocity.X * state.Facing > 0 ? 1 : -1;
                return moveDirection > 0 ? walkForwardState : walkBackwardState;
            }

            return idleState;
        }

        private static CombatMoveData ResolveMoveData(FighterState state)
        {
            switch (state.Phase)
            {
                case FighterPhase.Idle:
                    return state.RoleStats.IdleMove;
                case FighterPhase.Walk:
                    return state.Velocity.X * state.Facing >= 0
                        ? state.RoleStats.WalkForwardMove
                        : state.RoleStats.WalkBackwardMove;
                case FighterPhase.Guard:
                    return state.RoleStats.GuardMove;
                case FighterPhase.Crouch:
                    return state.RoleStats.CrouchMove;
                case FighterPhase.JumpStartup:
                case FighterPhase.Jump:
                case FighterPhase.Fall:
                case FighterPhase.Landing:
                    return state.RoleStats.JumpMove;
                case FighterPhase.AttackStartup:
                case FighterPhase.AttackActive:
                case FighterPhase.AttackRecovery:
                    return state.CurrentAttack == AttackKind.Light
                        ? state.RoleStats.LightAttackMove
                        : new CombatMoveData();
                case FighterPhase.Hitstun:
                    return state.RoleStats.HitstunMove;
                case FighterPhase.Blockstun:
                    return state.RoleStats.BlockstunMove;
                case FighterPhase.KO:
                    return state.RoleStats.KOMove;
                default:
                    return new CombatMoveData();
            }
        }

        private void PlayMoveFrame(string stateName, CombatMoveData moveData, int motionFrame)
        {
            if (string.IsNullOrEmpty(stateName))
            {
                return;
            }

            animator.speed = 0f;
            _timelineLocked = true;

            int frame = moveData.TotalFrames <= 0
                ? 0
                : Mathf.Clamp(motionFrame, 0, moveData.TotalFrames - 1);
            float normalizedTime = moveData.Loop && moveData.TotalFrames > 1
                ? frame / (float)moveData.TotalFrames
                : frame / (float)Mathf.Max(1, moveData.TotalFrames - 1);

            animator.Play(stateName, 0, normalizedTime);
            animator.Update(0f);
            _currentState = stateName;
        }

        private void Play(string stateName)
        {
            if (string.IsNullOrEmpty(stateName) || _currentState == stateName)
            {
                return;
            }

            animator.CrossFade(stateName, transitionDuration, 0);
            _currentState = stateName;
        }
    }
}

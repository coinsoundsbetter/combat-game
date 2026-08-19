using System;

namespace GLMFighter.Src.Core
{
    /// <summary>
    /// Serializable gameplay state. Keeping all mutable state here makes the
    /// machine easy to snapshot for deterministic replay and rollback later.
    /// </summary>
    public struct FighterStateSnapshot
    {
        public FighterStateId State;
        public FighterStateId PreviousState;
        public FighterAttackKind CurrentAttack;
        public int StateFrame;
        public int StateTimer;
        public int Health;
        public int MaxHealth;
        public int Facing;
        public int PositionX;
        public int PositionY;
        public int VelocityX;
        public int VelocityY;
        public bool OnGround;
        public bool AttackHasHit;
    }

    /// <summary>
    /// First-step fighter FSM. It owns state transitions and state-local
    /// timers, but deliberately does not know about Unity, animation, or
    /// collision boxes.
    /// </summary>
    public sealed class FighterStateMachine
    {
        private const int JumpStartupFrames = 3;
        private const int JumpVelocity = 12;
        private const int Gravity = 1;
        private const int LightStartupFrames = 5;
        private const int LightActiveFrames = 3;
        private const int LightRecoveryFrames = 12;
        private const int HeavyStartupFrames = 10;
        private const int HeavyActiveFrames = 4;
        private const int HeavyRecoveryFrames = 20;

        private FighterStateSnapshot _state;
        private readonly int _walkSpeed;

        public FighterStateMachine(int maxHealth, int walkSpeed)
        {
            if (maxHealth <= 0)
            {
                throw new ArgumentOutOfRangeException("maxHealth");
            }

            if (walkSpeed < 0)
            {
                throw new ArgumentOutOfRangeException("walkSpeed");
            }

            _walkSpeed = walkSpeed;
            Reset(maxHealth, 1);
        }

        public FighterStateSnapshot State
        {
            get { return _state; }
        }

        public bool IsKO
        {
            get { return _state.State == FighterStateId.KO || _state.Health <= 0; }
        }

        public bool CanAcceptCommand
        {
            get
            {
                return _state.State == FighterStateId.Idle ||
                       _state.State == FighterStateId.Walk ||
                       _state.State == FighterStateId.Crouch ||
                       _state.State == FighterStateId.Guard;
            }
        }

        public void Reset(int maxHealth, int facing)
        {
            _state = new FighterStateSnapshot
            {
                State = FighterStateId.Idle,
                PreviousState = FighterStateId.Idle,
                CurrentAttack = FighterAttackKind.None,
                StateFrame = 0,
                StateTimer = 0,
                Health = maxHealth,
                MaxHealth = maxHealth,
                Facing = facing < 0 ? -1 : 1,
                PositionX = 0,
                PositionY = 0,
                VelocityX = 0,
                VelocityY = 0,
                OnGround = true,
                AttackHasHit = false
            };
        }

        public void Restore(FighterStateSnapshot snapshot)
        {
            if (snapshot.MaxHealth <= 0)
            {
                throw new ArgumentException("Snapshot must contain a positive MaxHealth.", "snapshot");
            }

            _state = snapshot;
        }

        public void Tick(FighterCommand rawCommand)
        {
            FighterCommand command = rawCommand.Normalized();

            if (IsKO)
            {
                _state.VelocityX = 0;
                _state.VelocityY = 0;
                _state.StateFrame++;
                return;
            }

            switch (_state.State)
            {
                case FighterStateId.Idle:
                case FighterStateId.Walk:
                case FighterStateId.Crouch:
                case FighterStateId.Guard:
                    TickGrounded(command);
                    break;

                case FighterStateId.JumpStartup:
                case FighterStateId.Jump:
                case FighterStateId.Fall:
                    TickAirborne();
                    break;

                case FighterStateId.AttackStartup:
                case FighterStateId.AttackActive:
                case FighterStateId.AttackRecovery:
                    TickAttack();
                    break;

                case FighterStateId.Hitstun:
                case FighterStateId.Blockstun:
                case FighterStateId.Knockdown:
                    TickReaction();
                    break;
            }

            if (_state.State != FighterStateId.KO)
            {
                _state.StateFrame++;
            }
        }

        /// <summary>
        /// Applies a hit result. Collision detection will call this later.
        /// The blocked argument is a gameplay result, not an animation event.
        /// </summary>
        public bool ReceiveHit(int damage, int hitstunFrames, int blockstunFrames, bool blocked)
        {
            if (IsKO)
            {
                return false;
            }

            _state.CurrentAttack = FighterAttackKind.None;
            _state.AttackHasHit = false;

            if (blocked)
            {
                EnterState(FighterStateId.Blockstun, Math.Max(1, blockstunFrames));
                return true;
            }

            _state.Health = Math.Max(0, _state.Health - Math.Max(0, damage));
            if (_state.Health == 0)
            {
                EnterState(FighterStateId.KO, 0);
            }
            else
            {
                EnterState(FighterStateId.Hitstun, Math.Max(1, hitstunFrames));
            }

            return true;
        }

        private void TickGrounded(FighterCommand command)
        {
            _state.OnGround = true;
            _state.VelocityY = 0;

            if (_state.State == FighterStateId.Guard && !command.Guard)
            {
                EnterState(FighterStateId.Idle, 0);
            }
            else if (_state.State == FighterStateId.Crouch && !command.Crouch)
            {
                EnterState(FighterStateId.Idle, 0);
            }

            if ((_state.State == FighterStateId.Idle || _state.State == FighterStateId.Walk) && command.Jump)
            {
                _state.VelocityX = 0;
                EnterState(FighterStateId.JumpStartup, JumpStartupFrames);
                return;
            }

            if ((_state.State == FighterStateId.Idle || _state.State == FighterStateId.Walk) && command.Heavy)
            {
                BeginAttack(FighterAttackKind.Heavy);
                return;
            }

            if ((_state.State == FighterStateId.Idle || _state.State == FighterStateId.Walk) && command.Light)
            {
                BeginAttack(FighterAttackKind.Light);
                return;
            }

            if (command.Guard)
            {
                _state.VelocityX = 0;
                EnterState(FighterStateId.Guard, 0);
                return;
            }

            if (command.Crouch)
            {
                _state.VelocityX = 0;
                EnterState(FighterStateId.Crouch, 0);
                return;
            }

            _state.VelocityX = command.Horizontal * _walkSpeed;
            _state.PositionX += _state.VelocityX;
            EnterState(command.Horizontal == 0 ? FighterStateId.Idle : FighterStateId.Walk, 0);
        }

        private void TickAirborne()
        {
            if (_state.State == FighterStateId.JumpStartup)
            {
                _state.StateTimer--;
                if (_state.StateTimer <= 0)
                {
                    _state.OnGround = false;
                    _state.VelocityY = JumpVelocity;
                    EnterState(FighterStateId.Jump, 0);
                }

                return;
            }

            _state.OnGround = false;
            _state.PositionX += _state.VelocityX;
            _state.PositionY += _state.VelocityY;
            _state.VelocityY -= Gravity;

            if (_state.State == FighterStateId.Jump && _state.VelocityY <= 0)
            {
                EnterState(FighterStateId.Fall, 0);
            }

            if (_state.PositionY <= 0)
            {
                _state.PositionY = 0;
                _state.VelocityY = 0;
                _state.OnGround = true;
                EnterState(FighterStateId.Idle, 0);
            }
        }

        private void TickAttack()
        {
            _state.StateTimer--;
            if (_state.StateTimer > 0)
            {
                return;
            }

            if (_state.State == FighterStateId.AttackStartup)
            {
                EnterState(FighterStateId.AttackActive, GetActiveFrames());
            }
            else if (_state.State == FighterStateId.AttackActive)
            {
                EnterState(FighterStateId.AttackRecovery, GetRecoveryFrames());
            }
            else
            {
                _state.CurrentAttack = FighterAttackKind.None;
                EnterState(_state.OnGround ? FighterStateId.Idle : FighterStateId.Fall, 0);
            }
        }

        private void TickReaction()
        {
            _state.StateTimer--;
            _state.VelocityX = _state.VelocityX * 7 / 10;
            _state.PositionX += _state.VelocityX;

            if (_state.StateTimer <= 0)
            {
                EnterState(_state.OnGround ? FighterStateId.Idle : FighterStateId.Fall, 0);
            }
        }

        private void BeginAttack(FighterAttackKind attack)
        {
            _state.CurrentAttack = attack;
            _state.AttackHasHit = false;
            _state.VelocityX = 0;
            EnterState(FighterStateId.AttackStartup, GetStartupFrames());
        }

        private int GetStartupFrames()
        {
            return _state.CurrentAttack == FighterAttackKind.Heavy
                ? HeavyStartupFrames
                : LightStartupFrames;
        }

        private int GetActiveFrames()
        {
            return _state.CurrentAttack == FighterAttackKind.Heavy
                ? HeavyActiveFrames
                : LightActiveFrames;
        }

        private int GetRecoveryFrames()
        {
            return _state.CurrentAttack == FighterAttackKind.Heavy
                ? HeavyRecoveryFrames
                : LightRecoveryFrames;
        }

        private void EnterState(FighterStateId nextState, int timer)
        {
            if (_state.State == nextState)
            {
                _state.StateTimer = timer;
                return;
            }

            _state.PreviousState = _state.State;
            _state.State = nextState;
            _state.StateFrame = 0;
            _state.StateTimer = Math.Max(0, timer);

            if (nextState == FighterStateId.KO)
            {
                _state.VelocityX = 0;
                _state.VelocityY = 0;
                _state.OnGround = true;
            }
        }
    }
}

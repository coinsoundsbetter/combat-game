namespace GLMFighter.Core
{
    public enum FighterPhase
    {
        Idle,
        Walk,
        Guard,
        Crouch,
        JumpStartup,
        Jump,
        Fall,
        Landing,
        AttackStartup,
        AttackActive,
        AttackRecovery,
        Hitstun,
        Blockstun,
        Knockdown,
        KO
    }

    public enum AttackKind
    {
        None,
        Light,
        Heavy
    }

    [System.Serializable]
    public struct AttackSpec
    {
        public AttackKind Kind;
        public int StartupFrames;
        public int ActiveFrames;
        public int RecoveryFrames;
        public int Damage;
        public int HitstunFrames;
        public int BlockstunFrames;
        public int Pushback;

        public static AttackSpec Light()
        {
            return new AttackSpec
            {
                Kind = AttackKind.Light,
                StartupFrames = 5,
                ActiveFrames = 3,
                RecoveryFrames = 12,
                Damage = 40,
                HitstunFrames = 14,
                BlockstunFrames = 8,
                Pushback = 220
            };
        }

        public static AttackSpec Heavy()
        {
            return new AttackSpec
            {
                Kind = AttackKind.Heavy,
                StartupFrames = 10,
                ActiveFrames = 4,
                RecoveryFrames = 20,
                Damage = 80,
                HitstunFrames = 20,
                BlockstunFrames = 12,
                Pushback = 360
            };
        }
    }

    [System.Serializable]
    public struct FighterRoleStats
    {
        public int MaxHealth;
        public int WalkSpeed;
        public CombatBodyProfile StandingBody;
        public AttackSpec LightAttack;
        public AttackSpec HeavyAttack;
        public CombatMoveData IdleMove;
        public CombatMoveData WalkForwardMove;
        public CombatMoveData WalkBackwardMove;
        public CombatMoveData GuardMove;
        public CombatMoveData CrouchMove;
        public CombatMoveData JumpMove;
        public CombatMoveData LightAttackMove;
        public CombatMoveData HeavyAttackMove;
        public CombatMoveData HitstunMove;
        public CombatMoveData BlockstunMove;
        public CombatMoveData KOMove;
        public static FighterRoleStats Default()
        {
            CombatBodyProfile standingBody = CombatBodyProfile.FromStandingHurtBox(
                new SimVector2(0, 850),
                320,
                850);

            return new FighterRoleStats
            {
                MaxHealth = 1000,
                WalkSpeed = 95,
                StandingBody = standingBody,
                LightAttack = AttackSpec.Light(),
                HeavyAttack = new AttackSpec { Kind = AttackKind.None }
            };
        }
    }

    public struct FighterState
    {
        public int PlayerIndex;
        public FighterRoleStats RoleStats;
        public int Health;
        public int Facing;
        public SimVector2 Position;
        public SimVector2 Velocity;
        public FighterPhase Phase;
        public AttackKind CurrentAttack;
        public CombatMoveId CurrentMoveId;
        public int PhaseTimer;
        public int PhaseFrame;
        public int MotionFrame;
        public int MotionTicks;
        public bool OnGround;
        public bool AttackHasHit;

        public bool IsKO
        {
            get { return Phase == FighterPhase.KO || Health <= 0; }
        }

        public bool CanAcceptCommand
        {
            get
            {
                return Phase == FighterPhase.Idle ||
                       Phase == FighterPhase.Walk ||
                       Phase == FighterPhase.Guard ||
                       Phase == FighterPhase.Crouch ||
                       Phase == FighterPhase.Jump ||
                       Phase == FighterPhase.Fall;
            }
        }

        public bool IsAirborne
        {
            get { return Phase == FighterPhase.Jump || Phase == FighterPhase.Fall; }
        }

        public int MoveFrame
        {
            get { return MotionFrame; }
        }

        public int MoveTicks
        {
            get { return MotionTicks; }
        }
    }

    public struct BattleSnapshot
    {
        public int Frame;
        public int WinnerIndex;
        public FighterState PlayerOne;
        public FighterState PlayerTwo;
    }
}

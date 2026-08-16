namespace GLMFighter.Core
{
    public enum CombatMoveId
    {
        None,
        Idle,
        WalkForward,
        WalkBackward,
        Guard,
        Crouch,
        Jump,
        LightAttack,
        HeavyAttack,
        Hitstun,
        Blockstun,
        KO
    }

    public enum CombatBoxKind
    {
        Hurt,
        Hit,
        Push,
        Throw,
        Proximity,
        Block
    }

    [System.Flags]
    public enum CombatFrameFlags
    {
        None = 0,
        Startup = 1,
        Active = 1 << 1,
        Recovery = 1 << 2,
        Airborne = 1 << 3,
        Landing = 1 << 4,
        CanAcceptCommand = 1 << 5,
        CanGuard = 1 << 6,
        CanCancel = 1 << 7,
        Invulnerable = 1 << 8
    }

    [System.Serializable]
    public struct CombatBox
    {
        public CombatBoxKind Kind;
        public int LocalCenterX;
        public int LocalCenterY;
        public int HalfWidth;
        public int HalfHeight;
        public int Group;

        public SimRect ToWorldRect(FighterState fighter)
        {
            return new SimRect(
                fighter.Position.X + LocalCenterX * fighter.Facing,
                fighter.Position.Y + LocalCenterY,
                HalfWidth,
                HalfHeight);
        }
    }

    [System.Serializable]
    public struct CombatFrameData
    {
        public CombatFrameFlags Flags;
        public SimVector2 EntityOffset;
        public int BoundsHalfSizeOffsetX;
        public int BoundsHalfSizeOffsetY;
        public CombatBox[] Boxes;
    }

    [System.Serializable]
    public struct CombatBodyProfile
    {
        public SimVector2 HurtBoxCenter;
        public int HurtBoxHalfWidth;
        public int HurtBoxHalfHeight;
        public SimVector2 PushBoxCenter;
        public int PushBoxHalfWidth;
        public int PushBoxHalfHeight;

        public static CombatBodyProfile FromStandingHurtBox(
            SimVector2 hurtBoxCenter,
            int hurtBoxHalfWidth,
            int hurtBoxHalfHeight)
        {
            return new CombatBodyProfile
            {
                HurtBoxCenter = hurtBoxCenter,
                HurtBoxHalfWidth = hurtBoxHalfWidth,
                HurtBoxHalfHeight = hurtBoxHalfHeight,
                PushBoxCenter = hurtBoxCenter,
                PushBoxHalfWidth = hurtBoxHalfWidth,
                PushBoxHalfHeight = hurtBoxHalfHeight
            };
        }
    }

    [System.Serializable]
    public struct CombatStateRange
    {
        public string StateId;
        public int StartFrame;
        public int EndFrame;
        public bool Value;

        public bool ContainsFrame(int frame)
        {
            return frame >= StartFrame && frame <= EndFrame;
        }
    }

    [System.Serializable]
    public struct CombatMoveData
    {
        public CombatMoveId MoveId;
        public int FrameRate;
        public bool Loop;
        public CombatFrameData[] Frames;
        public CombatStateRange[] States;
        public AttackSpec Attack;

        public bool HasFrames
        {
            get { return Frames != null && Frames.Length > 0; }
        }

        public int TotalFrames
        {
            get { return Frames == null ? 0 : Frames.Length; }
        }

        public int FrameCount
        {
            get { return TotalFrames; }
        }

        public float DurationSeconds
        {
            get { return !HasFrames ? 0f : TotalFrames / (float)SafeFrameRate; }
        }

        public int SimulationFrameCount
        {
            get
            {
                if (!HasFrames)
                {
                    return 0;
                }

                int simulationFrameRate = BattleSimulation.FramesPerSecond;
                return (TotalFrames * simulationFrameRate + SafeFrameRate - 1) / SafeFrameRate;
            }
        }

        public int GetFrameForSimulationTick(int simulationTick)
        {
            if (!HasFrames)
            {
                return 0;
            }

            simulationTick = SimMath.Clamp(simulationTick - 1, 0, int.MaxValue);
            int frame = simulationTick * SafeFrameRate / BattleSimulation.FramesPerSecond;

            if (Loop)
            {
                frame %= TotalFrames;

                if (frame < 0)
                {
                    frame += TotalFrames;
                }
            }
            else
            {
                frame = SimMath.Clamp(frame, 0, TotalFrames - 1);
            }

            return frame;
        }

        public CombatFrameData GetFrame(int frame)
        {
            if (!HasFrames)
            {
                return new CombatFrameData();
            }

            int index = frame;

            if (Loop)
            {
                index %= Frames.Length;

                if (index < 0)
                {
                    index += Frames.Length;
                }
            }
            else
            {
                index = SimMath.Clamp(index, 0, Frames.Length - 1);
            }

            return Frames[index];
        }

        public bool SampleState(string stateId, int frame, bool defaultValue = false)
        {
            bool result = defaultValue;
            if (States == null)
            {
                return result;
            }

            for (int index = 0; index < States.Length; index++)
            {
                if (States[index].StateId == stateId && States[index].ContainsFrame(frame))
                {
                    result = States[index].Value;
                }
            }

            return result;
        }

        private int SafeFrameRate
        {
            get { return FrameRate <= 0 ? BattleSimulation.FramesPerSecond : FrameRate; }
        }

        public AttackSpec ApplyAttackWindow(AttackSpec fallback)
        {
            AttackWindow window = ResolveAttackWindow(fallback);

            return new AttackSpec
            {
                Kind = fallback.Kind,
                StartupFrames = window.StartupFrames,
                ActiveFrames = window.ActiveFrames,
                RecoveryFrames = window.RecoveryFrames,
                Damage = fallback.Damage,
                HitstunFrames = fallback.HitstunFrames,
                BlockstunFrames = fallback.BlockstunFrames,
                Pushback = fallback.Pushback
            };
        }

        private AttackWindow ResolveAttackWindow(AttackSpec fallback)
        {
            if (!HasFrames)
            {
                return AttackWindow.FromSpec(fallback);
            }

            int firstActive = -1;
            int lastActive = -1;

            for (int index = 0; index < Frames.Length; index++)
            {
                if ((Frames[index].Flags & CombatFrameFlags.Active) == 0)
                {
                    continue;
                }

                if (firstActive < 0)
                {
                    firstActive = index;
                }

                lastActive = index;
            }

            if (firstActive < 0)
            {
                return AttackWindow.FromSpec(fallback);
            }

            int activeStartTick = FrameIndexToSimulationTick(firstActive);
            int activeEndTick = FrameIndexToSimulationTick(lastActive + 1);
            int motionDurationTicks = SimulationFrameCount;

            return new AttackWindow
            {
                StartupFrames = activeStartTick,
                ActiveFrames = activeEndTick - activeStartTick < 1 ? 1 : activeEndTick - activeStartTick,
                RecoveryFrames = SimMath.Clamp(motionDurationTicks - activeEndTick, 0, motionDurationTicks)
            };
        }

        private int FrameIndexToSimulationTick(int frameIndex)
        {
            return (frameIndex * BattleSimulation.FramesPerSecond + SafeFrameRate - 1) / SafeFrameRate;
        }

        private struct AttackWindow
        {
            public int StartupFrames;
            public int ActiveFrames;
            public int RecoveryFrames;

            public static AttackWindow FromSpec(AttackSpec spec)
            {
                return new AttackWindow
                {
                    StartupFrames = spec.StartupFrames,
                    ActiveFrames = spec.ActiveFrames,
                    RecoveryFrames = spec.RecoveryFrames
                };
            }
        }
    }
}

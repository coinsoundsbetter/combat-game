using System;

namespace GLMFighter.Core
{
    public sealed class BattleSimulation
    {
        public const int FramesPerSecond = 60;
        public const int GroundY = 0;
        public const int ArenaMinX = -5200;
        public const int ArenaMaxX = 5200;
        private const string JumpStartupStateId = "JumpStartup";

        public static readonly FighterRoleStats DefaultFighterRoleStats = FighterRoleStats.Default();

        private FighterState _playerOne;
        private FighterState _playerTwo;

        public int Frame { get; private set; }
        public int WinnerIndex { get; private set; }

        public FighterState PlayerOne
        {
            get { return _playerOne; }
        }

        public FighterState PlayerTwo
        {
            get { return _playerTwo; }
        }

        public BattleSimulation()
        {
            Reset();
        }

        public void Reset()
        {
            Reset(DefaultFighterRoleStats, DefaultFighterRoleStats);
        }

        public void Reset(FighterRoleStats playerOneRoleStats, FighterRoleStats playerTwoRoleStats)
        {
            Frame = 0;
            WinnerIndex = -1;
            _playerOne = CreateFighter(0, -1400, 1, playerOneRoleStats);
            _playerTwo = CreateFighter(1, 1400, -1, playerTwoRoleStats);
        }

        public BattleSnapshot Capture()
        {
            return new BattleSnapshot
            {
                Frame = Frame,
                WinnerIndex = WinnerIndex,
                PlayerOne = _playerOne,
                PlayerTwo = _playerTwo
            };
        }

        public void Restore(BattleSnapshot snapshot)
        {
            Frame = snapshot.Frame;
            WinnerIndex = snapshot.WinnerIndex;
            _playerOne = snapshot.PlayerOne;
            _playerTwo = snapshot.PlayerTwo;
        }

        public int ComputeChecksum()
        {
            int hash = 17;
            AppendChecksum(ref hash, Frame);
            AppendChecksum(ref hash, WinnerIndex);
            AppendChecksum(ref hash, _playerOne);
            AppendChecksum(ref hash, _playerTwo);
            return hash;
        }

        public void Step(FighterInput playerOneInput, FighterInput playerTwoInput)
        {
            if (WinnerIndex >= 0)
            {
                Frame++;
                return;
            }

            playerOneInput = playerOneInput.Normalized();
            playerTwoInput = playerTwoInput.Normalized();
            FighterPhase playerOneStartingPhase = _playerOne.Phase;
            FighterPhase playerTwoStartingPhase = _playerTwo.Phase;
            int playerOneStartingMotionFrame = _playerOne.MotionFrame;
            int playerTwoStartingMotionFrame = _playerTwo.MotionFrame;

            UpdateFacing(ref _playerOne, ref _playerTwo);
            AdvanceFighter(ref _playerOne, playerOneInput);
            AdvanceFighter(ref _playerTwo, playerTwoInput);
            ResolveBodyCollision();
            ResolveAttack(ref _playerOne, ref _playerTwo);
            ResolveAttack(ref _playerTwo, ref _playerOne);
            TickPhase(ref _playerOne);
            TickPhase(ref _playerTwo);
            AdvanceFrameCounters(ref _playerOne, playerOneStartingPhase, playerOneStartingMotionFrame);
            AdvanceFrameCounters(ref _playerTwo, playerTwoStartingPhase, playerTwoStartingMotionFrame);
            UpdateWinner();

            Frame++;
        }

        public SimRect[] GetHurtboxes(FighterState fighter)
        {
            return GetLogicBodyBoxes(fighter, CombatBoxKind.Hurt);
        }

        public SimRect[] GetPushboxes(FighterState fighter)
        {
            return GetLogicBodyBoxes(fighter, CombatBoxKind.Push);
        }

        public bool TryGetAttackHitboxes(FighterState fighter, out SimRect[] hitboxes)
        {
            if (fighter.CurrentAttack == AttackKind.None)
            {
                hitboxes = new SimRect[0];
                return false;
            }

            hitboxes = GetLogicBodyBoxes(fighter, CombatBoxKind.Hit);

            if (hitboxes.Length > 0)
            {
                return true;
            }

            return false;
        }

        public SimVector2 GetEntityCenter(FighterState fighter)
        {
            CombatMoveData moveData = GetMoveData(fighter);
            CombatFrameData frame = moveData.HasFrames
                ? moveData.GetFrame(fighter.MotionFrame)
                : new CombatFrameData();

            return GetEntityCenter(fighter, frame);
        }

        private static SimVector2 GetEntityCenter(FighterState fighter, CombatFrameData frame)
        {
            return new SimVector2(
                fighter.Position.X + frame.EntityOffset.X * fighter.Facing,
                fighter.Position.Y + frame.EntityOffset.Y);
        }

        private static FighterState CreateFighter(int playerIndex, int x, int facing, FighterRoleStats roleStats)
        {
            return new FighterState
            {
                PlayerIndex = playerIndex,
                RoleStats = roleStats,
                Health = roleStats.MaxHealth,
                Facing = facing,
                Position = new SimVector2(x, GroundY),
                Velocity = SimVector2.Zero,
                Phase = FighterPhase.Idle,
                CurrentAttack = AttackKind.None,
                CurrentMoveId = CombatMoveId.Idle,
                PhaseTimer = 0,
                PhaseFrame = 0,
                MotionFrame = 0,
                MotionTicks = 0,
                OnGround = true,
                AttackHasHit = false
            };
        }

        private static void UpdateFacing(ref FighterState a, ref FighterState b)
        {
            if (!a.IsKO && a.CanAcceptCommand)
            {
                a.Facing = a.Position.X <= b.Position.X ? 1 : -1;
            }

            if (!b.IsKO && b.CanAcceptCommand)
            {
                b.Facing = b.Position.X <= a.Position.X ? 1 : -1;
            }
        }

        private static void AdvanceFighter(ref FighterState fighter, FighterInput input)
        {
            if (fighter.IsKO)
            {
                fighter.Velocity = SimVector2.Zero;
                return;
            }

            if (fighter.CanAcceptCommand)
            {
                bool canStartAttack = fighter.Phase != FighterPhase.Guard &&
                                      fighter.Phase != FighterPhase.Crouch;

                if (canStartAttack && input.Heavy)
                {
                    StartAttack(ref fighter, fighter.RoleStats.HeavyAttack);
                }
                else if (canStartAttack && input.Light)
                {
                    StartAttack(ref fighter, fighter.RoleStats.LightAttack);
                }
                else if (input.Guard && fighter.OnGround)
                {
                    SetPhase(ref fighter, FighterPhase.Guard);
                    fighter.Velocity.X = 0;
                }
                else if (input.Crouch && fighter.OnGround)
                {
                    SetPhase(ref fighter, FighterPhase.Crouch);
                    fighter.Velocity.X = 0;
                }
                else
                {
                    ApplyMovementInput(ref fighter, input);
                }
            }

            ApplyPhysics(ref fighter);
        }

        private static void ApplyMovementInput(ref FighterState fighter, FighterInput input)
        {
            if (CanStartJump(fighter, input))
            {
                StartJump(ref fighter);
                return;
            }

            fighter.Velocity.X = input.Horizontal * fighter.RoleStats.WalkSpeed;

            if (fighter.OnGround)
            {
                SetPhase(ref fighter, input.Horizontal == 0 ? FighterPhase.Idle : FighterPhase.Walk);
            }
        }

        private static bool CanStartJump(FighterState fighter, FighterInput input)
        {
            return fighter.OnGround && input.Jump;
        }

        private static void StartJump(ref FighterState fighter)
        {
            SetPhase(ref fighter, FighterPhase.JumpStartup);
            fighter.Velocity.X = 0;
            fighter.Velocity.Y = 0;
            fighter.OnGround = true;
        }

        private static void StartAttack(ref FighterState fighter, AttackSpec spec)
        {
            if (spec.Kind == AttackKind.None)
            {
                return;
            }

            fighter.CurrentAttack = spec.Kind;
            SetPhase(ref fighter, FighterPhase.AttackStartup);
            fighter.PhaseTimer = spec.StartupFrames;
            fighter.AttackHasHit = false;
            fighter.Velocity.X = 0;
        }

        private static void ApplyPhysics(ref FighterState fighter)
        {
            CombatMoveData moveData = GetMoveData(fighter);
            bool motionDrivenJump = IsJumpPhase(fighter.Phase);
            if (motionDrivenJump)
            {
                if (!moveData.HasFrames)
                {
                    throw new InvalidOperationException(
                        "A fighter entered a jump phase without a Jump MotionTimelineAsset.");
                }

                ApplyMotionDrivenJump(ref fighter, moveData);
            }
            else
            {
                fighter.Velocity.Y = 0;
            }

            fighter.Position.X += fighter.Velocity.X;

            fighter.Position.X = SimMath.Clamp(fighter.Position.X, ArenaMinX, ArenaMaxX);

            if (!motionDrivenJump)
            {
                if (fighter.Position.Y <= GroundY)
                {
                    fighter.Position.Y = GroundY;
                    fighter.Velocity.Y = 0;
                    fighter.OnGround = true;

                    if (fighter.Phase == FighterPhase.Jump || fighter.Phase == FighterPhase.Fall)
                    {
                        if (fighter.RoleStats.JumpMove.HasFrames &&
                            !IsMotionComplete(fighter, fighter.RoleStats.JumpMove))
                        {
                            SetPhase(ref fighter, FighterPhase.Landing, false);
                        }
                        else
                        {
                            SetPhase(ref fighter, FighterPhase.Idle);
                        }
                    }
                }
                else if (fighter.Velocity.Y < 0 && fighter.Phase == FighterPhase.Jump)
                {
                    SetPhase(ref fighter, FighterPhase.Fall, false);
                }
            }
        }

        private static void ApplyMotionDrivenJump(ref FighterState fighter, CombatMoveData moveData)
        {
            int currentHeight = GetEntityOffsetY(moveData, fighter.MotionFrame);
            int nextHeight = GetEntityOffsetYAtTick(moveData, fighter.MotionTicks + 1);
            fighter.Velocity.Y = nextHeight - currentHeight;

            if (fighter.Phase == FighterPhase.JumpStartup)
            {
                if (moveData.SampleState(JumpStartupStateId, fighter.MotionFrame))
                {
                    fighter.OnGround = true;
                    fighter.Velocity.Y = 0;
                    return;
                }

                SetPhase(ref fighter, FighterPhase.Jump, false);
            }

            if (fighter.Phase == FighterPhase.Landing)
            {
                fighter.OnGround = true;
                fighter.Velocity.Y = 0;
                return;
            }

            fighter.OnGround = false;

            if (fighter.Phase == FighterPhase.Jump && fighter.Velocity.Y < 0)
            {
                SetPhase(ref fighter, FighterPhase.Fall, false);
            }

            bool reachedGround = currentHeight <= GroundY && nextHeight <= currentHeight;
            if (!reachedGround || (fighter.Phase != FighterPhase.Jump && fighter.Phase != FighterPhase.Fall))
            {
                return;
            }

            fighter.OnGround = true;
            fighter.Velocity.Y = 0;

            if (!IsMotionComplete(fighter, moveData))
            {
                SetPhase(ref fighter, FighterPhase.Landing, false);
            }
            else
            {
                SetPhase(ref fighter, FighterPhase.Idle);
            }
        }

        private static int GetEntityOffsetY(CombatMoveData moveData, int motionFrame)
        {
            if (!moveData.HasFrames)
            {
                return GroundY;
            }
            
            return moveData.GetFrame(motionFrame).EntityOffset.Y;
        }

        private static int GetEntityOffsetYAtTick(CombatMoveData moveData, int motionTick)
        {
            if (!moveData.HasFrames)
            {
                return GroundY;
            }

            int nextFrame = moveData.GetFrameForSimulationTick(motionTick);
            return GetEntityOffsetY(moveData, nextFrame);
        }

        private static bool IsJumpPhase(FighterPhase phase)
        {
            return phase == FighterPhase.JumpStartup ||
                   phase == FighterPhase.Jump ||
                   phase == FighterPhase.Fall ||
                   phase == FighterPhase.Landing;
        }

        private void ResolveBodyCollision()
        {
            SimRect[] playerOnePushboxes = GetPushboxes(_playerOne);
            SimRect[] playerTwoPushboxes = GetPushboxes(_playerTwo);

            if (playerOnePushboxes.Length == 0 || playerTwoPushboxes.Length == 0)
            {
                return;
            }

            SimRect playerOnePushbox = playerOnePushboxes[0];
            SimRect playerTwoPushbox = playerTwoPushboxes[0];
            int bodySeparation = playerOnePushbox.HalfWidth + playerTwoPushbox.HalfWidth;
            int distance = playerTwoPushbox.CenterX - playerOnePushbox.CenterX;

            if (distance < bodySeparation && distance > -bodySeparation)
            {
                int direction = distance >= 0 ? 1 : -1;
                int overlap = bodySeparation - Abs(distance);
                int push = (overlap + 1) / 2;

                _playerOne.Position.X -= direction * push;
                _playerTwo.Position.X += direction * push;
                _playerOne.Position.X = SimMath.Clamp(_playerOne.Position.X, ArenaMinX, ArenaMaxX);
                _playerTwo.Position.X = SimMath.Clamp(_playerTwo.Position.X, ArenaMinX, ArenaMaxX);
            }
        }

        private void ResolveAttack(ref FighterState attacker, ref FighterState defender)
        {
            if (attacker.AttackHasHit || attacker.CurrentAttack == AttackKind.None || defender.IsKO)
            {
                return;
            }

            SimRect[] hitboxes;
            if (!TryGetAttackHitboxes(attacker, out hitboxes))
            {
                return;
            }

            if (!AnyIntersects(hitboxes, GetHurtboxes(defender)))
            {
                return;
            }

            AttackSpec spec = GetAttackSpec(attacker);
            attacker.AttackHasHit = true;

            if (defender.Phase == FighterPhase.Guard && defender.OnGround)
            {
                SetPhase(ref defender, FighterPhase.Blockstun);
                defender.PhaseTimer = spec.BlockstunFrames;
                defender.Velocity.X = attacker.Facing * spec.Pushback;
                defender.Velocity.Y = 0;
                return;
            }

            defender.Health = SimMath.Clamp(defender.Health - spec.Damage, 0, defender.RoleStats.MaxHealth);
            SetPhase(ref defender, defender.Health == 0 ? FighterPhase.KO : FighterPhase.Hitstun);
            defender.PhaseTimer = defender.Health == 0 ? 0 : spec.HitstunFrames;
            defender.CurrentAttack = AttackKind.None;
            defender.AttackHasHit = false;
            defender.Velocity.X = attacker.Facing * spec.Pushback;
            defender.Velocity.Y = defender.OnGround ? 0 : 140;
        }

        private static void TickPhase(ref FighterState fighter)
        {
            switch (fighter.Phase)
            {
                case FighterPhase.AttackStartup:
                    fighter.PhaseTimer--;
                    if (fighter.PhaseTimer <= 0)
                    {
                        AttackSpec spec = GetAttackSpec(fighter);
                        SetPhase(ref fighter, FighterPhase.AttackActive, false);
                        fighter.PhaseTimer = spec.ActiveFrames;
                    }
                    break;

                case FighterPhase.AttackActive:
                    fighter.PhaseTimer--;
                    if (fighter.PhaseTimer <= 0)
                    {
                        AttackSpec spec = GetAttackSpec(fighter);
                        SetPhase(ref fighter, FighterPhase.AttackRecovery, false);
                        fighter.PhaseTimer = spec.RecoveryFrames;
                    }
                    break;

                case FighterPhase.AttackRecovery:
                    fighter.PhaseTimer--;
                    if (fighter.PhaseTimer <= 0)
                    {
                        SetPhase(ref fighter, fighter.OnGround ? FighterPhase.Idle : FighterPhase.Fall);
                        fighter.CurrentAttack = AttackKind.None;
                    }
                    break;

                case FighterPhase.Hitstun:
                case FighterPhase.Blockstun:
                    fighter.PhaseTimer--;
                    fighter.Velocity.X = fighter.Velocity.X * 7 / 10;
                    if (fighter.PhaseTimer <= 0)
                    {
                        SetPhase(ref fighter, fighter.OnGround ? FighterPhase.Idle : FighterPhase.Fall);
                    }
                    break;

                case FighterPhase.Landing:
                    if (IsMotionComplete(fighter, fighter.RoleStats.JumpMove))
                    {
                        SetPhase(ref fighter, FighterPhase.Idle);
                    }
                    break;
            }
        }

        private static void SetPhase(ref FighterState fighter, FighterPhase phase, bool resetMotionFrame = true)
        {
            if (fighter.Phase == phase)
            {
                return;
            }

            fighter.Phase = phase;
            fighter.CurrentMoveId = ResolveMoveId(fighter, phase);
            fighter.PhaseFrame = 0;

            if (resetMotionFrame)
            {
                fighter.MotionFrame = 0;
                fighter.MotionTicks = 0;
            }
        }

        private static void AdvanceFrameCounters(
            ref FighterState fighter,
            FighterPhase startingPhase,
            int startingMotionFrame)
        {
            if (fighter.Phase == startingPhase)
            {
                fighter.PhaseFrame++;
            }

            if (fighter.MotionFrame < 0)
            {
                fighter.MotionFrame = 0;
                fighter.MotionTicks = 1;
            }
            else if (fighter.MotionFrame == startingMotionFrame)
            {
                fighter.MotionTicks++;
            }

            CombatMoveData moveData = GetMoveData(fighter);
            fighter.CurrentMoveId = moveData.MoveId;

            if (moveData.HasFrames)
            {
                int duration = moveData.SimulationFrameCount;

                if (duration > 0)
                {
                    if (moveData.Loop)
                    {
                        fighter.MotionTicks = (fighter.MotionTicks - 1) % duration + 1;
                    }
                    else if (fighter.MotionTicks > duration)
                    {
                        fighter.MotionTicks = duration;
                    }
                }

                fighter.MotionFrame = moveData.GetFrameForSimulationTick(fighter.MotionTicks);
            }
            else if (fighter.MotionFrame >= 0 && fighter.MotionFrame == startingMotionFrame)
            {
                fighter.MotionFrame++;
            }
        }

        private static bool IsMotionComplete(FighterState fighter, CombatMoveData moveData)
        {
            return moveData.HasFrames && fighter.MotionTicks >= moveData.SimulationFrameCount;
        }

        private static bool AnyIntersects(SimRect[] hitboxes, SimRect[] hurtboxes)
        {
            for (int hitIndex = 0; hitIndex < hitboxes.Length; hitIndex++)
            {
                for (int hurtIndex = 0; hurtIndex < hurtboxes.Length; hurtIndex++)
                {
                    if (hitboxes[hitIndex].Intersects(hurtboxes[hurtIndex]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static SimRect[] GetLogicBodyBoxes(FighterState fighter, CombatBoxKind kind)
        {
            CombatMoveData moveData = GetMoveData(fighter);
            CombatFrameData frame = moveData.HasFrames
                ? moveData.GetFrame(fighter.MotionFrame)
                : new CombatFrameData();

            if (kind == CombatBoxKind.Hit && (frame.Flags & CombatFrameFlags.Active) == 0)
            {
                return new SimRect[0];
            }

            SimRect[] moveFrameBoxes = GetMoveFrameBoxes(fighter, frame, kind);

            if (moveFrameBoxes.Length > 0)
            {
                return moveFrameBoxes;
            }

            if (kind == CombatBoxKind.Hurt || kind == CombatBoxKind.Push)
            {
                return new[] { ToBodyBox(fighter, frame, kind) };
            }

            return new SimRect[0];
        }

        private static SimRect[] GetMoveFrameBoxes(
            FighterState fighter,
            CombatFrameData frame,
            CombatBoxKind kind)
        {
            if (frame.Boxes == null || frame.Boxes.Length == 0)
            {
                return new SimRect[0];
            }

            int count = 0;

            for (int index = 0; index < frame.Boxes.Length; index++)
            {
                if (frame.Boxes[index].Kind == kind)
                {
                    count++;
                }
            }

            if (count == 0)
            {
                return new SimRect[0];
            }

            SimRect[] rects = new SimRect[count];
            int rectIndex = 0;

            for (int index = 0; index < frame.Boxes.Length; index++)
            {
                CombatBox box = frame.Boxes[index];

                if (box.Kind == kind)
                {
                    rects[rectIndex] = ToWorldRect(fighter, frame, box);
                    rectIndex++;
                }
            }

            return rects;
        }

        private static SimRect ToWorldRect(
            FighterState fighter,
            CombatFrameData frame,
            CombatBox box)
        {
            SimVector2 entityCenter = GetEntityCenter(fighter, frame);

            return new SimRect(
                entityCenter.X + box.LocalCenterX * fighter.Facing,
                entityCenter.Y + box.LocalCenterY,
                box.HalfWidth,
                box.HalfHeight);
        }

        private static SimRect ToBodyBox(
            FighterState fighter,
            CombatFrameData frame,
            CombatBoxKind kind)
        {
            CombatBodyProfile body = fighter.RoleStats.StandingBody;
            if (body.HurtBoxHalfWidth <= 0 || body.HurtBoxHalfHeight <= 0)
            {
                body = DefaultFighterRoleStats.StandingBody;
            }

            SimVector2 baseCenter = kind == CombatBoxKind.Push
                ? body.PushBoxCenter
                : body.HurtBoxCenter;
            int baseHalfWidth = kind == CombatBoxKind.Push
                ? body.PushBoxHalfWidth
                : body.HurtBoxHalfWidth;
            int baseHalfHeight = kind == CombatBoxKind.Push
                ? body.PushBoxHalfHeight
                : body.HurtBoxHalfHeight;

            if (baseHalfWidth <= 0 || baseHalfHeight <= 0)
            {
                baseCenter = body.HurtBoxCenter;
                baseHalfWidth = body.HurtBoxHalfWidth;
                baseHalfHeight = body.HurtBoxHalfHeight;
            }

            SimVector2 entityCenter = GetEntityCenter(fighter, frame);
            SimVector2 localCenter = baseCenter + frame.BoundsCenterOffset;
            int halfWidth = Math.Max(1, baseHalfWidth + frame.BoundsHalfSizeOffsetX);
            int halfHeight = Math.Max(1, baseHalfHeight + frame.BoundsHalfSizeOffsetY);

            return new SimRect(
                entityCenter.X + localCenter.X * fighter.Facing,
                entityCenter.Y + localCenter.Y,
                halfWidth,
                halfHeight);
        }

        private static CombatMoveData GetMoveData(FighterState fighter)
        {
            switch (fighter.Phase)
            {
                case FighterPhase.Idle:
                    return fighter.RoleStats.IdleMove;
                case FighterPhase.Walk:
                    return fighter.Velocity.X * fighter.Facing >= 0
                        ? fighter.RoleStats.WalkForwardMove
                        : fighter.RoleStats.WalkBackwardMove;
                case FighterPhase.Guard:
                    return fighter.RoleStats.GuardMove;
                case FighterPhase.Crouch:
                    return fighter.RoleStats.CrouchMove;
                case FighterPhase.JumpStartup:
                case FighterPhase.Jump:
                case FighterPhase.Fall:
                case FighterPhase.Landing:
                    return fighter.RoleStats.JumpMove;
                case FighterPhase.AttackStartup:
                case FighterPhase.AttackActive:
                case FighterPhase.AttackRecovery:
                    return fighter.CurrentAttack == AttackKind.Light
                        ? fighter.RoleStats.LightAttackMove
                        : fighter.RoleStats.HeavyAttackMove;
                case FighterPhase.Hitstun:
                    return fighter.RoleStats.HitstunMove;
                case FighterPhase.Blockstun:
                    return fighter.RoleStats.BlockstunMove;
                case FighterPhase.KO:
                    return fighter.RoleStats.KOMove;
                default:
                    return new CombatMoveData();
            }
        }

        private void UpdateWinner()
        {
            if (_playerOne.Health <= 0)
            {
                WinnerIndex = 1;
            }
            else if (_playerTwo.Health <= 0)
            {
                WinnerIndex = 0;
            }
        }

        private static AttackSpec GetAttackSpec(FighterState fighter)
        {
            return fighter.CurrentAttack == AttackKind.Heavy ? fighter.RoleStats.HeavyAttack : fighter.RoleStats.LightAttack;
        }

        private static CombatMoveId ResolveMoveId(FighterState fighter, FighterPhase phase)
        {
            switch (phase)
            {
                case FighterPhase.Idle:
                    return CombatMoveId.Idle;
                case FighterPhase.Walk:
                    return fighter.Velocity.X * fighter.Facing >= 0
                        ? CombatMoveId.WalkForward
                        : CombatMoveId.WalkBackward;
                case FighterPhase.Guard:
                    return CombatMoveId.Guard;
                case FighterPhase.Crouch:
                    return CombatMoveId.Crouch;
                case FighterPhase.JumpStartup:
                case FighterPhase.Jump:
                case FighterPhase.Fall:
                case FighterPhase.Landing:
                    return CombatMoveId.Jump;
                case FighterPhase.AttackStartup:
                case FighterPhase.AttackActive:
                case FighterPhase.AttackRecovery:
                    return fighter.CurrentAttack == AttackKind.Heavy
                        ? CombatMoveId.HeavyAttack
                        : CombatMoveId.LightAttack;
                case FighterPhase.Hitstun:
                    return CombatMoveId.Hitstun;
                case FighterPhase.Blockstun:
                    return CombatMoveId.Blockstun;
                case FighterPhase.KO:
                    return CombatMoveId.KO;
                default:
                    return CombatMoveId.None;
            }
        }

        private static int Abs(int value)
        {
            return value < 0 ? -value : value;
        }

        private static void AppendChecksum(ref int hash, FighterState fighter)
        {
            AppendChecksum(ref hash, fighter.PlayerIndex);
            AppendChecksum(ref hash, fighter.RoleStats);
            AppendChecksum(ref hash, fighter.Health);
            AppendChecksum(ref hash, fighter.Facing);
            AppendChecksum(ref hash, fighter.Position.X);
            AppendChecksum(ref hash, fighter.Position.Y);
            AppendChecksum(ref hash, fighter.Velocity.X);
            AppendChecksum(ref hash, fighter.Velocity.Y);
            AppendChecksum(ref hash, (int)fighter.Phase);
            AppendChecksum(ref hash, (int)fighter.CurrentAttack);
            AppendChecksum(ref hash, (int)fighter.CurrentMoveId);
            AppendChecksum(ref hash, fighter.PhaseTimer);
            AppendChecksum(ref hash, fighter.PhaseFrame);
            AppendChecksum(ref hash, fighter.MotionFrame);
            AppendChecksum(ref hash, fighter.MotionTicks);
            AppendChecksum(ref hash, fighter.OnGround ? 1 : 0);
            AppendChecksum(ref hash, fighter.AttackHasHit ? 1 : 0);
        }

        private static void AppendChecksum(ref int hash, int value)
        {
            unchecked
            {
                hash = hash * 31 + value;
            }
        }

        private static void AppendChecksum(ref int hash, FighterRoleStats stats)
        {
            AppendChecksum(ref hash, stats.MaxHealth);
            AppendChecksum(ref hash, stats.WalkSpeed);
        AppendChecksum(ref hash, stats.StandingBody);
            AppendChecksum(ref hash, stats.LightAttack);
            AppendChecksum(ref hash, stats.HeavyAttack);
            AppendChecksum(ref hash, stats.IdleMove);
            AppendChecksum(ref hash, stats.WalkForwardMove);
            AppendChecksum(ref hash, stats.WalkBackwardMove);
            AppendChecksum(ref hash, stats.GuardMove);
            AppendChecksum(ref hash, stats.CrouchMove);
            AppendChecksum(ref hash, stats.JumpMove);
            AppendChecksum(ref hash, stats.LightAttackMove);
            AppendChecksum(ref hash, stats.HeavyAttackMove);
            AppendChecksum(ref hash, stats.HitstunMove);
            AppendChecksum(ref hash, stats.BlockstunMove);
            AppendChecksum(ref hash, stats.KOMove);
        }

        private static void AppendChecksum(ref int hash, AttackSpec spec)
        {
            AppendChecksum(ref hash, (int)spec.Kind);
            AppendChecksum(ref hash, spec.StartupFrames);
            AppendChecksum(ref hash, spec.ActiveFrames);
            AppendChecksum(ref hash, spec.RecoveryFrames);
            AppendChecksum(ref hash, spec.Damage);
            AppendChecksum(ref hash, spec.HitstunFrames);
            AppendChecksum(ref hash, spec.BlockstunFrames);
            AppendChecksum(ref hash, spec.Pushback);
        }

        private static void AppendChecksum(ref int hash, CombatBodyProfile body)
        {
            AppendChecksum(ref hash, body.HurtBoxCenter.X);
            AppendChecksum(ref hash, body.HurtBoxCenter.Y);
            AppendChecksum(ref hash, body.HurtBoxHalfWidth);
            AppendChecksum(ref hash, body.HurtBoxHalfHeight);
            AppendChecksum(ref hash, body.PushBoxCenter.X);
            AppendChecksum(ref hash, body.PushBoxCenter.Y);
            AppendChecksum(ref hash, body.PushBoxHalfWidth);
            AppendChecksum(ref hash, body.PushBoxHalfHeight);
        }

        private static void AppendChecksum(ref int hash, CombatMoveData moveData)
        {
            AppendChecksum(ref hash, (int)moveData.MoveId);
            AppendChecksum(ref hash, moveData.FrameRate);
            AppendChecksum(ref hash, moveData.Loop ? 1 : 0);
            AppendChecksum(ref hash, moveData.FrameCount);
            AppendChecksum(ref hash, moveData.Attack);

            if (moveData.Frames != null)
            {
                for (int frameIndex = 0; frameIndex < moveData.Frames.Length; frameIndex++)
                {
                    AppendChecksum(ref hash, moveData.Frames[frameIndex]);
                }
            }

            int stateCount = moveData.States == null ? 0 : moveData.States.Length;
            AppendChecksum(ref hash, stateCount);
            for (int stateIndex = 0; stateIndex < stateCount; stateIndex++)
            {
                AppendChecksum(ref hash, moveData.States[stateIndex]);
            }
        }

        private static void AppendChecksum(ref int hash, CombatStateRange state)
        {
            AppendChecksum(ref hash, state.StateId);
            AppendChecksum(ref hash, state.StartFrame);
            AppendChecksum(ref hash, state.EndFrame);
            AppendChecksum(ref hash, state.Value ? 1 : 0);
        }

        private static void AppendChecksum(ref int hash, string value)
        {
            if (value == null)
            {
                AppendChecksum(ref hash, 0);
                return;
            }

            AppendChecksum(ref hash, value.Length);
            for (int index = 0; index < value.Length; index++)
            {
                AppendChecksum(ref hash, value[index]);
            }
        }

        private static void AppendChecksum(ref int hash, CombatFrameData frame)
        {
            int boxCount = frame.Boxes == null ? 0 : frame.Boxes.Length;
            AppendChecksum(ref hash, (int)frame.Flags);
            AppendChecksum(ref hash, frame.EntityOffset.X);
            AppendChecksum(ref hash, frame.EntityOffset.Y);
            AppendChecksum(ref hash, frame.BoundsCenterOffset.X);
            AppendChecksum(ref hash, frame.BoundsCenterOffset.Y);
            AppendChecksum(ref hash, frame.BoundsHalfSizeOffsetX);
            AppendChecksum(ref hash, frame.BoundsHalfSizeOffsetY);
            AppendChecksum(ref hash, boxCount);

            for (int boxIndex = 0; boxIndex < boxCount; boxIndex++)
            {
                AppendChecksum(ref hash, frame.Boxes[boxIndex]);
            }

        }

        private static void AppendChecksum(ref int hash, CombatBox box)
        {
            AppendChecksum(ref hash, (int)box.Kind);
            AppendChecksum(ref hash, box.LocalCenterX);
            AppendChecksum(ref hash, box.LocalCenterY);
            AppendChecksum(ref hash, box.HalfWidth);
            AppendChecksum(ref hash, box.HalfHeight);
            AppendChecksum(ref hash, box.Group);
        }

    }
}


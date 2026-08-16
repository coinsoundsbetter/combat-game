using GLMFighter.Core;
using System;
using UnityEngine;

namespace GLMFighter.Runtime
{
    [CreateAssetMenu(menuName = "GLM Fighter/Fighter Role Definition")]
    public sealed class FighterRoleDefinition : ScriptableObject
    {
        [SerializeField] private string roleId = "fighter";
        [SerializeField] private GameObject prefab;

        [Header("Base Stats")]
        [SerializeField] private int maxHealth = 1000;
        [SerializeField] private float walkSpeed = 5.7f;

        [Header("Light Attack")]
        [SerializeField] private AttackDefinition lightAttack = AttackDefinition.Light();

        [Header("Logic Body")]
        [SerializeField] private Vector2 standingHurtBoxSize = new Vector2(0.64f, 1.7f);

        [Header("Motion Timelines")]
        [SerializeField] private MotionTimelineAsset jumpTimeline;
        [SerializeField] private MotionTimelineAsset lightAttackTimeline;

        public string RoleId => roleId;
        public GameObject Prefab => prefab;
        public Vector2 StandingHurtBoxSize => new Vector2(
            Mathf.Abs(standingHurtBoxSize.x),
            Mathf.Abs(standingHurtBoxSize.y));

        public FighterRoleStats ToRoleStats()
        {
            CombatMoveData idle = EmptyMoveData(CombatMoveId.Idle);
            CombatMoveData walkForward = EmptyMoveData(CombatMoveId.WalkForward);
            CombatMoveData walkBackward = EmptyMoveData(CombatMoveId.WalkBackward);
            CombatMoveData guard = EmptyMoveData(CombatMoveId.Guard);
            CombatMoveData crouch = EmptyMoveData(CombatMoveId.Crouch);
            CombatMoveData jump = ToRequiredCoreMoveData(jumpTimeline, CombatMoveId.Jump);
            CombatMoveData lightAttackMotion = lightAttackTimeline == null
                ? EmptyMoveData(CombatMoveId.LightAttack)
                : lightAttackTimeline.ToCoreMoveData(CombatMoveId.LightAttack);
            CombatMoveData heavyAttackMotion = EmptyMoveData(CombatMoveId.HeavyAttack);
            CombatMoveData hitstun = EmptyMoveData(CombatMoveId.Hitstun);
            CombatMoveData blockstun = EmptyMoveData(CombatMoveId.Blockstun);
            CombatMoveData ko = EmptyMoveData(CombatMoveId.KO);
            CombatBodyProfile standingBody = CreateStandingBodyProfile();
            AttackSpec lightAttackSpec = lightAttack.ApplyMoveData(lightAttackMotion);
            lightAttackMotion.Attack = lightAttackSpec;

            return new FighterRoleStats
            {
                MaxHealth = maxHealth,
                WalkSpeed = SimMath.UnitsPerSecondToFrameStep(walkSpeed),
                StandingBody = standingBody,
                LightAttack = lightAttackSpec,
                HeavyAttack = new AttackSpec { Kind = AttackKind.None },
                IdleMove = idle,
                WalkForwardMove = walkForward,
                WalkBackwardMove = walkBackward,
                GuardMove = guard,
                CrouchMove = crouch,
                JumpMove = jump,
                LightAttackMove = lightAttackMotion,
                HeavyAttackMove = heavyAttackMotion,
                HitstunMove = hitstun,
                BlockstunMove = blockstun,
                KOMove = ko
            };
        }

        private CombatBodyProfile CreateStandingBodyProfile()
        {
            Vector2 size = StandingHurtBoxSize;
            return CombatBodyProfile.FromStandingHurtBox(
                new SimVector2(0, SimMath.FromUnity(size.y * 0.5f)),
                SimMath.FromUnity(size.x * 0.5f),
                SimMath.FromUnity(size.y * 0.5f));
        }

        private static CombatMoveData EmptyMoveData(CombatMoveId moveId)
        {
            return new CombatMoveData { MoveId = moveId };
        }

        private static CombatMoveData ToRequiredCoreMoveData(
            MotionTimelineAsset timeline,
            CombatMoveId moveId)
        {
            if (timeline != null)
            {
                MotionTimelineBodyTrackDefinition bodyTrack = timeline.GetBodyTrack();
                if (bodyTrack == null || bodyTrack.Keys.Length == 0)
                {
                    throw new InvalidOperationException(
                        "Jump MotionTimelineAsset requires a Body track with key frames.");
                }

                return timeline.ToCoreMoveData(moveId);
            }

            throw new InvalidOperationException(
                "Fighter role requires a Jump MotionTimelineAsset before it can enter battle.");
        }

        private void Reset()
        {
            lightAttack = AttackDefinition.Light();
        }

        private void OnValidate()
        {
            standingHurtBoxSize.x = Mathf.Max(0f, Mathf.Abs(standingHurtBoxSize.x));
            standingHurtBoxSize.y = Mathf.Max(0f, Mathf.Abs(standingHurtBoxSize.y));

            if (jumpTimeline != null)
            {
                MotionTimelineBodyTrackDefinition bodyTrack = jumpTimeline.GetBodyTrack();
                if (bodyTrack == null || bodyTrack.Keys.Length == 0)
                {
                    Debug.LogError(
                        "Jump MotionTimelineAsset requires a Body track with key frames.",
                        this);
                }
            }
            else
            {
                Debug.LogError("Fighter role requires a Jump MotionTimelineAsset.", this);
            }
        }

        [System.Serializable]
        private struct AttackDefinition
        {
            public int Damage;
            public int HitstunFrames;
            public int BlockstunFrames;
            public float Pushback;

            public AttackSpec ApplyMoveData(CombatMoveData moveData)
            {
                return moveData.ApplyAttackWindow(ToAttackSpec(AttackKind.Light));
            }

            private AttackSpec ToAttackSpec(AttackKind kind)
            {
                return new AttackSpec
                {
                    Kind = kind,
                    Damage = Damage,
                    HitstunFrames = HitstunFrames,
                    BlockstunFrames = BlockstunFrames,
                    Pushback = SimMath.UnitsPerSecondToFrameStep(Pushback)
                };
            }

            public static AttackDefinition Light()
            {
                AttackSpec spec = AttackSpec.Light();
                return new AttackDefinition
                {
                    Damage = spec.Damage,
                    HitstunFrames = spec.HitstunFrames,
                    BlockstunFrames = spec.BlockstunFrames,
                    Pushback = SimMath.ToUnity(spec.Pushback) * BattleSimulation.FramesPerSecond
                };
            }
        }
    }
}

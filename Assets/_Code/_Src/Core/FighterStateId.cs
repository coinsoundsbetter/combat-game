namespace GLMFighter.Src.Core
{
    /// <summary>
    /// High-level gameplay states. Presentation and hitbox data will be added
    /// later; this enum is the first source of truth for fighter behavior.
    /// </summary>
    public enum FighterStateId
    {
        Idle,
        Walk,
        Crouch,
        Guard,
        JumpStartup,
        Jump,
        Fall,
        AttackStartup,
        AttackActive,
        AttackRecovery,
        Hitstun,
        Blockstun,
        Knockdown,
        KO
    }

    public enum FighterAttackKind
    {
        None,
        Light,
        Heavy
    }
}

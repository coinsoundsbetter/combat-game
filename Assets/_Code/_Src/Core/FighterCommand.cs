namespace GLMFighter.Src.Core
{
    /// <summary>
    /// Deterministic input consumed by the fighter state machine.
    /// It intentionally has no Unity InputSystem dependency.
    /// </summary>
    public struct FighterCommand
    {
        public int Horizontal;
        public bool Jump;
        public bool Crouch;
        public bool Guard;
        public bool Light;
        public bool Heavy;

        public FighterCommand Normalized()
        {
            FighterCommand command = this;
            if (command.Horizontal < 0)
            {
                command.Horizontal = -1;
            }
            else if (command.Horizontal > 0)
            {
                command.Horizontal = 1;
            }

            return command;
        }
    }
}

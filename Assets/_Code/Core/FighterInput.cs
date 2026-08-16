namespace GLMFighter.Core
{
    public struct FighterInput
    {
        public int Horizontal;
        public bool Jump;
        public bool Crouch;
        public bool Light;
        public bool Heavy;
        public bool Guard;

        public static FighterInput Neutral
        {
            get { return new FighterInput(); }
        }

        public FighterInput Normalized()
        {
            FighterInput input = this;

            if (input.Horizontal < 0)
            {
                input.Horizontal = -1;
            }
            else if (input.Horizontal > 0)
            {
                input.Horizontal = 1;
            }

            return input;
        }
    }
}

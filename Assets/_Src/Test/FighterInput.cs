namespace _Src.Test {
    public struct FighterInput {
        public int MoveX;
        public bool Attack;

        public override string ToString() {
            return $"MoveX={MoveX},Attack={(Attack ? 1 : 0)}";
        }
    }
}

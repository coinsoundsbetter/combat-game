namespace GLMFighter.Core
{
    public struct SimVector2
    {
        public int X;
        public int Y;

        public SimVector2(int x, int y)
        {
            X = x;
            Y = y;
        }

        public static SimVector2 Zero
        {
            get { return new SimVector2(0, 0); }
        }

        public static SimVector2 operator +(SimVector2 a, SimVector2 b)
        {
            return new SimVector2(a.X + b.X, a.Y + b.Y);
        }

        public static SimVector2 operator -(SimVector2 a, SimVector2 b)
        {
            return new SimVector2(a.X - b.X, a.Y - b.Y);
        }
    }

    public struct SimRect
    {
        public int CenterX;
        public int CenterY;
        public int HalfWidth;
        public int HalfHeight;

        public SimRect(int centerX, int centerY, int halfWidth, int halfHeight)
        {
            CenterX = centerX;
            CenterY = centerY;
            HalfWidth = halfWidth;
            HalfHeight = halfHeight;
        }

        public bool Intersects(SimRect other)
        {
            int dx = Abs(CenterX - other.CenterX);
            int dy = Abs(CenterY - other.CenterY);
            return dx <= HalfWidth + other.HalfWidth && dy <= HalfHeight + other.HalfHeight;
        }

        private static int Abs(int value)
        {
            return value < 0 ? -value : value;
        }
    }

    public static class SimMath
    {
        public const int Unit = 1000;

        public static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        public static float ToUnity(int value)
        {
            return value / (float)Unit;
        }

        public static int FromUnity(float value)
        {
            return (int)(value * Unit + (value >= 0f ? 0.5f : -0.5f));
        }

        public static int UnitsPerSecondToFrameStep(float unitsPerSecond)
        {
            return FromUnity(unitsPerSecond) / BattleSimulation.FramesPerSecond;
        }

        public static int UnitsPerSecondSquaredToFrameAcceleration(float unitsPerSecondSquared)
        {
            return FromUnity(unitsPerSecondSquared) / (BattleSimulation.FramesPerSecond * BattleSimulation.FramesPerSecond);
        }
    }
}

namespace UnicornPet.Core.MiniGame
{
    /// <summary>An axis-aligned rectangle in world units (X to the right, Y up from the ground).</summary>
    public struct Box
    {
        public double X, Y, W, H;

        public Box(double x, double y, double w, double h) { X = x; Y = y; W = w; H = h; }

        /// <summary>True when the two overlap with some area. Touching edges do not count.</summary>
        public bool Intersects(Box o)
        {
            return X < o.X + o.W && o.X < X + W && Y < o.Y + o.H && o.Y < Y + H;
        }
    }
}

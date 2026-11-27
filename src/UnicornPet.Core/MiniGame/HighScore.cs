namespace UnicornPet.Core.MiniGame
{
    /// <summary>The best-score rule. The caller saves settings.json only when Submit returns true.</summary>
    public static class HighScore
    {
        public const int Max = 9999999;

        public static int Clamp(int score) { return score < 0 ? 0 : score > Max ? Max : score; }

        /// <summary>Raises the stored best to the clamped score when that is higher. True only when it changed.</summary>
        public static bool Submit(Settings s, int score)
        {
            if (s == null) return false;
            int v = Clamp(score);
            if (v <= s.GameHighScore) return false;
            s.GameHighScore = v;
            return true;
        }
    }
}

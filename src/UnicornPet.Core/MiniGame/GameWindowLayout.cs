using System;

namespace UnicornPet.Core.MiniGame
{
    public record GamePlacement(double Left, double Top);

    /// <summary>Where the game window opens: centred just above the pet, kept wholly inside the working area.</summary>
    public static class GameWindowLayout
    {
        public const double Gap = 8;

        public static GamePlacement Compute(double w, double h, double petL, double petT, double petW, double petH,
            double waL, double waT, double waW, double waH)
        {
            double left = petL + petW / 2 - w / 2;
            double top = petT - Gap - h;
            // inside the area; when the area is smaller than the window, its left/top edge wins
            left = Math.Max(waL, Math.Min(left, waL + waW - w));
            top = Math.Max(waT, Math.Min(top, waT + waH - h));
            return new GamePlacement(left, top);
        }
    }
}

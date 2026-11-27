using System;

namespace UnicornPet.Core
{
    public record BubblePlacement(double Left, double Top, double TailX, bool Beside);

    public static class BubbleLayout
    {
        const double Gap = 6;
        const double TailMargin = 16;

        static double Clamp(double v, double lo, double hi) => hi < lo ? lo : Math.Max(lo, Math.Min(hi, v));

        public static BubblePlacement Compute(double bw, double bh,
            double petL, double petT, double petW, double petH,
            double waL, double waT, double waW, double waH)
        {
            double waR = waL + waW, waB = waT + waH;
            double petCx = petL + petW / 2;

            double top = petT - bh - Gap;
            if (top >= waT)
            {
                double left = Clamp(petCx - bw / 2, waL, waR - bw);
                double tail = Clamp(petCx - left, TailMargin, bw - TailMargin);
                return new BubblePlacement(left, top, tail, false);
            }

            double l;
            if (petL + petW + Gap + bw <= waR) l = petL + petW + Gap;
            else if (petL - Gap - bw >= waL) l = petL - Gap - bw;
            else l = Clamp(petL + petW + Gap, waL, waR - bw);
            double t = Clamp(petT + 20, waT, waB - bh);
            return new BubblePlacement(l, t, 0, true);
        }
    }
}

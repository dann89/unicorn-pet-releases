using System;

namespace UnicornPet.Core
{
    /// <summary>Which pose picture is shown for a state. Shared by the sprite loader and the horn-tip table so they always agree.</summary>
    public static class PetPoses
    {
        public static readonly string[] All = { "idle", "sleep", "talk", "happy_hop", "walk_01", "walk_02", "walk_03", "walk_04" };
        static readonly string[] Walk = { "walk_01", "walk_02", "walk_03", "walk_04" };

        public static string For(PetState state, int tick)
        {
            switch (state)
            {
                case PetState.WalkLeft:
                case PetState.WalkRight: return Walk[((tick % 4) + 4) % 4];
                case PetState.Sleep: return "sleep";
                case PetState.Talk: return "talk";
                case PetState.Hop: return "happy_hop";
                default: return "idle";
            }
        }
    }

    /// <summary>
    /// Where the tip of the horn is in each pose picture, as a fraction of the picture's width and height (0,0 = top left).
    /// Measured once from the 512 x 512 poses with tools/measure-horn.py (the topmost pixel of the golden horn, plus half a pixel,
    /// divided by 512). The art is a side view facing right; facing left is the same picture mirrored by the sprite's transform.
    /// </summary>
    public static class HornTips
    {
        public static bool TryGet(string pose, out double x, out double y)
        {
            switch (pose)
            {
                case "idle": x = 0.9072; y = 0.0420; return true;       // pixel (464, 21)
                case "talk": x = 0.8994; y = 0.0303; return true;       // pixel (460, 15)
                case "happy_hop": x = 0.8623; y = 0.0400; return true;  // pixel (441, 20)
                case "walk_01": x = 0.9150; y = 0.0479; return true;    // pixel (468, 24)
                case "walk_02": x = 0.9150; y = 0.0479; return true;    // pixel (468, 24)
                case "walk_03": x = 0.9150; y = 0.0498; return true;    // pixel (468, 25)
                case "walk_04": x = 0.9131; y = 0.0459; return true;    // pixel (467, 23)
                // "sleep" (0.9189, 0.2139) is measured too, but a sleeping unicorn does not sparkle
                default: x = 0; y = 0; return false;
            }
        }

        /// <summary>
        /// Maps a point given as a fraction of the pose picture to the pet window, the same way the sprite is drawn: a square image of
        /// spriteSize, centred horizontally and standing on the window's bottom edge, scaled by (scaleX, scaleY) about its bottom centre
        /// (scaleX is negative when she faces left), then moved down by shiftY (negative while hopping). Window units, y downwards.
        /// </summary>
        public static void ToWindow(double u, double v, double spriteSize, double windowWidth, double windowHeight,
            double scaleX, double scaleY, double shiftY, out double x, out double y)
        {
            double left = (windowWidth - spriteSize) / 2, top = windowHeight - spriteSize;
            x = left + spriteSize / 2 + (u * spriteSize - spriteSize / 2) * scaleX;
            y = top + spriteSize + (v * spriteSize - spriteSize) * scaleY + shiftY;
        }
    }
}

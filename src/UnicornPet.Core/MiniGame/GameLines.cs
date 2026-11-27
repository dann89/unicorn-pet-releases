using System;
using System.Collections.Generic;

namespace UnicornPet.Core.MiniGame
{
    public enum GameMoment { Start, Milestone, Bump, NewBest }

    /// <summary>The short lines the unicorn says in the game (fixed text, from the design spec). Cheeky ones only in Cheeky mode.</summary>
    public static class GameLines
    {
        public const int MaxLength = 40;

        static readonly string[] StartAlways = { "Off we trot!", "Tiny hooves, big dreams.", "Break time is leg time." };
        static readonly string[] StartCheeky = { "Running from my inbox.", "Out of office: galloping." };
        static readonly string[] MilestoneAlways = { "Sparkling pace!", "Look at us go!", "Hooves of glory." };
        static readonly string[] MilestoneCheeky = { "Faster than the office printer.", "Quicker than a reply-all." };
        static readonly string[] BumpAlways = { "Oof. Walked into that one.", "Tripped. Nobody saw, right?", "Again? Again." };
        static readonly string[] BumpCheeky = { "I blame the deadline.", "That was a calendar clash." };
        static readonly string[] NewBestAlways = { "New best! Gold star for you.", "Personal best! Tell everyone." };
        static readonly string[] NewBestCheeky = { "Put that in your appraisal." };

        /// <summary>The lines for a moment: the "always" ones, plus the cheeky ones when cheeky is on.</summary>
        public static IReadOnlyList<string> Pool(GameMoment moment, bool cheeky)
        {
            string[] always, extra;
            switch (moment)
            {
                case GameMoment.Milestone: always = MilestoneAlways; extra = MilestoneCheeky; break;
                case GameMoment.Bump: always = BumpAlways; extra = BumpCheeky; break;
                case GameMoment.NewBest: always = NewBestAlways; extra = NewBestCheeky; break;
                default: always = StartAlways; extra = StartCheeky; break;
            }
            var list = new List<string>(always);
            if (cheeky) list.AddRange(extra);
            return list;
        }

        /// <summary>A random line for the moment; never the same as <paramref name="avoid"/> when there is another to choose.</summary>
        public static string Pick(GameMoment moment, bool cheeky, Func<double> rng01, string avoid)
        {
            var pool = new List<string>(Pool(moment, cheeky));
            if (pool.Count > 1) pool.Remove(avoid);
            double r = rng01 == null ? 0 : rng01();
            if (!(r > 0)) r = 0;   // also NaN
            if (r > 1) r = 1;
            return pool[Math.Min(pool.Count - 1, (int)(r * pool.Count))];
        }
    }
}

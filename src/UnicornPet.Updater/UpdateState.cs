using System;
using System.Collections.Generic;

namespace UnicornPet.Core.Updater
{
    public class TrialState
    {
        public string Version { get; set; }
        public int Attempts { get; set; }
        public DateTime StartedUtc { get; set; }
        public string Token { get; set; }
    }

    /// <summary>Everything the launcher and updater know, in one file (state.json), always replaced atomically.</summary>
    public class UpdateState
    {
        public string Current { get; set; }
        public string Previous { get; set; }
        public string Pending { get; set; }
        public TrialState Trial { get; set; }
        public List<string> Rejected { get; set; } = new List<string>();
    }

    public class LaunchPlan
    {
        public string Dir { get; set; }
        public string Version { get; set; }
        public string Token { get; set; }
    }
}

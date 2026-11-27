using System;
using System.Collections.Generic;

namespace UnicornPet.Core
{
    public class Settings
    {
        public bool CheekyMode { get; set; } = true;
        public bool ScheduledEnabled { get; set; } = true;
#if !WORKSAFE
        public bool StartWithWindows { get; set; } = true;
#endif

        /// <summary>Session-only: never saved, so the pet always starts awake.</summary>
        public bool Sleeping { get; set; }

        public DateTime? QuietUntil { get; set; }
#if !WORKSAFE
        public string UpdateUrl { get; set; } = "";
        public DateTime? LastUpdateCheck { get; set; }
#endif
        public string CustomName { get; set; } = "";
        public bool UseWindowsName { get; set; } = true;

        /// <summary>Whether the signed message list may be refreshed from the internet (text only, never code). On unless the person turns it off.</summary>
        public bool FetchMessages { get; set; } = true;
        public DateTime? LastMessageCheck { get; set; }

        /// <summary>Version of the last verified message list that was applied (replay protection).</summary>
        public string RemoteMessagesVersion { get; set; } = "";

        // ---------- 2.1 ----------

        /// <summary>Where the messages come from: the built-in chain, the person's own file, or an https address. Built-in by default.</summary>
        public MessageSourceKind MessageSource { get; set; } = MessageSourceKind.BuiltIn;
        /// <summary>Full path of the person's own .json file (source File).</summary>
        public string MessageFile { get; set; } = "";
        /// <summary>The https address of the person's own list (source Web).</summary>
        public string MessageUrl { get; set; } = "";
        /// <summary>The address whose list is kept in messages.custom.json; the cache is used only while it equals MessageUrl.</summary>
        public string MessageUrlCached { get; set; } = "";
        /// <summary>UTC time of the last attempt to read the web list.</summary>
        public DateTime? LastCustomFetch { get; set; }
        /// <summary>Ids of the message sections the person unticked. A section is on unless its id is here.</summary>
        public IReadOnlyList<string> SectionsOff { get; set; } = new string[0];
        /// <summary>Sparkle poop when the PC is left alone. Off by default.</summary>
        public bool IdlePoop { get; set; }
        /// <summary>Zoomies now and then by herself. Off by default.</summary>
        public bool RandomZoomies { get; set; }

        /// <summary>Best score in the mini game (0 = none yet). Saved only when beaten.</summary>
        public int GameHighScore { get; set; }

        /// <summary>How big the pet is drawn. Large is the original size.</summary>
        public PetSize PetSize { get; set; } = PetSize.Large;

        /// <summary>The unicorn's own name (not the person's). "Fifi" until changed; "" = the person cleared it (unnamed). Same rules as the person's name.</summary>
        public string PetName { get; set; } = PetNameText.DefaultName;

        /// <summary>The little glitter from the horn. On by default.</summary>
        public bool Sparkles { get; set; } = true;
    }
}

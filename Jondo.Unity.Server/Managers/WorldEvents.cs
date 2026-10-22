using System;
using System.Linq;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// Which of the client's world events are going on: what the criterion «WE» asks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Of the client's 23 (WorldEventsDataRoot), the ones this server keeps are Osavora's three
    /// seasons, «[!] Saison : Naissance» (228), «Chasse» (229) and «Repos» (230): one is always on,
    /// the week's (OsavoraSeasons). Osavora's weekly quests ask for them -- «Una pequeña comezón»
    /// (2529): «(WE=230|WE=255)&amp;(DD&gt;7,31582|PO!31582)» for the Repos, and so on --, the
    /// «perturbé» ones (252, 254, 255) beside them, which are never on here, nor are the invasions
    /// (238 to 249) or Gargandyas (250).
    /// </para>
    /// </remarks>
    public static class WorldEvents
    {
        public const int Naissance = 228, Chasse = 229, Repos = 230;

        /// <summary>The world events going on at a moment.</summary>
        public static int[] Active(DateTimeOffset now)
            => new[] { SeasonEvent(OsavoraSeasons.At(now)) };

        public static bool IsActive(int worldEvent, DateTimeOffset now) => Active(now).Contains(worldEvent);

        public static int SeasonEvent(OsavoraSeasons.Season season) => season switch
        {
            OsavoraSeasons.Season.Naissance => Naissance,
            OsavoraSeasons.Season.Chasse => Chasse,
            _ => Repos,
        };
    }
}

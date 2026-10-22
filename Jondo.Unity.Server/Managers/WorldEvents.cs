using System;
using System.Linq;
using Jondo.Unity.World.Content;

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
    /// (238 to 249). Gargandyas (250) is on every weekend (Gargandias).
    /// </para>
    /// </remarks>
    public static class WorldEvents
    {
        public const int Naissance = 228, Chasse = 229, Repos = 230;

        /// <summary>The world events going on at a moment.</summary>
        public static int[] Active(DateTimeOffset now)
            => Gargandias.IsOpen(now)
                ? new[] { SeasonEvent(OsavoraSeasons.At(now)), Gargandias.WorldEvent }
                : new[] { SeasonEvent(OsavoraSeasons.At(now)) };

        public static bool IsActive(int worldEvent, DateTimeOffset now) => Active(now).Contains(worldEvent);

        /// <summary>
        /// A resolver that answers «WE» itself and leaves the other letters to <paramref name="others"/>:
        /// Gargandias's altar wears one look a season, «{9634|||100$3;0;0;WE=229|WE=254}».
        /// </summary>
        public static Criterion.Resolver Answering(Criterion.Resolver others, DateTimeOffset now)
            => condition =>
            {
                if (condition.Code != "WE") return others?.Invoke(condition) ?? Answer.Unknown;
                bool on = IsActive((int)condition.Value, now);
                return condition.Operator switch
                {
                    '=' => on ? Answer.True : Answer.False,
                    '!' => on ? Answer.False : Answer.True,
                    _ => Answer.Unknown,
                };
            };

        public static int SeasonEvent(OsavoraSeasons.Season season) => season switch
        {
            OsavoraSeasons.Season.Naissance => Naissance,
            OsavoraSeasons.Season.Chasse => Chasse,
            _ => Repos,
        };
    }
}

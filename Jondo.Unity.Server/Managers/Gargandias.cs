using System;
using Jondo.Unity.World.Fights;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The titan Gargandias, on whose back Osavora stands: fought every weekend in his temple, after
    /// an offering at his altar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client's: the temple (subarea 1039) -- its door in Osabana (element 539214 on «Entrance to
    /// Gargandyas's Temple», 234623747), the «Offering Room» (237240320) with its grate (539273) and
    /// the altar, NPC 7752 «Ofrendas para Gargandias», the «Graogarm Room» (237241344) with its stairs
    /// (539272) and the titan's NPC 7751, and its arena (237241609); the titan, monster 8062, whose
    /// starting spell runs his phases and reads Osavora's season; the world event 250 «Gargandyas»,
    /// 3660 minutes long; the altar's words, which name each season's offering -- the raribóvoro's
    /// skin (31666) in the Naissance, the rhoarim's comb (31623) in the Chasse, the kabombz's
    /// hairball (31523) in the Repos, the three drops of Osabana's three families -- and its rule:
    /// «1 ... por el primer intento, 2 por el segundo y 3 por los intentos siguientes, hasta un
    /// máximo de 5 intentos»; the alteration 777 «Acceso al titán Gargandias».
    /// </para>
    /// <para>
    /// INFERRED: no capture enters the temple. The weekend is the guides' («du vendredi soir à 18h00 au
    /// lundi matin 8h00», up to four players a team): it ends when the season turns, Monday at 8:00
    /// in Paris, and the client's 3660 minutes put its start on Friday at 19:00. The way the rooms
    /// chain -- the offering grants the access, the grate takes it and leads to the titan's room,
    /// where the fight begins and where his NPC waits afterwards -- is read off the rooms and the
    /// NPCs' words; the cells NPCs stand and players land on are ours.
    /// </para>
    /// </remarks>
    public static class Gargandias
    {
        public const int WorldEvent = 250;

        public const long Entrance = 234623747, OfferingRoom = 237240320, GraogarmRoom = 237241344, Arena = 237241609;
        public const int TempleDoorCell = 289, GrateCell = 290, StairsCell = 518;

        /// <summary>Where one lands: in the offering room from Osabana, by the grate from the stairs, in the titan's room, back outside.</summary>
        public const int OfferingRoomArrival = 481, ByTheGrate = 318, GraogarmArrival = 478, OutsideArrival = 303;

        public const int Altar = 7752, TitanNpc = 7751, Titan = 8062;

        /// <summary>«Acceso al titán Gargandias», the offering's.</summary>
        public const int Access = 777;

        /// <summary>The most tries a week, and the most people a fight against him.</summary>
        public const int TriesAWeek = 5, PeopleCap = 4;

        /// <summary>The tally the week's tries are kept under.</summary>
        public const string TriesKind = "Gg";

        /// <summary>The weekend's span: from Friday 19:00 to Monday 8:00 in Paris, 3660 minutes.</summary>
        public static readonly TimeSpan Duration = TimeSpan.FromMinutes(3660);

        /// <summary>Whether the titan may be faced: the weekend, in Paris.</summary>
        public static bool IsOpen(DateTimeOffset now) => now >= OsavoraSeasons.NextChange(now) - Duration;

        /// <summary>The season's offering: its item.</summary>
        public static int OfferingOf(OsavoraSeasons.Season season) => season switch
        {
            OsavoraSeasons.Season.Naissance => 31666,
            OsavoraSeasons.Season.Chasse => 31623,
            _ => 31523,
        };

        /// <summary>How many the try after <paramref name="tries"/> takes: 1, 2, then 3.</summary>
        public static int CostOf(int tries) => Math.Min(tries + 1, 3);

        /// <summary>The key of a week's tries: the Osavora week it falls in.</summary>
        public static long WeekKey(DateTimeOffset now) => OsavoraSeasons.WeekOf(now);

        /// <summary>A fight against the titan: four people at most.</summary>
        public static void OnFightCreated(FightInstance fight)
        {
            if (fight.RoleplayMapId != GraogarmRoom) return;
            foreach (var monster in fight.Rojo)
            {
                if (monster.MonsterId != Titan) continue;
                fight.PeopleCap = PeopleCap;
                return;
            }
        }
    }
}

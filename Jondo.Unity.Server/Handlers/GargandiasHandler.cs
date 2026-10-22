using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;

namespace Jondo.Unity.Server.Handlers
{
    /// <summary>
    /// Gargandias's temple (<see cref="Gargandias"/>): the altar's offering, the grate it opens, the
    /// fight in the titan's room and the titan's words after it.
    /// </summary>
    /// <remarks>
    /// The altar's tree is not in the client -- its 32 sentences and 61 replies are, unpaired -- and
    /// is read off their words: «Acercarte para colocarte ante el altar» opens the season's offering,
    /// «Comprobar si tienes lo que hace falta» weighs the bag against the try's cost, «Hacer una
    /// ofrenda…» takes it and «*brilla mientras depositas…*» says it went, «Dirigirte hacia la reja…»
    /// closes. Out of the weekend the altar «*aún no ha llegado el momento propicio…*» tells what
    /// the offering «será». Each season has its own copy of every line.
    /// </remarks>
    public static class GargandiasHandler
    {
        /// <summary>A season's lines: the sentences and the replies, by try where the words count.</summary>
        private sealed record Lines(
            long Offer, long Amounts, long[] Shine, long[] Waiting, long OfferLater, long AmountsLater, long Lacking,
            long TakeNote, long Memorize, long Check, long Exhausted, long Effort,
            long[] Grate, long[] Give, long[] Search, long NoItem,
            long TakeNoteLater, long MemorizeLater, long EffortLater);

        private static readonly Dictionary<OsavoraSeasons.Season, Lines> BySeason = new()
        {
            [OsavoraSeasons.Season.Naissance] = new(58549, 58550, new long[] { 58552, 58553, 58555 }, new long[] { 0, 58554, 58556 },
                58557, 58558, 59404, 80075, 80076, 80077, 80078, 80079,
                new long[] { 80081, 80083, 80086 }, new long[] { 80082, 80085, 80088 }, new long[] { 0, 80084, 80087 }, 81245,
                80092, 80093, 80094),
            [OsavoraSeasons.Season.Chasse] = new(58559, 58560, new long[] { 58567, 58568, 58570 }, new long[] { 0, 58569, 58571 },
                58563, 58564, 59405, 80097, 80098, 80099, 80100, 80101,
                new long[] { 80117, 80119, 80122 }, new long[] { 80118, 80121, 80124 }, new long[] { 0, 80120, 80123 }, 81246,
                80109, 80110, 80111),
            [OsavoraSeasons.Season.Repos] = new(58561, 58562, new long[] { 58572, 58573, 58575 }, new long[] { 0, 58574, 58576 },
                58565, 58566, 59406, 80103, 80104, 80105, 80106, 80107,
                new long[] { 80125, 80127, 80130 }, new long[] { 80126, 80129, 80132 }, new long[] { 0, 80128, 80131 }, 81247,
                80113, 80114, 80115),
        };

        /// <summary>The altar's openings: «puede manifestarse» on the weekend, «aún no» otherwise.</summary>
        public const long Open = 58548, Approach = 80074, Leave = 80090,
                          Closed = 58551, ApproachLater = 80091, LeaveLater = 80096;

        /// <summary>The titan's «¡GRRRAAAAARRR!» and its three replies.</summary>
        public const long Roar = 58547, LeaveTemple = 80072, Stay = 80073, BackToOfferings = 80829;

        private static readonly (long, long[], (long, int)?) Close = (0, Array.Empty<long>(), null);

        /// <summary>The characters let through the grate, whose fight begins once the titan's room is loaded.</summary>
        private static readonly ConcurrentDictionary<long, bool> _goingIn = new();

        /// <summary>The server's clock, for the weekend and the season. Replaceable for the tests.</summary>
        public static Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

        public static bool Owns(int npc) => npc == Gargandias.Altar || npc == Gargandias.TitanNpc;

        private static Lines Today => BySeason[OsavoraSeasons.At(Clock())];

        private static int Tries => (int)Achievements.Tally(Gargandias.TriesKind, Gargandias.WeekKey(Clock()));

        /// <summary>What the NPC opens on.</summary>
        public static (long Question, long[] Replies) Opening(int npc)
        {
            if (npc == Gargandias.TitanNpc) return (Roar, new[] { LeaveTemple, Stay, BackToOfferings });
            return Gargandias.IsOpen(Clock()) ? (Open, new[] { Approach, Leave }) : (Closed, new[] { ApproachLater, LeaveLater });
        }

        /// <summary>
        /// A reply: the next sentence and its replies, or a sentence of zero to close -- and then,
        /// maybe, where the reply takes the character.
        /// </summary>
        public static async Task<(long Question, long[] Replies, (long Map, int Cell)? Then)> ReplyAsync(NetworkStream stream, int npc, long reply)
        {
            if (npc == Gargandias.TitanNpc)
            {
                if (reply == LeaveTemple) return (0, Array.Empty<long>(), (Gargandias.Entrance, Gargandias.OutsideArrival));
                if (reply == BackToOfferings) return (0, Array.Empty<long>(), (Gargandias.OfferingRoom, Gargandias.ByTheGrate));
                return Close;
            }

            var lines = Today;
            if (reply == ApproachLater) return (lines.OfferLater, new[] { lines.EffortLater, lines.TakeNoteLater }, null);
            if (reply == lines.EffortLater) return (lines.AmountsLater, new[] { lines.MemorizeLater }, null);
            if (!Gargandias.IsOpen(Clock())) return Close;

            if (reply == Approach || reply == lines.Memorize) return (lines.Offer, new[] { lines.Check, lines.Effort, lines.TakeNote }, null);
            if (reply == lines.Effort) return (lines.Amounts, new[] { lines.Memorize }, null);
            if (reply == lines.Check) return Weigh(lines);

            int give = Array.IndexOf(lines.Give, reply);
            if (give >= 0) return await GiveAsync(stream, lines, give);
            return Close;
        }

        /// <summary>«Comprobar si tienes lo que hace falta»: the try's cost against the bag.</summary>
        private static (long, long[], (long, int)?) Weigh(Lines lines)
        {
            int tries = Tries;
            if (tries >= Gargandias.TriesAWeek) return (lines.Offer, new[] { lines.Exhausted }, null);

            int step = Math.Min(tries, 2);
            bool enough = Equipment.HowMany(Gargandias.OfferingOf(OsavoraSeasons.At(Clock()))) >= Gargandias.CostOf(tries);
            if (step == 0) return enough ? (lines.Offer, new[] { lines.Give[0] }, null) : (lines.Lacking, new[] { lines.NoItem }, null);
            return (lines.Waiting[step], new[] { enough ? lines.Give[step] : lines.Search[step] }, null);
        }

        /// <summary>«Hacer una ofrenda…»: the offering taken, the try counted, the access given.</summary>
        private static async Task<(long, long[], (long, int)?)> GiveAsync(NetworkStream stream, Lines lines, int step)
        {
            int tries = Tries;
            if (tries >= Gargandias.TriesAWeek || Math.Min(tries, 2) != step) return Close;
            int item = Gargandias.OfferingOf(OsavoraSeasons.At(Clock()));
            if (!await Equipment.TakeAsync(stream, item, Gargandias.CostOf(tries))) return Close;

            Achievements.SetTally(Gargandias.TriesKind, Gargandias.WeekKey(Clock()), tries + 1);
            if (!Alterations.Has(SessionContext.State.CharacterId, Gargandias.Access))
                await AlterationHandler.GainAsync(stream, Gargandias.Access);
            Console.WriteLine($"[Gargandias] {SessionContext.State.CharacterName} offers {Gargandias.CostOf(tries)} x {item}: " +
                              $"try {tries + 1} of {Gargandias.TriesAWeek} this week.");
            return (lines.Shine[step], new[] { lines.Grate[step] }, null);
        }

        /// <summary>The temple's ways: its door in Osabana, the grate, the titan's room's stairs.</summary>
        public static IEnumerable<(long Map, int Cell)> Ways()
        {
            yield return (Gargandias.Entrance, Gargandias.TempleDoorCell);
            yield return (Gargandias.OfferingRoom, Gargandias.GrateCell);
            yield return (Gargandias.GraogarmRoom, Gargandias.StairsCell);
        }

        /// <summary>One of the temple's ways used.</summary>
        public static async Task UseAsync(NetworkStream stream, Interactives.Element element, int type, int skillId)
        {
            long map = SessionContext.State.MapId;
            (long Map, int Cell)? to = null;
            if (map == Gargandias.Entrance && element.Cell == Gargandias.TempleDoorCell)
                to = (Gargandias.OfferingRoom, Gargandias.OfferingRoomArrival);
            else if (map == Gargandias.GraogarmRoom && element.Cell == Gargandias.StairsCell)
                to = (Gargandias.OfferingRoom, Gargandias.ByTheGrate);
            else if (map == Gargandias.OfferingRoom && element.Cell == Gargandias.GrateCell && MayGoIn())
            {
                await AlterationHandler.LoseAsync(stream, Gargandias.Access);
                _goingIn[SessionContext.State.CharacterId] = true;
                to = (Gargandias.GraogarmRoom, Gargandias.GraogarmArrival);
            }
            if (to is not { } where) return;

            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.Push(Op.Iwn, ConnectionProtocol.BuildElementInUse(
                    element.Id, skillId, SessionContext.State.CharacterId)));
            await TeleportHandler.ReleaseAsync(stream, element.Id, skillId, element.Cell, type);
            int landed = await TeleportHandler.ToMapAsync(stream, where.Map, where.Cell);
            if (landed >= 0)
                Console.WriteLine($"[Gargandias] {SessionContext.State.CharacterName}: {map} -> {where.Map}, cell {landed}.");
        }

        /// <summary>The grate lets through whoever made the offering, while the titan may be faced.</summary>
        private static bool MayGoIn()
            => Gargandias.IsOpen(Clock()) && Alterations.Has(SessionContext.State.CharacterId, Gargandias.Access);

        /// <summary>The titan's room loaded by one the grate let through: his fight begins.</summary>
        public static async Task OnMapLoadedAsync(NetworkStream stream, long mapId)
        {
            if (mapId != Gargandias.GraogarmRoom || !_goingIn.TryRemove(SessionContext.State.CharacterId, out _)) return;
            var group = MobSpawnManager.ComposeOffMap(new[] { (Gargandias.Titan, 0) });
            if (group == null || group.Members.Count == 0) return;
            Console.WriteLine($"[Gargandias] {SessionContext.State.CharacterName} faces the titan.");
            await FightHandler.InitiateFightFromMobCollision(stream, group, mapId, arenaMapId: Gargandias.Arena);
        }
    }
}

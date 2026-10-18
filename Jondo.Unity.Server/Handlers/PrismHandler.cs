using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.World.Content;
using Jondo.Unity.World.Fights;

namespace Jondo.Unity.Server.Handlers
{
    /// <summary>
    /// The alliances' prisms: placing one with the prism item, the world's list of them, the
    /// teleport between an alliance's prisms, and attacking one, which its alliance defends from
    /// its fights window as it does its tax collectors.
    /// </summary>
    /// <remarks>
    /// All from the client: the prism item of the bag places it (its criterion
    /// «Oc=R&amp;Ox=4&amp;SL…», the right to place a prism and the subarea's level band), and the
    /// prism's own menu (PrismMenuMaker) attacks it (iez) or asks for its teleport (ifd).
    /// </remarks>
    public static class PrismHandler
    {
        /// <summary>The prism items' effect: 513, the prism.</summary>
        public const int PlaceEffect = 513;

        /// <summary>The refusals, info type 1 (datos/mensajes).</summary>
        private const int NotOnThisMap = 146;
        private const int NotInThisZone = 147;
        private const int ZoneTaken = 149;
        private const int NeedsAlliance = 400;
        private const int ZoneAlreadyYours = 403;

        /// <summary>«No puedes poner un prisma en este territorio: hay otra alianza que tiene prioridad hasta pasados {0} min.»</summary>
        private const int PriorityOfAnother = 697;

        /// <summary>«El estado del prisma es incompatible con esta acción.», info 1: attacking one that is not normal.</summary>
        private const int StateForbids = 406;

        /// <summary>«Uno de tus prismas ha sido destruido.», info 0/1581, to the alliance.</summary>
        internal const int OneOfYoursDestroyed = 1581;

        // ─── Placing ────────────────────────────────────────────────────────────

        public static bool IsPrism(int template)
            => DatabaseManager.GetItemEffectsData(DatabaseManager.GetItemTemplatePossibleEffects(template))
                              .Any(e => e.EffectId == PlaceEffect);

        /// <summary>
        /// A prism item used: the prism stands where its user stands, in a capturable subarea with
        /// none, if the item's criterion holds there. False when the item is no prism.
        /// </summary>
        public static async Task<bool> TryPlaceAsync(NetworkStream stream, Equipment.Item item)
        {
            if (!IsPrism(item.Template)) return false;

            var me = SessionContext.State;
            var alliance = AllianceStore.AllianceOf(me.CharacterId);
            var map = MapManager.GetMapInfo(me.MapId);
            int refusal = WhyNotHere(alliance, me.CharacterId, map, item.Template, out bool rights);
            if (!rights)
            {
                await SendAsync(stream, ConnectionProtocol.Push(Op.Hla,
                    TaxCollectorProtocol.BuildError(TaxCollectorProtocol.ErrorNoRights)));
                return true;
            }
            if (refusal > 0)
            {
                await SendAsync(stream, ConnectionProtocol.Push(Op.Lqn,
                    ConnectionProtocol.BuildInfoMessage(InfoMessages.Warning, refusal)));
                return true;
            }

            // A conquest won: the winner alone, for a while (ConquestHandler.BirthOf).
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var priority = Conquests.PriorityIn(map.SubAreaId, now);
            if (priority != null && priority.AllianceId != alliance.Id)
            {
                long minutes = Math.Max(1, (priority.UntilUtcMs - now + 59_999) / 60_000);
                await SendAsync(stream, ConnectionProtocol.Push(Op.Lqn,
                    ConnectionProtocol.BuildInfoMessage(InfoMessages.Warning, PriorityOfAnother, minutes.ToString())));
                return true;
            }
            var (state, nextChange) = ConquestHandler.BirthOf(alliance.Id, map.SubAreaId, now);

            var prism = PrismStore.Place(alliance.Id, map.SubAreaId, me.MapId, me.CellId, me.Orientation, state, nextChange);
            if (prism == null)
            {
                await SendAsync(stream, ConnectionProtocol.Push(Op.Lqn,
                    ConnectionProtocol.BuildInfoMessage(InfoMessages.Warning, ZoneTaken)));
                return true;
            }
            await ItemUseHandler.SpendOneAsync(stream, item.Uid);

            byte[] shown = ConnectionProtocol.Push(Op.Jsn, PrismProtocol.BuildShown(prism, alliance));
            await SendAsync(stream, shown);
            await SessionRegistry.BroadcastToMapAsync(prism.MapId, shown, SessionContext.Current.Id, fightId: 0);
            await SessionRegistry.BroadcastAsync(ConnectionProtocol.Push(Op.Ifa,
                PrismProtocol.BuildChanged(PrismProtocol.Information(prism, alliance))));

            Program.LogDebug($"[Prism] {me.CharacterName} places a prism of «{alliance.Name}» in subarea " +
                             $"{prism.SubAreaId}, map {prism.MapId}.");
            return true;
        }

        /// <summary>
        /// Why a prism cannot go here: an info message of type 1, or 0 when it can.
        /// <paramref name="rights"/> says whether the alliance right was there at all.
        /// </summary>
        internal static int WhyNotHere(AllianceStore.Alliance alliance, long characterId, MapInfo map, int item,
                                       out bool rights)
        {
            rights = true;
            if (alliance == null) return NeedsAlliance;
            if (!AllianceStore.HasRight(characterId, AllianceData.PlacePrism))
            {
                rights = false;
                return 0;
            }
            if (map == null || !map.Outdoor || map.SubAreaId <= 0 || DungeonManager.IsRoom(map.MapId)
                || Dreams.IsDreamMap(map.MapId) || GuildRaidManager.IsRaidMap(map.MapId))
                return NotOnThisMap;
            if (!Interactives.IsCapturable(map.SubAreaId)) return NotInThisZone;
            if (!CriterionHolds(DatabaseManager.GetItemTemplateCriterion(item), characterId, map.SubAreaId))
                return NotInThisZone;

            var standing = PrismStore.InSubArea(map.SubAreaId);
            if (standing != null) return standing.AllianceId == alliance.Id ? ZoneAlreadyYours : ZoneTaken;
            return 0;
        }

        /// <summary>
        /// The prism item's criterion: Ox the alliance right, SL the subarea's level, and Oc -- the
        /// one letter the client keeps hidden -- read as the subarea being capturable, which the
        /// prism needs and the caller has already checked.
        /// </summary>
        internal static bool CriterionHolds(string criterion, long characterId, int subAreaId)
        {
            if (string.IsNullOrWhiteSpace(criterion)) return true;
            int level = Interactives.LevelOfSubArea(subAreaId);
            return Criterion.Met(criterion, condition => condition.Code switch
            {
                "Ox" => condition.Operator == '='
                    ? (AllianceStore.HasRight(characterId, (int)condition.Value) ? Answer.True : Answer.False)
                    : Answer.Unknown,
                "SL" => Criterion.Compare(condition.Operator, level, condition.Value),
                "Oc" => Interactives.IsCapturable(subAreaId) ? Answer.True : Answer.False,
                _ => Answer.Unknown,
            });
        }

        // ─── The world's prisms ─────────────────────────────────────────────────

        /// <summary>Every prism of the world at world entry (ife), for the world map.</summary>
        public static async Task OnWorldEntryAsync(NetworkStream stream)
        {
            var prisms = new List<Pb>();
            foreach (var prism in PrismStore.All())
            {
                var alliance = AllianceStore.ById(prism.AllianceId);
                if (alliance != null) prisms.Add(PrismProtocol.Information(prism, alliance));
            }
            await SendAsync(stream, ConnectionProtocol.Push(Op.Ife, PrismProtocol.BuildAll(prisms)));
        }

        /// <summary>The map's prism among its actors, unless it is being fought.</summary>
        public static void AddToMap(Pb jss, long mapId)
        {
            var prism = PrismStore.OnMap(mapId);
            if (prism == null || FightHandler.IsGroupFighting(prism.ContextId)) return;
            var alliance = AllianceStore.ById(prism.AllianceId);
            if (alliance == null) return;
            jss.Msg(5, PrismProtocol.Actor(prism, alliance));
        }

        /// <summary>
        /// Talking to it (iov 3 on its actor): the dialogue opens (ioc) and asks the prism's
        /// question (iot, empty). False when the actor is no prism.
        /// </summary>
        /// <remarks>
        /// The client writes the sentence itself (NpcDialog.OnAlliancePrismDialogQuestion): the
        /// subarea's prism from the ife, its alliance and its state. Unanswered, «Speak» did nothing.
        /// Its «Leave» is the kla, closed like a tax collector's (TaxCollectorHandler.CloseTalkAsync).
        /// </remarks>
        public static async Task<bool> TalkAsync(NetworkStream stream, long mapId, long contextId)
        {
            var prism = PrismStore.ByContextId(contextId);
            if (prism == null || prism.MapId != mapId) return false;
            if (AllianceStore.ById(prism.AllianceId) == null) return false;

            SessionContext.State.OpenActorDialogue = contextId;
            await SendAsync(stream, ConnectionProtocol.Push(Op.Ioc, ConnectionProtocol.BuildNpcDialog(mapId, contextId)));
            await SendAsync(stream, ConnectionProtocol.Push(Op.Iot, Array.Empty<byte>()));
            return true;
        }

        /// <summary>An alliance's prisms, as its sheet (mcx) lists them in its f2.</summary>
        public static IEnumerable<Pb> InformationOf(AllianceStore.Alliance alliance)
            => PrismStore.OfAlliance(alliance.Id).Select(p => PrismProtocol.Information(p, alliance));

        // ─── Teleport ───────────────────────────────────────────────────────────

        /// <summary>
        /// The prism's «Teleport» (ifd, empty): the alliance's other prisms as a teleporter's list
        /// (hjj), charged as the zaap charges. Only from a prism of one's own alliance.
        /// </summary>
        public static async Task TeleportListAsync(NetworkStream stream)
        {
            var me = SessionContext.State;
            var here = PrismStore.OnMap(me.MapId);
            var alliance = AllianceStore.AllianceOf(me.CharacterId);
            if (here == null || alliance == null || here.AllianceId != alliance.Id) return;

            me.OpenZaapMapId = me.MapId;
            var destinations = new List<ConnectionProtocol.ZaapDestination>();
            foreach (var prism in PrismStore.OfAlliance(alliance.Id))
            {
                if (MapManager.GetMapInfo(prism.MapId) == null) continue;
                destinations.Add(new ConnectionProtocol.ZaapDestination(
                    prism.MapId, prism.SubAreaId, Interactives.LevelOfSubArea(prism.SubAreaId),
                    prism.MapId == me.MapId ? 0 : ZaapTravelHandler.CostBetween(me.MapId, prism.MapId),
                    PrismProtocol.DestinationKind));
            }
            await SendAsync(stream, ConnectionProtocol.Push(Op.Hjj, ConnectionProtocol.BuildZaapList(
                0, destinations, PrismProtocol.Teleporter)));
        }

        /// <summary>Whether a teleport chosen from a prism's list goes to one of the alliance's prisms.</summary>
        internal static bool IsDestination(long characterId, long mapId)
        {
            var alliance = AllianceStore.AllianceOf(characterId);
            var prism = PrismStore.OnMap(mapId);
            return alliance != null && prism != null && prism.AllianceId == alliance.Id;
        }

        // ─── Attacking ──────────────────────────────────────────────────────────

        /// <summary>
        /// The prism's «Attack» (iez, empty: a subarea has one prism and the map names it): a
        /// fight against the prism, which its alliance hears of (iex) and may come to defend.
        /// </summary>
        public static async Task AttackAsync(NetworkStream stream)
        {
            var me = SessionContext.State;
            var prism = PrismStore.OnMap(me.MapId);
            if (prism == null || me.IsInFight || me.FightId != 0) return;
            if (FightHandler.IsGroupFighting(prism.ContextId)) return;
            var alliance = AllianceStore.ById(prism.AllianceId);
            var mine = AllianceStore.AllianceOf(me.CharacterId);
            if (alliance == null || mine == null || mine.Id == prism.AllianceId) return;

            // Only a normal prism is fought: a weakened one waits for its conquest, a vulnerable
            // one is being conquered, a pacified one is protected (Translations 1149320).
            if (prism.State != PrismStore.Normal)
            {
                await SendAsync(stream, ConnectionProtocol.Push(Op.Lqn,
                    ConnectionProtocol.BuildInfoMessage(InfoMessages.Warning, StateForbids)));
                return;
            }

            var monster = MobSpawnManager.GetMonsterData(PrismProtocol.Monster);
            if (monster == null || monster.Grades.Count == 0)
            {
                Program.LogDebug($"[Prism] Monster {PrismProtocol.Monster} is not loaded: no fight.");
                return;
            }

            var alone = new MobSpawnManager.MobGroup
            {
                MobId = prism.ContextId,
                CellId = prism.CellId,
                Orientation = prism.Direction,
                Members = { new MobSpawnManager.MobMember { Monster = monster, GradeIndex = 0, Level = monster.Grades[0].Level } },
            };
            await FightHandler.InitiateFightFromMobCollision(stream, alone, prism.MapId, rules: FightRules.ContraPrisma);

            var fight = FightHandler.FightOf(me.CharacterId);
            if (fight == null || AllianceFights.Of(fight) == null) return;

            await AllianceHandler.TellMembersAsync(alliance, ConnectionProtocol.Push(Op.Iex,
                PrismProtocol.BuildAttacked(PrismProtocol.Information(prism, alliance))));
            await AllianceFights.AnnounceAsync(fight);
            Program.LogDebug($"[Prism] The prism of «{alliance.Name}» in subarea {prism.SubAreaId} attacked: " +
                             $"fight #{fight.FightId}.");
        }

        /// <summary>A fight opening against a prism: its line in the alliance's fights, its look.</summary>
        internal static void OnFightCreated(FightInstance fight)
        {
            if (fight.Reglas != FightRules.ContraPrisma) return;
            var prism = PrismStore.ByContextId(fight.DefenderLeaderId);
            if (prism == null) return;
            var alliance = AllianceStore.ById(prism.AllianceId);
            foreach (var fighter in fight.Rojo.Where(f => f.IsMonster && f.MonsterId == PrismProtocol.Monster))
                fighter.Look = PrismProtocol.LookString(alliance);
            AllianceFights.Open(fight, TaxCollectorProtocol.FightOfPrism, prism.MapId, prism.AllianceId,
                                () => PrismProtocol.Fighter(prism, alliance));
        }

        /// <summary>
        /// A prism's fight over: beaten, it stands weakened until its conquest
        /// (<see cref="ConquestHandler.WeakenAsync"/>); standing, it goes back on its map. Its
        /// alliance hears how (iev).
        /// </summary>
        internal static async Task FightEndedAsync(FightInstance fight, bool attackersWon)
        {
            var prism = PrismStore.ByContextId(fight.DefenderLeaderId);
            if (prism == null) return;
            var alliance = AllianceStore.ById(prism.AllianceId);

            if (attackersWon)
            {
                var attackers = fight.Azul.Where(f => !f.IsMonster && !f.EsInvocado)
                                          .Select(f => AllianceStore.AllianceOf(f.Id)?.Id ?? 0)
                                          .Where(id => id != 0);
                await ConquestHandler.WeakenAsync(prism, attackers.ToList(), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                Program.LogDebug($"[Prism] The prism of subarea {prism.SubAreaId} falls in fight #{fight.FightId}.");
            }
            if (alliance == null) return;

            await AllianceHandler.TellMembersAsync(alliance, ConnectionProtocol.Push(Op.Iev,
                PrismProtocol.BuildFightResult(attackersWon ? PrismProtocol.Weakened : PrismProtocol.NotDefeated,
                                               PrismProtocol.Information(prism, alliance))));
            if (!attackersWon)
                await SessionRegistry.BroadcastToMapAsync(prism.MapId, ConnectionProtocol.Push(Op.Jsn,
                    PrismProtocol.BuildShown(prism, alliance)), fightId: 0);
        }

        /// <summary>A prism gone: off its map (kmu) and off the world's list (iew).</summary>
        internal static async Task GoneAsync(PrismStore.Prism prism)
        {
            await SessionRegistry.BroadcastToMapAsync(prism.MapId, ConnectionProtocol.Push(Op.Kmu,
                FightJoinProtocol.BuildActorHidden(prism.ContextId)), fightId: 0);
            await SessionRegistry.BroadcastAsync(ConnectionProtocol.Push(Op.Iew, PrismProtocol.BuildGone(prism.SubAreaId)));
        }

        /// <summary>An alliance dissolved: its prisms go.</summary>
        internal static async Task AllianceDissolvedAsync(long allianceId)
        {
            foreach (var prism in PrismStore.OfAlliance(allianceId))
            {
                PrismStore.Remove(prism);
                await GoneAsync(prism);
            }
        }

        private static async Task SendAsync(NetworkStream stream, byte[] frame)
        {
            if (stream == null) return;
            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, frame);
        }
    }
}

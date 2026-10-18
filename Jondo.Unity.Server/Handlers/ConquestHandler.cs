using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.World.Fights;

namespace Jondo.Unity.Server.Handlers
{
    /// <summary>
    /// A prism's life after it is beaten: weakened, then vulnerable while its zone's conquest,
    /// the King of the Hill, is fought, and then the defence's again or the winner's to take.
    /// </summary>
    /// <remarks>
    /// The cycle is the client's help (Translations 354323): a prism beaten in a fight turns
    /// weakened and becomes vulnerable at the defence's hour, 24 hours on at least
    /// (<see cref="ServerSettings.PrismWeakenedHours"/>); while vulnerable the conquest is fought
    /// in its zone, and the alliance that wins it may place its prism instead -- alone for thirty
    /// minutes, the prism then pacified a while (1149317) --; the defence winning puts its prism
    /// back to normal.
    ///
    /// The conquest is the players' of the zone with the alliance window's «Alliance fight» on:
    /// their status turns AvA aggressable (3) while they are there, which is what makes the
    /// client offer «Agredir» on an enemy (PlayerMenuMaker.AllowAggression: the subarea's prism
    /// vulnerable, both at 3, other alliances). The client opens the conquest window itself on
    /// learning its zone's prism is vulnerable (ku.fhn → fud), and the mep fills it each minute.
    /// </remarks>
    public static class ConquestHandler
    {
        /// <summary>The notices, info type 2 (datos/mensajes): «Tu territorio «$subarea{0}» es vulnerable. ¡Defiéndelo!» and the rest.</summary>
        private const int YourTerritoryVulnerable = 86;
        private const int TerritoryVulnerable = 89;
        private const int TerritoryWeakened = 94;
        private const int YourTerritoryWeakened = 95;

        /// <summary>«Tu alianza es la única que puede colocar un prisma en el territorio $subarea{0} durante {1} minuto(s).», info 0.</summary>
        private const int OnlyYoursMayPlace = 1567;

        /// <summary>«Imposible: no eres un luchador AcA válido.», info 1.</summary>
        private const int NotAnAvaFighter = 421;

        /// <summary>«No es posible activar/desactivar el modo AcA en una zona donde se desarrolla un combate de alianza.», info 1.</summary>
        internal const int NoSwitchInAWar = 399;

        /// <summary>The PvP notices' info type.</summary>
        private const int PvpNotice = 2;

        // ─── The clock ──────────────────────────────────────────────────────────

        /// <summary>Every few seconds, from Program: the prisms whose time has come change state.</summary>
        public static Task TickAsync() => TickAsync(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        internal static async Task TickAsync(long nowMs)
        {
            foreach (var prism in PrismStore.All())
            {
                try
                {
                    if (prism.State == PrismStore.Weakened && nowMs >= prism.NextChangeUtcMs)
                        await BecomeVulnerableAsync(prism, nowMs);
                    else if (prism.State == PrismStore.Vulnerable)
                        await ConquestMinuteAsync(prism, nowMs);
                    else if (prism.State == PrismStore.Pacified && nowMs >= prism.NextChangeUtcMs)
                        await ChangeStateAsync(prism, PrismStore.Normal, 0);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Conquest] The prism of subarea {prism.SubAreaId} could not move on: {ex.Message}");
                }
            }

            foreach (var war in Conquests.All())
                if (PrismStore.InSubArea(war.SubAreaId)?.State != PrismStore.Vulnerable) Conquests.End(war.SubAreaId);
        }

        /// <summary>A prism's state changed: the world's list (ifa) and its map (jsn) learn it.</summary>
        internal static async Task ChangeStateAsync(PrismStore.Prism prism, int state, long nextChangeUtcMs)
        {
            PrismStore.SetState(prism, state, nextChangeUtcMs);
            await ShowChangedAsync(prism);
        }

        internal static async Task ShowChangedAsync(PrismStore.Prism prism)
        {
            var alliance = AllianceStore.ById(prism.AllianceId);
            if (alliance == null) return;
            await SessionRegistry.BroadcastAsync(ConnectionProtocol.Push(Op.Ifa,
                PrismProtocol.BuildChanged(PrismProtocol.Information(prism, alliance))));
            if (!FightHandler.IsGroupFighting(prism.ContextId))
                await SessionRegistry.BroadcastToMapAsync(prism.MapId, ConnectionProtocol.Push(Op.Jsn,
                    PrismProtocol.BuildShown(prism, alliance)), fightId: 0);
        }

        // ─── Weakened ───────────────────────────────────────────────────────────

        /// <summary>
        /// A prism beaten: weakened until the vulnerability, its alliance warned (95) and the
        /// attackers' too (94).
        /// </summary>
        internal static async Task WeakenAsync(PrismStore.Prism prism, IEnumerable<long> attackerAlliances, long nowMs)
        {
            long vulnerable = nowMs + Math.Max(0, ServerSettings.Current.PrismWeakenedHours) * 3_600_000L;
            await ChangeStateAsync(prism, PrismStore.Weakened, vulnerable);

            var owner = AllianceStore.ById(prism.AllianceId);
            if (owner != null)
                await AllianceHandler.TellMembersAsync(owner, Notice(YourTerritoryWeakened, prism.SubAreaId));
            foreach (long id in attackerAlliances.Distinct().Where(a => a != prism.AllianceId))
            {
                var attacker = AllianceStore.ById(id);
                if (attacker != null) await AllianceHandler.TellMembersAsync(attacker, Notice(TerritoryWeakened, prism.SubAreaId));
            }
            Program.LogDebug($"[Conquest] The prism of subarea {prism.SubAreaId} is weakened until " +
                             $"{DateTimeOffset.FromUnixTimeMilliseconds(vulnerable):u}.");
        }

        // ─── Vulnerable: the conquest ───────────────────────────────────────────

        /// <summary>
        /// The hour come: the prism vulnerable and its conquest open. Its alliance is told to
        /// defend it (86), every other alliance's fighters to take it (89), and whoever is in the
        /// zone turns aggressable.
        /// </summary>
        internal static async Task BecomeVulnerableAsync(PrismStore.Prism prism, long nowMs)
        {
            long ends = nowMs + Math.Max(Conquests.DominationToWin, ServerSettings.Current.ConquestMaxMinutes) * 60_000L;
            await ChangeStateAsync(prism, PrismStore.Vulnerable, ends);
            Conquests.Start(prism.SubAreaId, prism.AllianceId, nowMs, ends);

            byte[] defend = Notice(YourTerritoryVulnerable, prism.SubAreaId);
            byte[] take = Notice(TerritoryVulnerable, prism.SubAreaId);
            foreach (var session in SessionRegistry.InWorld())
            {
                long id = session.State.CharacterId;
                var alliance = id == 0 ? null : AllianceStore.AllianceOf(id);
                if (alliance == null) continue;
                if (alliance.Id == prism.AllianceId) await session.SendAsync(defend);
                else if (AllianceStore.TakesPartInAva(id)) await session.SendAsync(take);
            }

            await RefreshZoneAsync(prism.SubAreaId);
            Program.LogDebug($"[Conquest] The prism of subarea {prism.SubAreaId} is vulnerable: its conquest is open.");
        }

        /// <summary>The conquest's minute, when it is due: the score counted and sent, and maybe its end.</summary>
        internal static async Task ConquestMinuteAsync(PrismStore.Prism prism, long nowMs)
        {
            var war = Conquests.In(prism.SubAreaId)
                      ?? Conquests.Start(prism.SubAreaId, prism.AllianceId, nowMs,
                                         prism.NextChangeUtcMs > nowMs
                                             ? prism.NextChangeUtcMs
                                             : nowMs + Math.Max(Conquests.DominationToWin, ServerSettings.Current.ConquestMaxMinutes) * 60_000L);
            if (nowMs < war.NextTickUtcMs) return;

            long winner = Conquests.Tick(war, PresentIn(prism.SubAreaId), nowMs);
            if (winner == 0 && nowMs >= war.EndsUtcMs) winner = war.DefenderId;
            await SendScoreAsync(war);
            if (winner != 0) await EndAsync(prism, war, winner, nowMs);
        }

        /// <summary>
        /// The conquest over (mdz to the zone): the defence winning keeps its prism, normal again;
        /// another alliance winning, the prism falls (1581 to its alliance) and the winner has the
        /// zone alone for thirty minutes (1567).
        /// </summary>
        private static async Task EndAsync(PrismStore.Prism prism, Conquests.War war, long winnerId, long nowMs)
        {
            var winner = AllianceStore.ById(winnerId);
            if (winner != null)
            {
                byte[] end = ConnectionProtocol.Push(Op.Mdz, KothProtocol.BuildEnd(winner));
                foreach (var session in FightersIn(war.SubAreaId)) await session.SendAsync(end);
            }
            Conquests.End(war.SubAreaId);

            if (winnerId == prism.AllianceId || winner == null)
            {
                await ChangeStateAsync(prism, PrismStore.Normal, 0);
                Program.LogDebug($"[Conquest] The defence keeps subarea {prism.SubAreaId}.");
            }
            else
            {
                PrismStore.Remove(prism);
                await PrismHandler.GoneAsync(prism);
                var loser = AllianceStore.ById(prism.AllianceId);
                if (loser != null)
                    await AllianceHandler.TellMembersAsync(loser, ConnectionProtocol.Push(Op.Lqn,
                        ConnectionProtocol.BuildSystemMessage(PrismHandler.OneOfYoursDestroyed)));

                Conquests.GivePriority(war.SubAreaId, winnerId, nowMs + Conquests.PriorityMinutes * 60_000L);
                await AllianceHandler.TellMembersAsync(winner, ConnectionProtocol.Push(Op.Lqn,
                    ConnectionProtocol.BuildSystemMessage(OnlyYoursMayPlace, war.SubAreaId.ToString(),
                                                          Conquests.PriorityMinutes.ToString())));
                Program.LogDebug($"[Conquest] «{winner.Name}» conquers subarea {war.SubAreaId}.");
            }

            await RefreshZoneAsync(war.SubAreaId);
        }

        /// <summary>The score (mep) to the zone's fighters.</summary>
        private static async Task SendScoreAsync(Conquests.War war)
        {
            byte[] score = ScoreFrame(war);
            foreach (var session in FightersIn(war.SubAreaId)) await session.SendAsync(score);
        }

        private static byte[] ScoreFrame(Conquests.War war)
            => ConnectionProtocol.Push(Op.Mep, KothProtocol.BuildUpdate(war, AllianceStore.ById, AllianceData.DefaultKothRole));

        // ─── Who is there ───────────────────────────────────────────────────────

        /// <summary>Where a session stands in the world: its fight's map when it fights.</summary>
        private static long WhereIs(GameSession session)
            => session.State.IsInFight && session.State.RoleplayMapId != 0 ? session.State.RoleplayMapId : session.State.MapId;

        private static int SubAreaOf(long mapId) => MapManager.GetMapInfo(mapId)?.SubAreaId ?? 0;

        /// <summary>Whether a subarea's prism is vulnerable: a conquest is fought there.</summary>
        public static bool IsWarZone(int subAreaId)
            => subAreaId > 0 && PrismStore.InSubArea(subAreaId)?.State == PrismStore.Vulnerable;

        /// <summary>Whether a connected character stands in a conquest's zone.</summary>
        public static bool InWarZone(long characterId)
        {
            var session = SessionRegistry.FindByCharacter(characterId);
            return session != null && IsWarZone(SubAreaOf(WhereIs(session)));
        }

        /// <summary>The zone's fighters: in the world on its maps, in an alliance, with «Alliance fight» on.</summary>
        private static List<GameSession> FightersIn(int subAreaId)
            => SessionRegistry.InWorld()
                              .Where(s => s.State.CharacterId != 0 && SubAreaOf(WhereIs(s)) == subAreaId
                                          && AllianceStore.TakesPartInAva(s.State.CharacterId)
                                          && AllianceStore.AllianceOf(s.State.CharacterId) != null)
                              .ToList();

        /// <summary>Who is there for the minute's count: each fighter of the zone, his alliance and map.</summary>
        internal static List<Conquests.Presence> PresentIn(int subAreaId)
        {
            var present = new List<Conquests.Presence>();
            foreach (var session in FightersIn(subAreaId))
            {
                var alliance = AllianceStore.AllianceOf(session.State.CharacterId);
                if (alliance != null) present.Add(new Conquests.Presence(alliance.Id, WhereIs(session), session.State.CharacterId));
            }
            return present;
        }

        // ─── The fighters' status ───────────────────────────────────────────────

        /// <summary>The status each character last heard (hon), to tell him only when it changes.</summary>
        private static readonly ConcurrentDictionary<long, int> _heard = new();

        /// <summary>
        /// A character on a new map: if «Alliance fight» is on and his status changed -- he walked
        /// into a conquest's zone or out of it -- he hears it (hon) and is drawn again; walking in,
        /// the zone's prism (ifa), which opens the conquest window, and its score (mep).
        /// </summary>
        public static async Task OnMapEnteredAsync(NetworkStream stream, long characterId, int subAreaId)
        {
            if (characterId == 0 || !AllianceStore.TakesPartInAva(characterId)) return;
            if (AllianceStore.AllianceOf(characterId) == null) return;

            int status = AllianceProtocol.AggressableStatus(characterId);
            int before = _heard.TryGetValue(characterId, out int heard) ? heard : AllianceProtocol.AvaNotAggressable;
            if (status == before) return;
            _heard[characterId] = status;
            await SendAsync(stream, ConnectionProtocol.Push(Op.Hon, AllianceProtocol.BuildAggressableStatus(characterId, status)));

            if (status != AllianceProtocol.AvaAggressable) return;
            var prism = PrismStore.InSubArea(subAreaId);
            var owner = prism == null ? null : AllianceStore.ById(prism.AllianceId);
            if (owner != null)
                await SendAsync(stream, ConnectionProtocol.Push(Op.Ifa,
                    PrismProtocol.BuildChanged(PrismProtocol.Information(prism, owner))));
            var war = Conquests.In(subAreaId);
            if (war != null) await SendAsync(stream, ScoreFrame(war));
        }

        /// <summary>A conquest opened or closed: the zone's fighters hear their new status and are drawn again.</summary>
        private static async Task RefreshZoneAsync(int subAreaId)
        {
            foreach (var session in FightersIn(subAreaId))
            {
                long id = session.State.CharacterId;
                int status = AllianceProtocol.AggressableStatus(id);
                _heard[id] = status;
                await session.SendAsync(ConnectionProtocol.Push(Op.Hon, AllianceProtocol.BuildAggressableStatus(id, status)));
                if (session.State.IsInFight) continue;

                var character = DatabaseManager.GetCharacterById(id);
                if (character == null) continue;
                byte[] jsn = ConnectionProtocol.Push(Op.Jsn, ConnectionProtocol.BuildActorRefreshed(
                    character, session.State.CellId, session.State.Orientation, session.AccountId));
                await session.SendAsync(jsn);
                await SessionRegistry.BroadcastToMapAsync(session.MapId, jsn, session.Id);
            }
        }

        /// <summary>A character gone from the world: his next status is heard afresh.</summary>
        public static void Forget(long characterId) => _heard.TryRemove(characterId, out _);

        // ─── Aggressions ────────────────────────────────────────────────────────

        /// <summary>
        /// «Agredir» on a player (the hph without its f2): in a conquest's zone, between fighters
        /// of two alliances, the fight starts at once, an aggression. Whoever is not a fighter of
        /// the zone hears so (421).
        /// </summary>
        public static async Task AggressAsync(NetworkStream stream, long targetId)
        {
            var me = SessionContext.Current;
            long myId = me.State.CharacterId;
            var target = SessionRegistry.FindByCharacter(targetId);
            if (myId == 0 || target == null || !target.IsInWorld || target.MapId != me.State.MapId) return;
            if (me.State.IsInFight || me.State.FightId != 0 || target.State.IsInFight || target.State.FightId != 0) return;

            var mine = AllianceStore.AllianceOf(myId);
            if (mine == null || !AllianceStore.TakesPartInAva(myId) || !IsWarZone(SubAreaOf(me.State.MapId)))
            {
                await SendAsync(stream, ConnectionProtocol.Push(Op.Lqn,
                    ConnectionProtocol.BuildInfoMessage(InfoMessages.Warning, NotAnAvaFighter)));
                return;
            }
            var theirs = AllianceStore.AllianceOf(targetId);
            if (theirs == null || theirs.Id == mine.Id || !AllianceStore.TakesPartInAva(targetId)) return;

            Program.LogDebug($"[Conquest] {myId} of «{mine.Name}» attacks {targetId} of «{theirs.Name}».");
            await FightHandler.InitiatePvpAsync(new List<GameSession> { me }, new List<GameSession> { target },
                                                me.State.MapId, rules: FightRules.Agresion);
        }

        /// <summary>An aggression over: the zone's conquest counts it -- points won and lost -- and its score goes out.</summary>
        internal static async Task AggressionEndedAsync(FightInstance fight, bool attackersWon)
        {
            var war = Conquests.In(SubAreaOf(fight.RoleplayMapId));
            if (war == null) return;

            List<long> AlliancesOf(IEnumerable<Fighter> side)
                => side.Where(f => !f.IsMonster && !f.EsInvocado)
                       .Select(f => AllianceStore.AllianceOf(f.Id)?.Id ?? 0)
                       .Where(id => id != 0)
                       .ToList();

            var winners = AlliancesOf(attackersWon ? fight.Azul : fight.Rojo);
            var losers = AlliancesOf(attackersWon ? fight.Rojo : fight.Azul);
            Conquests.FightOver(war, winners, losers);
            await SendScoreAsync(war);
        }

        // ─── Placing after a conquest ───────────────────────────────────────────

        /// <summary>
        /// The state a new prism is born in: pacified when its alliance won the zone's conquest --
        /// for the setting's hours over the prisms it holds -- and the priority spent; normal otherwise.
        /// </summary>
        internal static (int State, long NextChangeUtcMs) BirthOf(long allianceId, int subAreaId, long nowMs)
        {
            var priority = Conquests.PriorityIn(subAreaId, nowMs);
            if (priority == null || priority.AllianceId != allianceId) return (PrismStore.Normal, 0);
            Conquests.TakePriority(subAreaId);
            long pacified = Conquests.InvulnerabilityMs(ServerSettings.Current.ConquestInvulnerabilityHours,
                                                        PrismStore.OfAlliance(allianceId).Count + 1);
            return pacified <= 0 ? (PrismStore.Normal, 0) : (PrismStore.Pacified, nowMs + pacified);
        }

        private static byte[] Notice(int id, int subAreaId)
            => ConnectionProtocol.Push(Op.Lqn, ConnectionProtocol.BuildInfoMessage(PvpNotice, id, subAreaId.ToString()));

        private static async Task SendAsync(NetworkStream stream, byte[] frame)
        {
            if (stream == null) return;
            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, frame);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;

namespace Jondo.Unity.Server.Handlers
{
    /// <summary>
    /// The automatic dungeon search (DungeonMatchmaking): the window's state, searching and
    /// stopping, the ready check and its yes and no, and the group made.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The capture's order is followed: the search answered with kjb and kkk; the ready check
    /// (kif) to everybody, again after every yes; the yes answered with kjv; and once all have said
    /// it, the party (ing), the group made (kjl), the key spent and the first room.
    /// </para>
    /// <para>
    /// And the group once made (see <see cref="Matchmaking"/>): its votes to give up the dungeon
    /// (kjs → kig, kkq; kjr while one may not start) and to put somebody out (ili, ikn → ikw,
    /// ike; iku while one may not start), the hour without searching for leaving it full (kjo)
    /// or being put out of it (kjb's ban). Read off the client, not captured.
    /// </para>
    /// <para>
    /// Not done: completing a party already inside (the search's third kind).
    /// </para>
    /// </remarks>
    public static class MatchmakingHandler
    {
        private static Timer _ticker;
        private static readonly object _tickerLock = new();
        private static int _ticking;

        private static Task WriteAsync(NetworkStream stream, byte[] frame)
            => Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream, frame);

        private static async Task TellAsync(long characterId, byte[] frame)
        {
            var session = SessionRegistry.FindByCharacter(characterId);
            if (session == null) return;
            try
            {
                await session.SendAsync(frame);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Matchmaking] A notice could not reach {characterId}: {ex.Message}");
            }
        }

        private static int LevelOf(long characterId)
        {
            if (characterId == SessionContext.State.CharacterId && GameState.CharacterLevel > 0) return GameState.CharacterLevel;
            return DatabaseManager.GetCharacterById(characterId)?.Level ?? 0;
        }

        /// <summary>A minute's worth of checks: offers that ran out, and new groups.</summary>
        private static void EnsureTicking()
        {
            if (_ticker != null) return;
            lock (_tickerLock)
                _ticker ??= new Timer(_ => _ = TickAsync(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        private static async Task TickAsync()
        {
            if (Interlocked.Exchange(ref _ticking, 1) == 1) return;
            try
            {
                await ExpireAsync(DateTime.UtcNow);
                await TryMatchAsync(DateTime.UtcNow);
                await ExpireVotesAsync(DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Matchmaking] The search's tick failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _ticking, 0);
            }
        }

        // ─── The window ─────────────────────────────────────────────────────

        /// <summary>The window opened (kjx → kis): every dungeon the search offers, its classic list.</summary>
        public static async Task StateAsync(NetworkStream stream, byte[] frame)
            => await WriteAsync(stream, ConnectionProtocol.Answer(Op.Kis,
                MatchmakingProtocol.BuildState(SearchDungeons().Select(d => d.Id)), ConnectionProtocol.RequestId(frame)));

        /// <summary>The dungeons the search offers: autoGroup in the client data, with a room to go to.</summary>
        private static IEnumerable<DungeonManager.Dungeon> SearchDungeons()
            => DungeonManager.All.Values.Where(d => d.AutoGroup && d.FirstRoom != 0);

        // ─── Searching ──────────────────────────────────────────────────────

        /// <summary>
        /// A search (kke → kjb, then kkk to the party): alone or as a party's leader, for the
        /// dungeons the search offers, high enough and with the key; a random one is all of them.
        /// </summary>
        public static async Task SearchAsync(NetworkStream stream, byte[] frame)
        {
            long who = SessionContext.State.CharacterId;
            byte[] kke = ConnectionProtocol.ReadPayload(frame, Op.Kke);
            if (who == 0 || kke == null) return;
            long requestId = ConnectionProtocol.RequestId(frame);

            async Task RefuseAsync(int code)
                => await WriteAsync(stream, ConnectionProtocol.Answer(Op.Kjb, MatchmakingProtocol.BuildSearchRefused(code), requestId));

            var (kind, asked) = MatchmakingProtocol.ReadSearch(kke);
            var party = Parties.IsInParty(who) ? Parties.Of(who) : null;
            if (party != null && party.LeaderId != who)
            {
                await RefuseAsync(MatchmakingProtocol.JoinCode.NotLeader);
                return;
            }
            if (kind == MatchmakingProtocol.Kind.Completing || kind == MatchmakingProtocol.Kind.None)
            {
                await RefuseAsync(MatchmakingProtocol.JoinCode.InvalidRequest);
                return;
            }
            var members = party == null ? new List<long> { who } : Parties.MembersOf(party).ToList();

            // Somebody who left a full group, or was put out of one, waits his hour.
            var banned = members.Max(m => Matchmaking.BanLeft(m, DateTime.UtcNow));
            if (banned > TimeSpan.Zero)
            {
                await WriteAsync(stream, ConnectionProtocol.Answer(Op.Kjb,
                    MatchmakingProtocol.BuildSearchBanned((long)Math.Ceiling(banned.TotalSeconds)), requestId));
                return;
            }

            var offered = (kind == MatchmakingProtocol.Kind.Random
                    ? SearchDungeons().Select(d => d.Id)
                    : asked.Distinct())
                .Select(DungeonManager.Get)
                .Where(d => d != null && d.AutoGroup && d.FirstRoom != 0)
                .ToList();
            if (offered.Count == 0)
            {
                await RefuseAsync(MatchmakingProtocol.JoinCode.EmptySlice);
                return;
            }
            int lowest = members.Min(LevelOf);
            var levelled = offered.Where(d => d.MinLevel <= lowest).ToList();
            if (levelled.Count == 0)
            {
                await RefuseAsync(MatchmakingProtocol.JoinCode.BadRequiredLevel);
                return;
            }
            var keyed = levelled.Where(d => DungeonHandler.CanEnterByMatch(d, LevelOf(who), who)).ToList();
            if (keyed.Count == 0)
            {
                await RefuseAsync(MatchmakingProtocol.JoinCode.KeyMissing);
                return;
            }
            if (members.Count >= Matchmaking.GroupSize)
            {
                await RefuseAsync(MatchmakingProtocol.JoinCode.NoDungeonLeft);
                return;
            }

            var dungeons = keyed.Select(d => d.Id).ToList();
            Matchmaking.Join(new Matchmaking.Seeker { LeaderId = who, Members = members, Dungeons = dungeons });
            Console.WriteLine($"[Matchmaking] {SessionContext.State.CharacterName} searches {dungeons.Count} dungeon(s) " +
                              $"for {members.Count} player(s).");

            // The capture's order: the kkk ahead of the kjb that answers.
            byte[] searching = ConnectionProtocol.Push(Op.Kkk, MatchmakingProtocol.BuildSearching(dungeons));
            foreach (long member in members) await TellAsync(member, searching);
            await WriteAsync(stream, ConnectionProtocol.Answer(Op.Kjb, MatchmakingProtocol.BuildSearchTaken(), requestId));

            EnsureTicking();
            await TryMatchAsync(DateTime.UtcNow);
        }

        /// <summary>The search stopped (kkm → kje): out of the queue, or out of the offer in front of him.</summary>
        public static async Task StopAsync(NetworkStream stream, byte[] frame)
        {
            long who = SessionContext.State.CharacterId;
            if (who == 0) return;
            var seeker = Matchmaking.SeekerOf(who);
            if (Matchmaking.MatchOf(who) != null) await DeclineAsync(who);
            Matchmaking.Leave(who);
            await WriteAsync(stream, ConnectionProtocol.Answer(Op.Kje, MatchmakingProtocol.BuildStopped(), ConnectionProtocol.RequestId(frame)));
            if (seeker != null)
                foreach (long member in seeker.Members.Where(m => m != who))
                    await TellAsync(member, ConnectionProtocol.Push(Op.Kje, MatchmakingProtocol.BuildStopped()));
        }

        // ─── The ready check ────────────────────────────────────────────────

        /// <summary>The ready check as one of its players sees it.</summary>
        private static byte[] ReadyCheckFor(Matchmaking.Match match, long viewer, DateTime now)
        {
            var places = Matchmaking.Read(() => match.Members.Select(id =>
            {
                var character = DatabaseManager.GetCharacterById(id);
                return new MatchmakingProtocol.Place(!match.Accepted.Contains(id), character?.Sex ?? 0, character?.Breed ?? 0);
            }).ToList());
            bool accepted = Matchmaking.Read(() => match.Accepted.Contains(viewer));
            return ConnectionProtocol.Push(Op.Kif, MatchmakingProtocol.BuildReadyCheck(places,
                (long)Math.Floor((match.Deadline - now).TotalSeconds), accepted, match.DungeonId));
        }

        private static async Task TellReadyCheckAsync(Matchmaking.Match match, DateTime now)
        {
            foreach (long member in Matchmaking.Read(() => match.Members.ToList()))
                await TellAsync(member, ReadyCheckFor(match, member, now));
        }

        /// <summary>Groups out of the queue, each offered to its players.</summary>
        private static async Task TryMatchAsync(DateTime now)
        {
            Matchmaking.Match match;
            while ((match = Matchmaking.TryMatch(Matchmaking.GroupSize, now,
                       id => SessionRegistry.FindByCharacter(id) != null)) != null)
            {
                Console.WriteLine($"[Matchmaking] Dungeon {match.DungeonId} offered to " +
                                  string.Join(", ", match.Members) + ".");
                await TellReadyCheckAsync(match, now);
            }
        }

        /// <summary>
        /// Yes (kkl → kjv): the ready check again to everybody, and the group made once all have
        /// said it. Without the key now, or in a fight, it counts as a no.
        /// </summary>
        public static async Task AcceptAsync(NetworkStream stream, byte[] frame)
        {
            long who = SessionContext.State.CharacterId;
            if (who == 0) return;
            long requestId = ConnectionProtocol.RequestId(frame);
            var offer = Matchmaking.MatchOf(who);
            if (offer == null)
            {
                await WriteAsync(stream, ConnectionProtocol.Answer(Op.Kjv,
                    MatchmakingProtocol.BuildAccepted(MatchmakingProtocol.AcceptCode.NoWaitingInvitation), requestId));
                return;
            }
            int refusal = SessionContext.State.IsInFight ? MatchmakingProtocol.AcceptCode.PlayerOccupied
                        : !DungeonHandler.CanEnterByMatch(DungeonManager.Get(offer.DungeonId), LevelOf(who), who)
                            ? MatchmakingProtocol.AcceptCode.KeyNeeded
                            : MatchmakingProtocol.AcceptCode.Ok;
            if (refusal != MatchmakingProtocol.AcceptCode.Ok)
            {
                await WriteAsync(stream, ConnectionProtocol.Answer(Op.Kjv, MatchmakingProtocol.BuildAccepted(refusal), requestId));
                await DeclineAsync(who);
                return;
            }

            // The capture's order: the ready check again ahead of the kjv that answers.
            var (match, all) = Matchmaking.Accept(who);
            await TellReadyCheckAsync(match, DateTime.UtcNow);
            await WriteAsync(stream, ConnectionProtocol.Answer(Op.Kjv, MatchmakingProtocol.BuildAccepted(), requestId));
            if (all) _ = Task.Run(() => FormAsync(match));
        }

        /// <summary>No (kij): the offer is off for everybody, and the one who said it stops searching.</summary>
        public static async Task RefuseAsync(NetworkStream stream, byte[] frame)
        {
            long who = SessionContext.State.CharacterId;
            if (who != 0) await DeclineAsync(who);
        }

        /// <summary>
        /// The offer off because of one player: «matchFail» to all; his party stops searching, the
        /// others go on as they were.
        /// </summary>
        private static async Task DeclineAsync(long who)
        {
            var match = Matchmaking.Refuse(who);
            if (match == null) return;
            Console.WriteLine($"[Matchmaking] {who} says no to dungeon {match.DungeonId}.");
            byte[] failed = ConnectionProtocol.Push(Op.Kjl, MatchmakingProtocol.BuildMatchFailed());
            foreach (var seeker in match.Seekers)
            {
                bool stops = seeker.Members.Contains(who);
                foreach (long member in seeker.Members)
                {
                    await TellAsync(member, failed);
                    await TellAsync(member, stops
                        ? ConnectionProtocol.Push(Op.Kje, MatchmakingProtocol.BuildStopped())
                        : ConnectionProtocol.Push(Op.Kkk, MatchmakingProtocol.BuildSearching(seeker.Dungeons)));
                }
            }
        }

        /// <summary>The offers whose minute ran out: off, those who had said yes back to searching.</summary>
        private static async Task ExpireAsync(DateTime now)
        {
            foreach (var match in Matchmaking.Expire(now))
            {
                Console.WriteLine($"[Matchmaking] The offer of dungeon {match.DungeonId} ran out.");
                byte[] failed = ConnectionProtocol.Push(Op.Kjl, MatchmakingProtocol.BuildMatchFailed());
                foreach (var seeker in match.Seekers)
                {
                    bool goesOn = seeker.Members.All(match.Accepted.Contains);
                    foreach (long member in seeker.Members)
                    {
                        await TellAsync(member, failed);
                        await TellAsync(member, goesOn
                            ? ConnectionProtocol.Push(Op.Kkk, MatchmakingProtocol.BuildSearching(seeker.Dungeons))
                            : ConnectionProtocol.Push(Op.Kje, MatchmakingProtocol.BuildStopped()));
                    }
                }
            }
        }

        // ─── The group ──────────────────────────────────────────────────────

        /// <summary>
        /// Everybody said yes: one party, then each player, in his own turn, told (kjl) and taken in
        /// -- his key spent and the first room. Off its packet: it moves other sessions.
        /// </summary>
        private static async Task FormAsync(Matchmaking.Match match)
        {
            try
            {
                var dungeon = DungeonManager.Get(match.DungeonId);
                var members = match.Members.ToList();
                var cameFrom = new Dictionary<long, (long MapId, int CellId)>();
                foreach (long member in members)
                    if (SessionRegistry.FindByCharacter(member) is { } where)
                        cameFrom[member] = (where.State.MapId, where.State.CellId);
                var party = await PartyHandler.FormAsync(members);
                Matchmaking.Made(party.Id, match.DungeonId, cameFrom);
                Console.WriteLine($"[Matchmaking] Dungeon {dungeon?.Name ?? match.DungeonId.ToString()} for " +
                                  string.Join(", ", members) + ".");
                foreach (long member in members)
                {
                    var session = SessionRegistry.FindByCharacter(member);
                    if (session == null || session.Stream == null) continue;
                    if (!await session.UnoCadaVez.WaitAsync(TimeSpan.FromSeconds(10)))
                    {
                        Console.WriteLine($"[Matchmaking] {member} was busy and is left out of the teleport.");
                        continue;
                    }
                    try
                    {
                        using (SessionContext.Push(session))
                        {
                            await WriteAsync(session.Stream, ConnectionProtocol.Push(Op.Kjl, MatchmakingProtocol.BuildMatched(match.DungeonId)));
                            if (dungeon != null) await DungeonHandler.EnterByMatchAsync(session.Stream, dungeon);
                        }
                    }
                    finally
                    {
                        session.UnoCadaVez.Release();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Matchmaking] The group could not be made: {ex.Message}");
            }
        }

        // ─── The group's votes ──────────────────────────────────────────────

        private static async Task TellAllAsync(IEnumerable<long> members, byte[] frame)
        {
            foreach (long member in members.ToList()) await TellAsync(member, frame);
        }

        private static bool InAFight(long characterId)
            => SessionRegistry.FindByCharacter(characterId)?.State.IsInFight == true;

        private static long SecondsLeft(Matchmaking.Vote vote, DateTime now)
            => (long)Math.Ceiling((vote.Deadline - now).TotalSeconds);

        private static List<bool> BallotsOf(Matchmaking.Vote vote)
            => Matchmaking.Read(() => vote.Ballots.Select(b => b.Yes).ToList());

        /// <summary>Whether a party is one of the search's groups and still full: leaving it then costs the search.</summary>
        internal static bool IsFull(Parties.Party party)
        {
            var group = Matchmaking.GroupOf(party.Id);
            return group != null && Parties.MembersOf(party).Count >= group.Size;
        }

        /// <summary>
        /// A ballot on giving up the dungeon (kjs): the popup's yes starts the vote -- refused with
        /// kjr while one failed a moment ago, and not started in the middle of a fight --, and
        /// everybody is told of every ballot (kig). It ends at the first no, or once all have said
        /// yes: then the group goes back where it came from and breaks up, with nothing to pay.
        /// </summary>
        public static async Task SurrenderAsync(NetworkStream stream, byte[] frame)
        {
            long who = SessionContext.State.CharacterId;
            byte[] kjs = ConnectionProtocol.ReadPayload(frame, Op.Kjs);
            var party = Parties.IsInParty(who) ? Parties.Of(who) : null;
            if (who == 0 || kjs == null || party == null) return;
            var group = Matchmaking.GroupOf(party.Id);
            if (group == null) return;
            var members = Parties.MembersOf(party);
            bool yes = MatchmakingProtocol.ReadSurrenderBallot(kjs);
            var now = DateTime.UtcNow;

            if (Matchmaking.Read(() => group.Surrender == null) && members.Any(InAFight))
            {
                await WriteAsync(stream, ConnectionProtocol.Push(Op.Kkq, MatchmakingProtocol.BuildSurrenderResult(Matchmaking.Outcome.Undefined)));
                return;
            }
            var (what, vote, result, wait) = Matchmaking.VoteSurrender(party.Id, who, yes, members, now);
            if (what == Matchmaking.Ballot.Cooldown)
            {
                await WriteAsync(stream, ConnectionProtocol.Push(Op.Kjr,
                    MatchmakingProtocol.BuildSurrenderCooldown((long)Math.Ceiling(wait.TotalSeconds))));
                return;
            }
            if (what == Matchmaking.Ballot.Refused || vote == null) return;

            Console.WriteLine($"[Matchmaking] {SessionContext.State.CharacterName} " +
                              (what == Matchmaking.Ballot.Started ? "proposes to give up" : yes ? "gives up" : "stays") +
                              $" (party {party.Id}).");
            await TellAllAsync(members, ConnectionProtocol.Push(Op.Kig,
                MatchmakingProtocol.BuildSurrenderVote(BallotsOf(vote), SecondsLeft(vote, now))));
            if (result != null) await SurrenderOverAsync(party, group, result.Value);
        }

        /// <summary>The give-up vote over (kkq); a favourable one sends everybody back and breaks the group up.</summary>
        private static async Task SurrenderOverAsync(Parties.Party party, Matchmaking.Group group, Matchmaking.Outcome outcome)
        {
            var members = Parties.MembersOf(party).ToList();
            await TellAllAsync(members, ConnectionProtocol.Push(Op.Kkq, MatchmakingProtocol.BuildSurrenderResult(outcome)));
            Console.WriteLine($"[Matchmaking] Party {party.Id}'s vote to give up: {outcome}.");
            if (outcome != Matchmaking.Outcome.Favorable) return;

            var back = members.ToDictionary(m => m, m => Matchmaking.Read(() => group.CameFrom.GetValueOrDefault(m)));
            Matchmaking.Disband(party.Id);
            await PartyHandler.BreakUpAsync(party);
            _ = Task.Run(async () =>
            {
                foreach (var (member, from) in back) await SendBackAsync(member, from, group.DungeonId);
            });
        }

        /// <summary>
        /// Back where he was when the search took him in, if he is still in its dungeon. Off any
        /// packet: it moves a session that may be the one asking.
        /// </summary>
        private static async Task SendBackAsync(long characterId, (long MapId, int CellId) from, int dungeonId)
        {
            var session = SessionRegistry.FindByCharacter(characterId);
            var dungeon = DungeonManager.Get(dungeonId);
            if (session?.Stream == null || dungeon == null || from.MapId == 0) return;
            if (!await session.UnoCadaVez.WaitAsync(TimeSpan.FromSeconds(10)))
            {
                Console.WriteLine($"[Matchmaking] {characterId} was busy and stays where he is.");
                return;
            }
            try
            {
                using (SessionContext.Push(session))
                {
                    if (!dungeon.Rooms.Contains(session.State.MapId) || session.State.IsInFight) return;
                    await TeleportHandler.ToMapAsync(session.Stream, from.MapId, from.CellId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Matchmaking] {characterId} could not be sent back: {ex.Message}");
            }
            finally
            {
                session.UnoCadaVez.Release();
            }
        }

        /// <summary>
        /// The party's ili in one of the search's groups: a yes to putting somebody out -- the one
        /// that starts the vote, or a ballot in it --, iku while one may not start. Every member's
        /// client sends one after a vote that failed (fgr.beqp), and that one gets the iku.
        /// </summary>
        internal static async Task KickBallotAsync(NetworkStream stream, Parties.Party party, long target)
        {
            long who = SessionContext.State.CharacterId;
            var now = DateTime.UtcNow;
            var cooldown = Matchmaking.KickCooldown(party.Id, now);
            if (cooldown > TimeSpan.Zero)
            {
                await WriteAsync(stream, ConnectionProtocol.Push(Op.Iku,
                    MatchmakingProtocol.BuildKickCooldown((long)Math.Ceiling(cooldown.TotalSeconds))));
                return;
            }
            await KickBallotAsync(stream, party, Matchmaking.VoteKick(party.Id, who, target, true, Parties.MembersOf(party), now), now);
        }

        /// <summary>No to putting somebody out (ikn): the vote's «No expulsar».</summary>
        public static async Task KickNoAsync(NetworkStream stream, byte[] frame)
        {
            long who = SessionContext.State.CharacterId;
            var party = Parties.IsInParty(who) ? Parties.Of(who) : null;
            if (who == 0 || party == null || Matchmaking.GroupOf(party.Id) == null) return;
            var now = DateTime.UtcNow;
            await KickBallotAsync(stream, party, Matchmaking.VoteKick(party.Id, who, 0, false, Parties.MembersOf(party), now), now);
        }

        private static async Task KickBallotAsync(NetworkStream stream, Parties.Party party,
            (Matchmaking.Ballot What, Matchmaking.Vote Vote, Matchmaking.Outcome? Result, TimeSpan Wait) ballot, DateTime now)
        {
            var (what, vote, result, wait) = ballot;
            if (what == Matchmaking.Ballot.Cooldown)
            {
                await WriteAsync(stream, ConnectionProtocol.Push(Op.Iku,
                    MatchmakingProtocol.BuildKickCooldown((long)Math.Ceiling(wait.TotalSeconds))));
                return;
            }
            if (what == Matchmaking.Ballot.Refused || vote == null) return;

            Console.WriteLine($"[Matchmaking] {SessionContext.State.CharacterName} " +
                              (what == Matchmaking.Ballot.Started ? "proposes to put out" : "votes on putting out") +
                              $" {vote.Target} (party {party.Id}).");
            await TellAllAsync(Parties.MembersOf(party), ConnectionProtocol.Push(Op.Ikw, MatchmakingProtocol.BuildKickVote(
                BallotsOf(vote), vote.Target, (long)Math.Ceiling((vote.Deadline - now).TotalMilliseconds))));
            if (result != null && Matchmaking.GroupOf(party.Id) is { } group)
                await KickOverAsync(party, group, vote.Target, result.Value);
        }

        /// <summary>
        /// The vote to put somebody out over (ike), to everybody and him; a favourable one bars him
        /// from the search for the hour, takes him out of the party and sends him back.
        /// </summary>
        private static async Task KickOverAsync(Parties.Party party, Matchmaking.Group group, long target, Matchmaking.Outcome outcome)
        {
            await TellAllAsync(Parties.MembersOf(party), ConnectionProtocol.Push(Op.Ike, MatchmakingProtocol.BuildKickResult(target, outcome)));
            Console.WriteLine($"[Matchmaking] Party {party.Id}'s vote to put out {target}: {outcome}.");
            if (outcome != Matchmaking.Outcome.Favorable) return;

            Matchmaking.Ban(target, DateTime.UtcNow.AddSeconds(Matchmaking.BanSeconds));
            var from = Matchmaking.Read(() => group.CameFrom.GetValueOrDefault(target));
            await PartyHandler.PutOutAsync(party, target);
            _ = Task.Run(() => SendBackAsync(target, from, group.DungeonId));
        }

        /// <summary>
        /// Somebody out of one of the search's groups -- by his own will, put out or disconnected:
        /// the hour without searching for leaving it full by his will (kjo), the group gone with its
        /// party, and the votes his going settled told.
        /// </summary>
        internal static async Task LeftAsync(Parties.Party party, long who, bool dissolved, bool penalised)
        {
            var group = Matchmaking.GroupOf(party.Id);
            if (group == null) return;
            var now = DateTime.UtcNow;
            if (penalised)
            {
                Matchmaking.Ban(who, now.AddSeconds(Matchmaking.BanSeconds));
                await TellAsync(who, ConnectionProtocol.Push(Op.Kjo, MatchmakingProtocol.BuildLeaveCooldown(Matchmaking.BanSeconds)));
                Console.WriteLine($"[Matchmaking] {who} leaves a full group: no search for {Matchmaking.BanSeconds / 60} minutes.");
            }
            if (dissolved)
            {
                Matchmaking.Disband(party.Id);
                return;
            }
            foreach (var (vote, kick, result) in Matchmaking.Left(party.Id, who, now))
            {
                if (kick) await KickOverAsync(party, group, vote.Target, result);
                else await SurrenderOverAsync(party, group, result);
            }
        }

        /// <summary>The votes whose minute ran out: told as expired.</summary>
        private static async Task ExpireVotesAsync(DateTime now)
        {
            foreach (var (group, vote, kick) in Matchmaking.ExpireVotes(now))
            {
                var party = Parties.Get(group.PartyId);
                if (party == null)
                {
                    Matchmaking.Disband(group.PartyId);
                    continue;
                }
                if (kick) await KickOverAsync(party, group, vote.Target, Matchmaking.Outcome.Expired);
                else await SurrenderOverAsync(party, group, Matchmaking.Outcome.Expired);
            }
        }
    }
}

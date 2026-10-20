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
    /// Not done: the votes inside the dungeon (to give up, kig/kkq; to put someone out,
    /// ikw/ike/iku), the cooldowns after them, and completing a party already inside.
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
            var members = party == null ? new List<long> { who } : Parties.MembersOf(party).ToList();
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
                await PartyHandler.FormAsync(members);
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
    }
}

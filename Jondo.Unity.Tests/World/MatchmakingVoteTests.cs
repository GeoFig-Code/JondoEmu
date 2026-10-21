using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Xunit;
using Wire = Jondo.Unity.Tests.Combat.PortalTests.Wire;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// The group the dungeon search made: its votes to give up and to put somebody out, both
    /// unanimous, their cooldowns, and the hour without searching for leaving it full or being
    /// put out -- the rules of the client's own texts.
    /// </summary>
    [Collection("lobbies")]
    public sealed class MatchmakingVoteTests
    {
        public MatchmakingVoteTests() => Matchmaking.Clear();

        private static readonly DateTime T = new(2026, 10, 22, 20, 0, 0, DateTimeKind.Utc);

        private static Matchmaking.Group Group(int partyId, params long[] members)
            => Matchmaking.Made(partyId, 141, members.ToDictionary(m => m, m => (100L + m, 300)));

        [Fact]
        public void Giving_up_needs_everybody_and_a_no_waits_its_cooldown()
        {
            Group(7, 1, 2, 3);
            var members = new long[] { 1, 2, 3 };

            Assert.Equal(Matchmaking.Ballot.Refused, Matchmaking.VoteSurrender(7, 1, false, members, T).What);   // a no starts nothing
            var started = Matchmaking.VoteSurrender(7, 1, true, members, T);
            Assert.Equal(Matchmaking.Ballot.Started, started.What);
            Assert.Null(started.Result);
            Assert.Equal(Matchmaking.Ballot.Refused, Matchmaking.VoteSurrender(7, 1, true, members, T).What);    // one ballot each
            Assert.Null(Matchmaking.VoteSurrender(7, 2, true, members, T).Result);
            Assert.Equal(Matchmaking.Outcome.Favorable, Matchmaking.VoteSurrender(7, 3, true, members, T).Result);

            // One no ends it, and another may not start for the cooldown.
            Matchmaking.VoteSurrender(7, 1, true, members, T);
            Assert.Equal(Matchmaking.Outcome.NotFavorable, Matchmaking.VoteSurrender(7, 2, false, members, T).Result);
            var later = Matchmaking.VoteSurrender(7, 3, true, members, T.AddSeconds(10));
            Assert.Equal(Matchmaking.Ballot.Cooldown, later.What);
            Assert.Equal(TimeSpan.FromSeconds(Matchmaking.VoteCooldownSeconds - 10), later.Wait);
            Assert.Equal(Matchmaking.Ballot.Started,
                         Matchmaking.VoteSurrender(7, 3, true, members, T.AddSeconds(Matchmaking.VoteCooldownSeconds)).What);

            // Nor is anybody outside the group heard.
            Assert.Equal(Matchmaking.Ballot.Refused, Matchmaking.VoteSurrender(8, 1, true, members, T).What);
        }

        [Fact]
        public void Putting_out_is_everybody_else_s_yes()
        {
            Group(9, 1, 2, 3, 4);
            var members = new long[] { 1, 2, 3, 4 };

            Assert.Equal(Matchmaking.Ballot.Refused, Matchmaking.VoteKick(9, 1, 1, true, members, T).What);   // not oneself
            Assert.Equal(Matchmaking.Ballot.Refused, Matchmaking.VoteKick(9, 1, 5, true, members, T).What);   // not a stranger
            var started = Matchmaking.VoteKick(9, 1, 4, true, members, T);
            Assert.Equal(Matchmaking.Ballot.Started, started.What);
            Assert.Equal(new long[] { 1, 2, 3 }, started.Vote.Voters);
            Assert.Equal(Matchmaking.Ballot.Refused, Matchmaking.VoteKick(9, 4, 4, true, members, T).What);   // he does not vote
            Assert.Equal(Matchmaking.Ballot.Refused, Matchmaking.VoteKick(9, 2, 3, true, members, T).What);   // another target
            Assert.Null(Matchmaking.VoteKick(9, 2, 4, true, members, T).Result);
            Assert.Equal(Matchmaking.Outcome.Favorable, Matchmaking.VoteKick(9, 3, 0, true, members, T).Result);

            // A failed vote: the cooldown every member's client asks about.
            Matchmaking.VoteKick(9, 1, 4, true, members, T);
            Assert.Equal(Matchmaking.Outcome.NotFavorable, Matchmaking.VoteKick(9, 2, 0, false, members, T).Result);
            Assert.Equal(TimeSpan.FromSeconds(Matchmaking.VoteCooldownSeconds), Matchmaking.KickCooldown(9, T));
            Assert.Equal(Matchmaking.Ballot.Cooldown, Matchmaking.VoteKick(9, 3, 4, true, members, T).What);
        }

        [Fact]
        public void A_vote_runs_out_and_a_departure_can_settle_it()
        {
            Group(11, 1, 2, 3);
            var members = new long[] { 1, 2, 3 };
            Matchmaking.VoteSurrender(11, 1, true, members, T);
            Matchmaking.VoteKick(11, 2, 3, true, new long[] { 1, 2, 3 }, T);
            Assert.Empty(Matchmaking.ExpireVotes(T.AddSeconds(Matchmaking.VoteSeconds - 1)));
            var expired = Matchmaking.ExpireVotes(T.AddSeconds(Matchmaking.VoteSeconds));
            Assert.Equal(new[] { false, true }, expired.Select(e => e.Kick));

            // The last one who had not voted goes: the others had all said yes.
            Matchmaking.VoteSurrender(11, 1, true, members, T.AddHours(1));
            Matchmaking.VoteSurrender(11, 2, true, members, T.AddHours(1));
            var settled = Matchmaking.Left(11, 3, T.AddHours(1)).Single();
            Assert.False(settled.Kick);
            Assert.Equal(Matchmaking.Outcome.Favorable, settled.Result);

            // The one a vote would put out goes: it is over.
            Assert.Null(Matchmaking.VoteKick(11, 1, 2, true, new long[] { 1, 2, 5 }, T.AddHours(2)).Result);
            Assert.Equal(Matchmaking.Outcome.Undefined, Matchmaking.Left(11, 2, T.AddHours(2)).Single().Result);
        }

        [Fact]
        public void The_messages_carry_what_the_client_reads()
        {
            // kig: f1 the ballots packed, f2 seconds; ikw: f1, f2 whom, f3 milliseconds.
            Assert.Equal("0a0201001020", Hex(MatchmakingProtocol.BuildSurrenderVote(new[] { true, false }, 32)));
            Assert.Equal("0a01011007" + "18e0d403", Hex(MatchmakingProtocol.BuildKickVote(new[] { true }, 7, 60_000)));
            Assert.Equal("", Hex(MatchmakingProtocol.BuildSurrenderResult(Matchmaking.Outcome.Favorable)));
            Assert.Equal("1001", Hex(MatchmakingProtocol.BuildSurrenderResult(Matchmaking.Outcome.NotFavorable)));
            Assert.Equal("0807" + "1002", Hex(MatchmakingProtocol.BuildKickResult(7, Matchmaking.Outcome.Expired)));
            Assert.Equal("0878", Hex(MatchmakingProtocol.BuildKickCooldown(120)));
            Assert.Equal("1078", Hex(MatchmakingProtocol.BuildSurrenderCooldown(120)));
            Assert.Equal("10901c", Hex(MatchmakingProtocol.BuildLeaveCooldown(3600)));
            Assert.Equal("1a03" + "10901c", Hex(MatchmakingProtocol.BuildSearchBanned(3600)));
            Assert.True(MatchmakingProtocol.ReadSurrenderBallot(new byte[] { 0x08, 0x01 }));
            Assert.False(MatchmakingProtocol.ReadSurrenderBallot(Array.Empty<byte>()));
        }

        private static string Hex(byte[] bytes) => string.Concat(bytes.Select(b => b.ToString("x2")));

        private static Task Send(Wire wire, Func<Task> handler)
        {
            using (SessionContext.Push(wire.Session)) return handler();
        }

        /// <summary>
        /// Three players of one search group, through the real handlers: a vote to give up that
        /// one turns down and its cooldown; a vote that puts the third out, who is then barred from
        /// the search; and the second leaving the group by his own will.
        /// </summary>
        [Fact]
        public async Task A_group_votes_through_the_handlers()
        {
            const long a = 9_200_000_001, b = 9_200_000_002, c = 9_200_000_003;
            await using var wa = await Wire.Open(a);
            await using var wb = await Wire.Open(b);
            await using var wc = await Wire.Open(c);
            var party = Parties.Create(a);
            foreach (long member in new[] { b, c })
            {
                Assert.True(Parties.Invite(party, member, a));
                Assert.True(Parties.Accept(party, member));
            }
            Matchmaking.Made(party.Id, 141, new Dictionary<long, (long, int)> { [a] = (1, 1), [b] = (1, 1), [c] = (1, 1) });
            try
            {
                // ── Giving up: A proposes, B stays.
                await Send(wa, () => MatchmakingHandler.SurrenderAsync(wa.Session.Stream!, ConnectionProtocol.Push(Op.Kjs, new byte[] { 0x08, 0x01 })));
                foreach (var wire in new[] { wa, wb, wc })
                {
                    var kig = (await wire.Drain()).Single();
                    Assert.Equal("kig", kig.Op);
                    Assert.Equal("0a0101", Hex(kig.Payload).Substring(0, 6));
                }
                await Send(wb, () => MatchmakingHandler.SurrenderAsync(wb.Session.Stream!, ConnectionProtocol.Push(Op.Kjs, Array.Empty<byte>())));
                var told = await wc.Drain();
                Assert.Equal(new[] { "kig", "kkq" }, told.Select(f => f.Op));
                Assert.Equal("1001", Hex(told[1].Payload));
                await wa.Drain();
                await wb.Drain();
                await Send(wc, () => MatchmakingHandler.SurrenderAsync(wc.Session.Stream!, ConnectionProtocol.Push(Op.Kjs, new byte[] { 0x08, 0x01 })));
                Assert.Equal("kjr", (await wc.Drain()).Single().Op);

                // ── Putting C out: A's ili starts it, B's agrees.
                byte[] Ili(long who) => ConnectionProtocol.Push(Op.Ili, Pb.New().Var(1, party.Id).Var(2, who).Build());
                await Send(wa, () => PartyHandler.KickAsync(wa.Session.Stream!, Ili(c)));
                var vote = (await wc.Drain()).Single();
                Assert.Equal("ikw", vote.Op);
                Assert.Equal(c, ProtoMessage.Parse(vote.Payload).Fields.Single(f => f.FieldNumber == 2).VarIntValue);
                await wa.Drain();
                await wb.Drain();
                await Send(wb, () => PartyHandler.KickAsync(wb.Session.Stream!, Ili(c)));
                var cTold = await wc.Drain();
                Assert.Equal(new[] { "ikw", "ike", "ils" }, cTold.Select(f => f.Op));
                Assert.Equal(c, ProtoMessage.Parse(cTold[1].Payload).Fields.Single(f => f.FieldNumber == 1).VarIntValue);
                Assert.DoesNotContain(c, Parties.MembersOf(party));
                Assert.True(Matchmaking.BanLeft(c, DateTime.UtcNow) > TimeSpan.FromMinutes(59));
                Assert.Contains("ing", (await wa.Drain()).Select(f => f.Op));
                await wb.Drain();

                // C may not search for his hour: kjb's ban.
                await Send(wc, () => MatchmakingHandler.SearchAsync(wc.Session.Stream!, ConnectionProtocol.Push(Op.Kke, new byte[] { 0x12, 0x00 })));
                var refused = (await wc.Drain()).Single();
                Assert.Equal("kjb", refused.Op);
                Assert.Contains(ProtoMessage.Parse(refused.Payload).Fields, f => f.FieldNumber == 3);

                // ── B leaves: the group is not full any more, so it costs him nothing.
                await Send(wb, () => PartyHandler.LeaveAsync(wb.Session.Stream!,
                    ConnectionProtocol.Push(Op.Inh, Pb.New().Var(2, party.Id).Build())));
                Assert.DoesNotContain("kjo", (await wb.Drain()).Select(f => f.Op));
                Assert.Equal(TimeSpan.Zero, Matchmaking.BanLeft(b, DateTime.UtcNow));
                Assert.Null(Matchmaking.GroupOf(party.Id));   // two became one: the party broke up
            }
            finally
            {
                Parties.Dissolve(party);
            }
        }

        /// <summary>Everybody gives up: the vote's result to all, the party broken up, nobody barred.</summary>
        [Fact]
        public async Task Giving_up_together_breaks_the_group_up_for_free()
        {
            const long a = 9_200_000_021, b = 9_200_000_022;
            await using var wa = await Wire.Open(a);
            await using var wb = await Wire.Open(b);
            var party = Parties.Create(a);
            Parties.Invite(party, b, a);
            Parties.Accept(party, b);
            Matchmaking.Made(party.Id, 141, new Dictionary<long, (long, int)> { [a] = (1, 1), [b] = (1, 1) });
            try
            {
                byte[] yes = ConnectionProtocol.Push(Op.Kjs, new byte[] { 0x08, 0x01 });
                await Send(wa, () => MatchmakingHandler.SurrenderAsync(wa.Session.Stream!, yes));
                await wa.Drain();
                await wb.Drain();
                await Send(wb, () => MatchmakingHandler.SurrenderAsync(wb.Session.Stream!, yes));
                foreach (var wire in new[] { wa, wb })
                {
                    var told = await wire.Drain();
                    Assert.Equal(new[] { "kig", "kkq", "imy" }, told.Select(f => f.Op));
                    Assert.Empty(told[1].Payload);   // favourable: the enum's zero
                }
                Assert.Null(Matchmaking.GroupOf(party.Id));
                Assert.False(Parties.IsInParty(a));
                Assert.Equal(TimeSpan.Zero, Matchmaking.BanLeft(a, DateTime.UtcNow));
            }
            finally
            {
                Parties.Dissolve(party);
            }
        }

        /// <summary>Leaving a full group by one's own will: the hour without searching, told with kjo.</summary>
        [Fact]
        public async Task Leaving_a_full_group_costs_the_search_an_hour()
        {
            const long a = 9_200_000_011, b = 9_200_000_012, c = 9_200_000_013;
            await using var wa = await Wire.Open(a);
            await using var wb = await Wire.Open(b);
            await using var wc = await Wire.Open(c);
            var party = Parties.Create(a);
            foreach (long member in new[] { b, c })
            {
                Parties.Invite(party, member, a);
                Parties.Accept(party, member);
            }
            Matchmaking.Made(party.Id, 141, new Dictionary<long, (long, int)> { [a] = (1, 1), [b] = (1, 1), [c] = (1, 1) });
            try
            {
                await Send(wc, () => PartyHandler.LeaveAsync(wc.Session.Stream!,
                    ConnectionProtocol.Push(Op.Inh, Pb.New().Var(2, party.Id).Build())));
                var told = await wc.Drain();
                Assert.Equal(new[] { "ils", "kjo" }, told.Select(f => f.Op));
                Assert.Equal("10901c", Hex(told[1].Payload));
                Assert.True(Matchmaking.BanLeft(c, DateTime.UtcNow) > TimeSpan.FromMinutes(59));
                Assert.NotNull(Matchmaking.GroupOf(party.Id));
            }
            finally
            {
                Parties.Dissolve(party);
            }
        }
    }
}

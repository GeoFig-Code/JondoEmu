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
    /// The automatic dungeon search: the capture «busqueda automatica de grupo-encuentra para una
    /// dung-aceptar-autoteleporte de todos y creacion de grupo-abandonarlo» for the wire, the
    /// queue's rules for who is grouped with whom.
    /// </summary>
    [Collection("lobbies")]
    public sealed class MatchmakingTests
    {
        public MatchmakingTests() => Matchmaking.Clear();

        private static string Hex(byte[] bytes) => string.Concat(bytes.Select(b => b.ToString("x2")));

        private static byte[] Bytes(string hex) => Enumerable.Range(0, hex.Length / 2)
            .Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();

        private const string CapturedSearch =
            "0a640a625a13088a0158222d2f30190d15208e01341b27231274641169595b095c1e730f487d03292a2b072e363265507e7b0a7a" +
            "377c332c388b011c4b3984018301663b0e163d8d01683c3e4a4975768c01676e45466a476b61638701447277626c6d437854";

        private const string CapturedSearching =
            "0a641a62676665646362615c5b5a595854504b4a494847464544433e3d3c3b39383736343332302f2e2d2c2b2a29272322201e1c" +
            "1b1916151312110f0e0d0a090807038e018d018c018b018a018701840183017e7d7c7b7a787776757473726e6d6c6b6a69682000";

        /// <summary>The classic search the capture sends: ninety dungeons, the 141 it got among them.</summary>
        [Fact]
        public void The_search_reads_as_the_capture_sends_it()
        {
            var (kind, dungeons) = MatchmakingProtocol.ReadSearch(Bytes(CapturedSearch));
            Assert.Equal(MatchmakingProtocol.Kind.Classic, kind);
            Assert.Equal(90, dungeons.Count);
            Assert.Equal(new[] { 90, 19, 8, 138 }, dungeons.Take(4));
            Assert.Contains(141, dungeons);
            Assert.Equal(MatchmakingProtocol.Kind.Random, MatchmakingProtocol.ReadSearch(Bytes("1200")).Kind);
            Assert.Equal(MatchmakingProtocol.Kind.Completing, MatchmakingProtocol.ReadSearch(Bytes("1a00")).Kind);
        }

        /// <summary>The answers and notices, byte for byte as the capture has them.</summary>
        [Fact]
        public void The_messages_go_as_the_capture_has_them()
        {
            Assert.Equal("1200", Hex(MatchmakingProtocol.BuildSearchTaken()));
            Assert.Equal("", Hex(MatchmakingProtocol.BuildAccepted()));
            Assert.Equal("0a03088d01", Hex(MatchmakingProtocol.BuildMatched(141)));

            // The searching notice, with the dungeons in the capture's own order.
            var order = ProtoMessage.Parse(ProtoMessage.Parse(Bytes(CapturedSearching)).Fields[0].BytesValue).Fields[0].BytesValue;
            var listed = MatchmakingProtocol.ReadSearch(Pb.New().Msg(1, Pb.New().Bytes(1, order)).Build()).Dungeons;
            Assert.Equal(CapturedSearching, Hex(MatchmakingProtocol.BuildSearching(listed)));

            // The ready check as it opens, and once the third place -- his -- has said yes.
            var places = new List<MatchmakingProtocol.Place>
            {
                new(true, 1, 15), new(true, 0, 9), new(true, 0, 11), new(true, 0, 9),
            };
            Assert.Equal("0a0608011001180f0a04080118090a040801180b0a0408011809103b208d01",
                         Hex(MatchmakingProtocol.BuildReadyCheck(places, 59, false, 141)));
            places[2] = new(false, 0, 11);
            Assert.Equal("0a0608011001180f0a04080118090a02180b0a040801180910381801208d01",
                         Hex(MatchmakingProtocol.BuildReadyCheck(places, 56, true, 141)));
        }

        private static Matchmaking.Seeker Seeker(DateTime since, int[] dungeons, params long[] members)
            => new() { LeaderId = members[0], Members = members.ToList(), Dungeons = dungeons.ToList(), Since = since };

        /// <summary>
        /// Exactly the group's size, the longest waiting first, a party whole or not at all, and
        /// only for a dungeon all of them chose.
        /// </summary>
        [Fact]
        public void A_group_is_made_of_the_longest_waiting()
        {
            var t = new DateTime(2026, 10, 20, 22, 0, 0, DateTimeKind.Utc);
            Matchmaking.Join(Seeker(t, new[] { 1, 2 }, 1));
            Matchmaking.Join(Seeker(t.AddSeconds(1), new[] { 2 }, 2, 3));   // a party of two
            Matchmaking.Join(Seeker(t.AddSeconds(2), new[] { 3 }, 5));
            Assert.Null(Matchmaking.TryMatch(4, t));
            Assert.Equal(3, Matchmaking.Waiting()[2]);

            // A party of three would not fit with the three waiting for dungeon 2.
            Matchmaking.Join(Seeker(t.AddSeconds(3), new[] { 2 }, 8, 9, 10));
            Assert.Null(Matchmaking.TryMatch(4, t));
            Matchmaking.Join(Seeker(t.AddSeconds(4), new[] { 2, 3 }, 6));
            var match = Matchmaking.TryMatch(4, t);
            Assert.Equal(2, match.DungeonId);
            Assert.Equal(new long[] { 1, 2, 3, 6 }, match.Members);
            Assert.Equal(t.AddSeconds(Matchmaking.ConfirmSeconds), match.Deadline);
            Assert.Same(match, Matchmaking.MatchOf(3));
            Assert.Null(Matchmaking.SeekerOf(1));
            Assert.NotNull(Matchmaking.SeekerOf(8));

            // Somebody gone is dropped before anything is matched.
            Matchmaking.Join(Seeker(t.AddSeconds(5), new[] { 3 }, 7));
            Assert.Null(Matchmaking.TryMatch(2, t, id => id != 5));
            Assert.Null(Matchmaking.SeekerOf(5));
        }

        /// <summary>
        /// A no puts the others back where they were and stops whoever said it; the minute out
        /// puts back only those who had all said yes; everybody's yes ends the offer.
        /// </summary>
        [Fact]
        public void The_offer_ends_in_a_yes_a_no_or_the_minute()
        {
            var t = new DateTime(2026, 10, 20, 22, 0, 0, DateTimeKind.Utc);
            Matchmaking.Join(Seeker(t, new[] { 9 }, 1));
            Matchmaking.Join(Seeker(t.AddSeconds(1), new[] { 9 }, 2));
            var match = Matchmaking.TryMatch(2, t);
            Assert.False(Matchmaking.Accept(1).AllAccepted);
            Assert.Same(match, Matchmaking.Refuse(2));
            Assert.NotNull(Matchmaking.SeekerOf(1));
            Assert.Null(Matchmaking.SeekerOf(2));

            Matchmaking.Join(Seeker(t.AddSeconds(2), new[] { 9 }, 3));
            match = Matchmaking.TryMatch(2, t);
            Matchmaking.Accept(3);
            Assert.Empty(Matchmaking.Expire(t.AddSeconds(59)));
            Assert.Same(match, Matchmaking.Expire(t.AddSeconds(60)).Single());
            Assert.NotNull(Matchmaking.SeekerOf(3));
            Assert.Null(Matchmaking.SeekerOf(1));

            Matchmaking.Join(Seeker(t.AddSeconds(3), new[] { 9 }, 4));
            match = Matchmaking.TryMatch(2, t);
            Assert.False(Matchmaking.Accept(3).AllAccepted);
            var (done, all) = Matchmaking.Accept(4);
            Assert.True(all);
            Assert.Same(match, done);
            Assert.Null(Matchmaking.MatchOf(3));
        }

        /// <summary>
        /// A group short of players is completed by those who wait for its dungeon: it said yes
        /// when it asked, so only the newcomers are asked; two such groups are not put together.
        /// </summary>
        [Fact]
        public void A_group_short_of_players_is_completed()
        {
            var t = new DateTime(2026, 10, 22, 21, 0, 0, DateTimeKind.Utc);
            Matchmaking.Join(new Matchmaking.Seeker
            {
                LeaderId = 1, Members = new List<long> { 1, 2 }, Dungeons = new List<int> { 9 }, Since = t, CompletingParty = 70,
            });
            Matchmaking.Join(new Matchmaking.Seeker
            {
                LeaderId = 3, Members = new List<long> { 3 }, Dungeons = new List<int> { 9 }, Since = t.AddSeconds(1), CompletingParty = 71,
            });
            Matchmaking.Join(Seeker(t.AddSeconds(2), new[] { 9 }, 4));
            Assert.Null(Matchmaking.TryMatch(4, t));
            Matchmaking.Join(Seeker(t.AddSeconds(3), new[] { 9, 5 }, 5));

            var match = Matchmaking.TryMatch(4, t);
            Assert.Equal(new long[] { 1, 2, 4, 5 }, match.Members);
            Assert.Equal(70, match.Completing.CompletingParty);
            Assert.True(match.AlreadyIn(2));
            Assert.False(match.AlreadyIn(4));
            Assert.False(Matchmaking.Accept(4).AllAccepted);
            Assert.True(Matchmaking.Accept(5).AllAccepted);
            Assert.NotNull(Matchmaking.SeekerOf(3));   // the other group still waits
        }

        /// <summary>The group's size: the game's four, or fewer from the server's settings.</summary>
        [Fact]
        public void The_group_size_follows_the_settings()
        {
            try
            {
                ServerSettings.UseForTests(new ServerSettings());
                Assert.Equal(4, Matchmaking.GroupSize);
                ServerSettings.UseForTests(new ServerSettings { MatchmakingGroupSize = 2 });
                Assert.Equal(2, Matchmaking.GroupSize);
                ServerSettings.UseForTests(new ServerSettings { MatchmakingGroupSize = 9 });
                Assert.Equal(4, Matchmaking.GroupSize);
            }
            finally
            {
                TestDatabases.DefaultSettings();
            }
        }

        /// <summary>
        /// The window's state lists every dungeon of the search, whether anybody waits or not --
        /// the classic list came out empty, and a second client saw only the dungeon the first one
        /// waited for --, and a search without the key is refused with the client's code for it.
        /// </summary>
        [Fact]
        public async Task The_handler_answers_the_window_and_a_keyless_search()
        {
            if (!DungeonManager.IsLoaded) DungeonManager.Initialize();
            var t = DateTime.UtcNow;
            Matchmaking.Join(Seeker(t, new[] { 141, 3 }, 9_100_000_001));
            await using var wire = await Wire.Open(9_100_000_002);
            using (SessionContext.Push(wire.Session))
            {
                await MatchmakingHandler.StateAsync(wire.Session.Stream!, ConnectionProtocol.Push(Op.Kjx, Array.Empty<byte>()));
                await MatchmakingHandler.SearchAsync(wire.Session.Stream!, ConnectionProtocol.Push(Op.Kke,
                    Pb.New().Msg(1, Pb.New().Packed(1, new long[] { 8 })).Build()));
            }
            var frames = await wire.Drain();
            Assert.Equal(new[] { "kis", "kjb" }, frames.Select(f => f.Op));
            var listed = ProtoMessage.Parse(frames[0].Payload).Fields.Where(f => f.FieldNumber == 3)
                                     .Select(f => ProtoMessage.Parse(f.BytesValue).Fields.Single().VarIntValue).ToList();
            Assert.Equal(DungeonManager.All.Values.Count(d => d.AutoGroup && d.FirstRoom != 0), listed.Count);
            Assert.Equal(listed.OrderBy(d => d), listed);
            Assert.Contains(141L, listed);
            Assert.Contains(8L, listed);
            Assert.Equal("1a0228" + "01", Hex(MatchmakingProtocol.BuildState(new[] { 1 })));

            // Dungeon 8 asks for its key (8143) and level 10: the level is checked first.
            var refused = ProtoMessage.Parse(ProtoMessage.Parse(frames[1].Payload).Fields.Single(f => f.FieldNumber == 1).BytesValue);
            Assert.Contains(refused.Fields.Single(f => f.FieldNumber == 2).VarIntValue,
                            new long[] { MatchmakingProtocol.JoinCode.BadRequiredLevel, MatchmakingProtocol.JoinCode.KeyMissing });
            Assert.Null(Matchmaking.SeekerOf(9_100_000_002));
        }
    }
}

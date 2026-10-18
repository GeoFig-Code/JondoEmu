using System;
using System.Linq;
using System.Threading.Tasks;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.World.Content;
using Jondo.Unity.World.Maps;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// The Patio de Efedra's chess as the client's data lays it out: the Reserva's four boards, one
    /// a variant, Belladona's map; the rules bringing the pieces out, green where a piece stands
    /// right, red or a decoy otherwise; and the game's own data -- each piece's move, its glyph's
    /// cell, the turn that ends it. In the MapManager collection, as every test that loads the
    /// interactives.
    /// </summary>
    [Collection("MapManager")]
    public class GuildRaidEfedraTests
    {
        public GuildRaidEfedraTests() => Interactives.Initialize();

        private static RaidKind Gardens => Raids.Of(Raids.EternalGardens);

        private static GuildRaidEfedra.Work Patio => GuildRaidEfedra.WorkOf(Gardens);

        private static RaidInstance Raid(long id)
        {
            var raid = new RaidInstance(id, Raids.EternalGardens, 1, 7001, DateTimeOffset.UtcNow, TimeSpan.FromHours(2));
            raid.SetHealth(Gardens.Health);
            return raid;
        }

        /// <summary>Four boards of the Reserva, variants 0 to 3, four pieces each; Belladona at [12,14]; the centre's logos.</summary>
        [Fact]
        public void The_boards_are_read_off_the_client_s_maps()
        {
            if (Patio == null) return;   // no world data

            Assert.Equal(new[] { 0, 1, 2, 3 }, Patio.Boards.Select(b => b.Variant));
            Assert.Equal(new[] { 238300417L, 238299648L, 238299393L, 238299906L }, Patio.Boards.Select(b => b.MapId));
            Assert.All(Patio.Boards, b => Assert.Equal(new[] { 1, 2, 3, 4 }, b.Pieces.Values.OrderBy(k => k)));
            Assert.All(Patio.Boards, b => Assert.All(b.Pieces.Keys.Concat(b.Decoys.Keys),
                e => Assert.Contains(Interactives.ElementsOf(b.MapId), x => x.Id == e)));   // clickable, as the registry needs
            Assert.Equal(238303489L, Patio.NpcMap);
            // Her game is fought on the Patio's arena, the one whose stagings count the turns down.
            if (!Jondo.Unity.Server.MapManager.WalkableCells.ContainsKey(Patio.NpcMap)) Jondo.Unity.Server.MapManager.Initialize();
            long arena = Jondo.Unity.Server.MapManager.ResolveArenaMapId(Patio.NpcMap);
            Assert.Equal(238305025L, arena);
            Assert.True(GuildRaidStagings.Has(arena, GuildRaidEfedra.Counter(3)));
            Assert.True(GuildRaidStagings.Has(arena, GuildRaidEfedra.CounterReset));
            var raid = Raid(700);
            foreach (var variants in new[] { new[] { 0, 0, 0, 0 }, new[] { 1, 1, 1, 1 }, new[] { 2, 2, 2, 2 }, new[] { 3, 3, 3, 3 } })
            {
                GuildRaidEfedra.Set(raid, variants);
                Assert.All(Enumerable.Range(1, GuildRaidEfedra.Pieces),
                    k => Assert.Contains(GuildRaidEfedra.TargetOf(raid, k), Jondo.Unity.Server.MapManager.GetFightWalkable(arena)));
            }
            Assert.True(GuildRaidStagings.Has(Patio.Centre, string.Format(GuildRaidMonochrome.StepDone, 1)));
        }

        /// <summary>
        /// The turning gives rooks (pieces 1 and 4) their line move and bishops (2 and 3) their
        /// diagonal one; piece 1's glyph of variant 0 lies on 201; Belladona's fourth turn ends it.
        /// </summary>
        [Fact]
        public void The_game_s_data_is_the_client_s()
        {
            if (Patio == null) return;

            Assert.Equal(31840, GuildRaidEfedra.MoveOf(1));
            Assert.Equal(31841, GuildRaidEfedra.MoveOf(2));
            Assert.Equal(31841, GuildRaidEfedra.MoveOf(3));
            Assert.Equal(31840, GuildRaidEfedra.MoveOf(4));
            Assert.Equal(6541, GuildRaidEfedra.PlacedStateOf(1));
            Assert.Equal(4, GuildRaidEfedra.TurnsOf());

            var raid = Raid(701);
            GuildRaidEfedra.Set(raid, new[] { 0, 1, 2, 3 });
            Assert.Equal(201, GuildRaidEfedra.TargetOf(raid, 1));
            Assert.All(Enumerable.Range(1, 4), k => Assert.True(MapGeometry.IsValid(GuildRaidEfedra.TargetOf(raid, k))));
        }

        /// <summary>A rook moves in line, a bishop in diagonal; neither stays where it is.</summary>
        [Fact]
        public void Rooks_go_in_line_and_bishops_in_diagonal()
        {
            if (Patio == null) return;
            int from = MapGeometry.PointToCell(10, 0);
            var (x, y) = MapGeometry.CellToPoint(from);
            int line = MapGeometry.PointToCell(x + 3, y), diagonal = MapGeometry.PointToCell(x + 2, y + 2), neither = MapGeometry.PointToCell(x + 1, y + 3);

            Assert.True(GuildRaidEfedra.Reaches(31840, from, line));
            Assert.False(GuildRaidEfedra.Reaches(31840, from, diagonal));
            Assert.True(GuildRaidEfedra.Reaches(31841, from, diagonal));
            Assert.False(GuildRaidEfedra.Reaches(31841, from, line));
            Assert.False(GuildRaidEfedra.Reaches(31840, from, neither));
            Assert.False(GuildRaidEfedra.Reaches(31841, from, neither));
            Assert.False(GuildRaidEfedra.Reaches(31840, from, from));
        }

        /// <summary>Belladona's words: hers, and only the farewell for someone outside a raid.</summary>
        [Fact]
        public void Belladona_talks_of_her_game()
        {
            var (question, replies) = GuildRaidEfedra.DialogueFor(-12345);
            Assert.Equal(GuildRaidEfedra.Question, question);
            Assert.Equal(new[] { GuildRaidEfedra.Bye }, replies);
            Assert.True(GuildRaidEfedra.Owns(GuildRaidEfedra.Challenge));
            Assert.True(GuildRaidEfedra.Owns(GuildRaidEfedra.AskRules));
            Assert.False(GuildRaidEfedra.Owns(GuildRaidEfedra.Question));
        }

        private static int ElementOf(GuildRaidEfedra.Board board, int piece) => board.Pieces.First(p => p.Value == piece).Key;

        /// <summary>
        /// No piece before the rules; the rules bring them out on the four boards; a piece where it
        /// should be turns green and counts once at the centre, one where it should not turns red, a
        /// decoy too, and none of them costs a life.
        /// </summary>
        [Fact]
        public async Task The_pieces_are_found_on_their_boards()
        {
            if (Patio == null) return;
            var raid = Raid(702);
            GuildRaidEfedra.Set(raid, new[] { 0, 1, 2, 3 });
            var first = Patio.Boards[0];

            Assert.False(GuildRaidEfedra.Offers(raid, first.MapId, ElementOf(first, 1)));
            Assert.False(await GuildRaidEfedra.PressAsync(null, raid, first.MapId, ElementOf(first, 1), GuildRaidEfedra.PieceSkill));

            var (rules, shrug) = await GuildRaidEfedra.RulesAsync(raid);
            Assert.Equal(GuildRaidEfedra.Rules, rules);
            Assert.Equal(new[] { GuildRaidEfedra.Shrug }, shrug);
            foreach (var board in Patio.Boards)
            {
                for (int k = 1; k <= GuildRaidEfedra.Pieces; k++)
                    Assert.Contains(GuildRaidStagings.IdOf(board.MapId, GuildRaidEfedra.Spawn(k)), GuildRaidStagings.Standing(raid, board.MapId));
            }
            Assert.True(GuildRaidEfedra.Offers(raid, first.MapId, ElementOf(first, 1)));

            // Piece 2 stands right on board 1, not on board 0.
            Assert.True(await GuildRaidEfedra.PressAsync(null, raid, first.MapId, ElementOf(first, 2), GuildRaidEfedra.PieceSkill));
            Assert.DoesNotContain(GuildRaidStagings.IdOf(first.MapId, GuildRaidEfedra.Green(2, 0)), GuildRaidStagings.Standing(raid, first.MapId));
            Assert.Equal(0, raid.Get(GuildRaidEfedra.FoundVariable));

            Assert.True(await GuildRaidEfedra.PressAsync(null, raid, first.MapId, ElementOf(first, 1), GuildRaidEfedra.PieceSkill));
            Assert.True(await GuildRaidEfedra.PressAsync(null, raid, first.MapId, ElementOf(first, 1), GuildRaidEfedra.PieceSkill));
            Assert.Contains(GuildRaidStagings.IdOf(first.MapId, GuildRaidEfedra.Green(1, 0)), GuildRaidStagings.Standing(raid, first.MapId));
            Assert.Equal(1, raid.Get(GuildRaidEfedra.FoundVariable));
            Assert.Contains(GuildRaidStagings.IdOf(Patio.Centre, string.Format(GuildRaidMonochrome.StepDone, 1)), GuildRaidStagings.Standing(raid, Patio.Centre));

            if (first.Decoys.Count > 0)
            {
                Assert.True(await GuildRaidEfedra.PressAsync(null, raid, first.MapId, first.Decoys.Keys.First(), GuildRaidEfedra.PieceSkill));
                Assert.Equal(1, raid.Get(GuildRaidEfedra.FoundVariable));
            }

            for (int k = 2; k <= GuildRaidEfedra.Pieces; k++)
            {
                var board = Patio.Boards[k - 1];
                Assert.True(await GuildRaidEfedra.PressAsync(null, raid, board.MapId, ElementOf(board, k), GuildRaidEfedra.PieceSkill));
            }
            Assert.Equal(15, raid.Get(GuildRaidEfedra.FoundVariable));
            Assert.Contains(GuildRaidStagings.IdOf(Patio.Centre, string.Format(GuildRaidMonochrome.StepDone, 4)), GuildRaidStagings.Standing(raid, Patio.Centre));
            Assert.Equal(Gardens.Health, raid.Health);
            Assert.False(GuildRaidEfedra.Solved(raid));     // the game is still to be won
            GuildRaidStagings.Forget(raid);
        }

        /// <summary>Belladona beaten is the game won: the Patio's goal, once.</summary>
        [Fact]
        public async Task Belladona_beaten_wins_the_game()
        {
            if (Patio == null) return;
            var raid = Raid(703);
            GuildRaidEfedra.Set(raid, new[] { 0, 1, 2, 3 });
            raid.SetHealth(Gardens.Health - 1);

            await GuildRaidEfedra.BeatenAsync(raid, new System.Collections.Generic.Dictionary<int, int> { [8245] = 1 });
            Assert.False(GuildRaidEfedra.Solved(raid));
            await GuildRaidEfedra.BeatenAsync(raid, new System.Collections.Generic.Dictionary<int, int> { [GuildRaidEfedra.Queen] = 1 });
            Assert.True(GuildRaidEfedra.Solved(raid));
            Assert.Equal(Gardens.Health, raid.Health);
            Assert.Equal(2000, raid.Score);
            GuildRaidStagings.Forget(raid);
        }
    }
}

using System;
using System.Linq;
using System.Threading.Tasks;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.World.Content;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// The Enclave de los protectores as the client's maps lay it out: its gardens, the Patio's four
    /// miniature boards with their squares and buttons, the statue, the sixteen protectors; the
    /// boards set right and wrong, and the goal's two steps. In the MapManager collection, as every
    /// test that loads the interactives.
    /// </summary>
    [Collection("MapManager")]
    public class GuildRaidEnclaveTests
    {
        public GuildRaidEnclaveTests() => Interactives.Initialize();

        private static RaidKind Gardens => Raids.Of(Raids.EternalGardens);

        private static GuildRaidEnclave.Work Enclave => GuildRaidEnclave.WorkOf(Gardens);

        private static RaidInstance Raid(long id)
        {
            var raid = new RaidInstance(id, Raids.EternalGardens, 1, 7001, DateTimeOffset.UtcNow, TimeSpan.FromHours(2));
            raid.SetHealth(Gardens.Health);
            return raid;
        }

        /// <summary>
        /// The gardens' maps around the Enclave's centre [11,20]; the four boards, each the garden of
        /// the same side of the Patio's centre -- 1 [9,14], 2 [10,13], 3 [11,14], 4 [10,15] -- with
        /// nine squares and its button.
        /// </summary>
        [Fact]
        public void The_enclave_is_read_off_the_client_s_maps()
        {
            if (Enclave == null) return;   // no world data

            Assert.Equal(238297345L, Enclave.Centre);
            Assert.Equal(new[] { 238296833L, 238297088L, 238297345L, 238297346L, 238297857L }, Enclave.Maps);
            Assert.Equal(238301953L, Enclave.Boards[1].MapId);
            Assert.Equal(238302466L, Enclave.Boards[2].MapId);
            Assert.Equal(238302977L, Enclave.Boards[3].MapId);
            Assert.Equal(238302208L, Enclave.Boards[4].MapId);
            Assert.Equal(539896, Enclave.Boards[2].Button.Id);
            foreach (var board in Enclave.Boards.Values)
            {
                Assert.Equal(9, board.Squares.Count);
                Assert.Equal(9, board.Squares.Values.Distinct().Count());
            }
        }

        /// <summary>
        /// Board 3 names its squares after their statuettes; the others are matched by nearness, and
        /// matching board 3 the same way gives what its names say.
        /// </summary>
        [Fact]
        public void A_square_toggles_the_statuette_beside_it()
        {
            if (Enclave == null) return;
            var named = Enclave.Boards[3].Squares;
            Assert.Equal((1, 1), named[539925]);
            Assert.Equal((1, 3), named[539927]);
            Assert.Equal((2, 2), named[539929]);
            Assert.Equal((3, 1), named[539931]);
            Assert.Equal((3, 3), named[539933]);

            // Board 2, whose squares carry no name: each takes the statuette nearest to it.
            var board = Enclave.Boards[2].Squares;
            Assert.Equal((1, 1), board[539888]);     // cell 270, its statuette's
            Assert.Equal((3, 1), board[539887]);     // cell 314
        }

        /// <summary>The four kinds by the Vigilante's numbers, and each one's protector in each colour.</summary>
        [Fact]
        public void The_protectors_by_kind_and_colour()
        {
            if (Enclave == null) return;
            var byMob = GuildRaidGuardians.CompanionByMob();
            Assert.Equal(8286, byMob[1]);   // Machacamelia
            Assert.Equal(8285, byMob[2]);   // Trilipán
            Assert.Equal(8288, byMob[3]);   // Daliana
            Assert.Equal(8287, byMob[4]);   // Muguetégida

            Assert.Equal(8255, GuildRaidEnclave.ProtectorOf(2, 1));   // Trilipán escarlata: fire, red
            Assert.Equal(8260, GuildRaidEnclave.ProtectorOf(1, 3));   // Machacamelia azulada: water, blue
            Assert.Equal(8271, GuildRaidEnclave.ProtectorOf(4, 2));   // Muguetégida viridina: air, green
            Assert.Equal(8274, GuildRaidEnclave.ProtectorOf(3, 4));   // Daliana ambarina: earth, amber
            Assert.True(GuildRaidEnclave.IsProtector(8255));
            Assert.False(GuildRaidEnclave.IsProtector(8285));
            Assert.Equal(16, GuildRaidEnclave.Placements.Select(p => GuildRaidEnclave.ProtectorOf(p.Kind, p.Colour)).Distinct().Count());
        }

        private static (int Square, int Statue, int Spot) SquareFor(int board, int statue, int spot)
        {
            var square = Enclave.Boards[board].Squares.First(s => s.Value == (statue, spot));
            return (square.Key, statue, spot);
        }

        private static async Task SetRightAsync(RaidInstance raid, int board)
        {
            var b = Enclave.Boards[board];
            for (int a = 1; a <= 3; a++)
            {
                var (square, _, _) = SquareFor(board, a, GuildRaidEnclave.SpotOf(raid, board, a));
                Assert.True(await GuildRaidEnclave.PressAsync(null, raid, b.MapId, square, GuildRaidEnclave.SquareSkill));
            }
            Assert.True(GuildRaidEnclave.Usable(raid, b.MapId, b.Button.Id));
            await GuildRaidEnclave.PressAsync(null, raid, b.MapId, b.Button.Id, GuildRaidEnclave.ButtonSkill);
        }

        /// <summary>
        /// The gardens copied on their boards: each turns green and counts on the centre; the four
        /// raise the statue there, in the Obra's colour once it is solved, and light it on the Patio;
        /// the right protector beaten solves the enigma.
        /// </summary>
        [Fact]
        public async Task The_gardens_copied_and_the_protector_beaten_solve_it()
        {
            if (Enclave == null) return;
            var raid = Raid(401);
            raid.Set(GuildRaidGuardians.ColourVariable, 1);    // fire: red
            GuildRaidEnclave.Set(raid, new[] { 1, 2, 3, 3, 2, 1, 2, 2, 2, 1, 3, 1 }, 2);
            raid.ChangeHealth(-3, Gardens.Health);

            // Garden 1's statue 1 stands on spot 1, on every map that shows it.
            Assert.Contains(GuildRaidStagings.IdOf(238296833, GuildRaidEnclave.GardenStatue(1, 1, 1)), GuildRaidStagings.Standing(raid, 238296833));

            for (int board = 1; board <= 4; board++)
            {
                await SetRightAsync(raid, board);
                Assert.True(GuildRaidEnclave.Validated(raid, board));
                long map = Enclave.Boards[board].MapId;
                Assert.Contains(GuildRaidStagings.IdOf(map, GuildRaidEnclave.BoardRight), GuildRaidStagings.Standing(raid, map));
                Assert.False(GuildRaidEnclave.Usable(raid, map, Enclave.Boards[board].Squares.Keys.First()));
                Assert.Contains(GuildRaidStagings.IdOf(Enclave.Centre, $"#SD Logo{board}_spawn"), GuildRaidStagings.Standing(raid, Enclave.Centre));
            }

            Assert.Contains(GuildRaidStagings.IdOf(Enclave.Centre, GuildRaidEnclave.FinalStatue(2)), GuildRaidStagings.Standing(raid, Enclave.Centre));
            Assert.DoesNotContain(GuildRaidStagings.IdOf(Enclave.Centre, "Halo_rouge"), GuildRaidStagings.Standing(raid, Enclave.Centre));
            long patio = 238302465;
            Assert.Contains(GuildRaidStagings.IdOf(patio, GuildRaidEnclave.PatioStatue(2)), GuildRaidStagings.Standing(raid, patio));
            Assert.Equal(1, raid.GoalAt(GuildRaidEnclave.Goal));
            Assert.Equal(0, raid.Score);

            // The Obra solved now: the statue takes the raid's colour.
            raid.SetGoal(GuildRaidMonochrome.Goal, 1);
            await GuildRaidEnclave.ObraSolvedAsync(raid);
            Assert.Contains(GuildRaidStagings.IdOf(Enclave.Centre, "Halo_rouge"), GuildRaidStagings.Standing(raid, Enclave.Centre));

            // A wrong protector beaten moves nothing; the right one -- Trilipán escarlata -- solves it.
            await GuildRaidEnclave.BeatenAsync(raid, new System.Collections.Generic.Dictionary<int, int> { [8256] = 1 });
            Assert.Equal(1, raid.GoalAt(GuildRaidEnclave.Goal));
            Assert.Equal(8255, GuildRaidEnclave.RightProtector(raid));
            await GuildRaidEnclave.BeatenAsync(raid, new System.Collections.Generic.Dictionary<int, int> { [8255] = 1 });
            Assert.Equal(2, raid.GoalAt(GuildRaidEnclave.Goal));
            Assert.Equal(2000, raid.Score);
            Assert.Equal(Gardens.Health - 2, raid.Health);
            GuildRaidStagings.Forget(raid);
        }

        /// <summary>
        /// A board set wrong costs a life and is bare again; the button lights only with three
        /// statuettes standing.
        /// </summary>
        [Fact]
        public async Task A_board_set_wrong_costs_a_life()
        {
            if (Enclave == null) return;
            var raid = Raid(402);
            GuildRaidEnclave.Set(raid, new[] { 1, 1, 1, 2, 2, 2, 3, 3, 3, 1, 2, 3 }, 1);
            var board = Enclave.Boards[2];

            var (first, _, _) = SquareFor(2, 1, 1);    // garden 2 wants spots 2, 2, 2
            var (second, _, _) = SquareFor(2, 2, 2);
            var (third, _, _) = SquareFor(2, 3, 2);
            await GuildRaidEnclave.PressAsync(null, raid, board.MapId, first, GuildRaidEnclave.SquareSkill);
            await GuildRaidEnclave.PressAsync(null, raid, board.MapId, second, GuildRaidEnclave.SquareSkill);
            Assert.False(GuildRaidEnclave.Usable(raid, board.MapId, board.Button.Id));
            await GuildRaidEnclave.PressAsync(null, raid, board.MapId, third, GuildRaidEnclave.SquareSkill);
            Assert.True(GuildRaidEnclave.Usable(raid, board.MapId, board.Button.Id));
            Assert.Contains(GuildRaidStagings.IdOf(board.MapId, GuildRaidEnclave.ButtonOn), GuildRaidStagings.Standing(raid, board.MapId));

            await GuildRaidEnclave.PressAsync(null, raid, board.MapId, board.Button.Id, GuildRaidEnclave.ButtonSkill);
            Assert.False(GuildRaidEnclave.Validated(raid, 2));
            Assert.Empty(GuildRaidEnclave.Lit(raid, 2));
            Assert.Equal(Gardens.Health - 1, raid.Health);
            Assert.DoesNotContain(GuildRaidStagings.IdOf(board.MapId, GuildRaidEnclave.SquareOn(1, 1)), GuildRaidStagings.Standing(raid, board.MapId));
            Assert.DoesNotContain(GuildRaidStagings.IdOf(board.MapId, GuildRaidEnclave.ButtonOn), GuildRaidStagings.Standing(raid, board.MapId));

            // A square clicked twice takes its statuette away again.
            await GuildRaidEnclave.PressAsync(null, raid, board.MapId, first, GuildRaidEnclave.SquareSkill);
            await GuildRaidEnclave.PressAsync(null, raid, board.MapId, first, GuildRaidEnclave.SquareSkill);
            Assert.Empty(GuildRaidEnclave.Lit(raid, 2));
            GuildRaidStagings.Forget(raid);
        }

        /// <summary>
        /// As a raid starts the Enclave's maps lose the world's groups and get the sixteen protectors
        /// alone, four on each garden's map, each kind and each colour once there.
        /// </summary>
        [Fact]
        public void The_enclave_starts_with_its_sixteen_protectors()
        {
            if (Enclave == null) return;
            if (MapManager.GetMapInfo(238297346) == null) MapManager.Initialize();
            if (MapManager.GetMapInfo(238297346) == null) return;
            MobSpawnManager.EnsureMonsterData();
            var maps = DatabaseManager.MapsOfSubArea(Gardens.Floors[GuildRaidEnclave.Zone - 1]);
            try
            {
                Assert.Equal(16, GuildRaidEnclave.Populate(Gardens, new Random(7)));
                Assert.Empty(MobSpawnManager.GetMobsForMap(Enclave.Centre));
                foreach (long garden in new[] { 238296833L, 238297088L, 238297346L, 238297857L })
                {
                    var groups = MobSpawnManager.GetMobsForMap(garden);
                    Assert.Equal(4, groups.Count);
                    Assert.All(groups, g => Assert.True(GuildRaidEnclave.IsProtector(g.Members.Single().Monster.Id)));
                }
                // [11,21]: Dahliane ambré, Fracamélia azuré, Muguégide écarlate, Tritulipe viridine (the guide).
                Assert.Equal(new[] { 8258, 8260, 8268, 8274 },
                             MobSpawnManager.GetMobsForMap(238297088).Select(g => g.Members.Single().Monster.Id).OrderBy(i => i));
            }
            finally
            {
                foreach (long map in maps) MobSpawnManager.RestoreMap(map);
            }
        }
    }
}

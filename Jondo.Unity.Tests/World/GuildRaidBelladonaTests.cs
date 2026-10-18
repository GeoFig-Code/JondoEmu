using System;
using System.Linq;
using System.Threading.Tasks;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.World.Content;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// The Reserva de Belladona's battleship as the client's maps lay it out: the Obra's paper boats,
    /// the two boards with their seven balls each, the centre; the draw, a boat revealed, shots that
    /// sink, a miss that floats everything again, and the six sunk. In the MapManager collection, as
    /// every test that loads the interactives.
    /// </summary>
    [Collection("MapManager")]
    public class GuildRaidBelladonaTests
    {
        public GuildRaidBelladonaTests() => Interactives.Initialize();

        private static RaidKind Gardens => Raids.Of(Raids.EternalGardens);

        private static GuildRaidBelladona.Work Reserva => GuildRaidBelladona.WorkOf(Gardens);

        private static RaidInstance Raid(long id)
        {
            var raid = new RaidInstance(id, Raids.EternalGardens, 1, 7001, DateTimeOffset.UtcNow, TimeSpan.FromHours(2));
            raid.SetHealth(Gardens.Health);
            return raid;
        }

        /// <summary>The boards: [21,17] first, [19,15] second, their balls by the switches they stand on; the boats of the Obra.</summary>
        [Fact]
        public void The_reserva_is_read_off_the_client_s_maps()
        {
            if (Reserva == null) return;   // no world data

            Assert.Equal(238300160L, Reserva.Boards[0].MapId);
            Assert.Equal(238299394L, Reserva.Boards[1].MapId);
            Assert.Equal(238299905L, Reserva.Centre);
            Assert.Equal((true, 1), Reserva.Boards[0].Balls[540002]);    // A, cell 486
            Assert.Equal((true, 4), Reserva.Boards[0].Balls[540005]);    // D, cell 445
            Assert.Equal((false, 1), Reserva.Boards[0].Balls[540006]);   // 1, cell 389
            Assert.Equal((false, 3), Reserva.Boards[1].Balls[540015]);   // 3, cell 217
            Assert.All(Reserva.Boards, b => Assert.Equal(7, b.Balls.Count));
            Assert.Contains(Reserva.Slots, s => s.MapId == 238294016 && s.Element.Id == 539921 && s.Number == 1);
            Assert.True(Reserva.Slots.Count >= GuildRaidBelladona.Boats);
        }

        /// <summary>Six boats of the Obra, three squares on each board, and the first board's turn.</summary>
        [Fact]
        public void The_draw_hides_six_boats_and_their_squares()
        {
            if (Reserva == null) return;
            var raid = Raid(601);
            GuildRaidBelladona.Draw(raid, new Random(3));

            var slots = raid.SequenceOf(GuildRaidBelladona.SlotsSequence);
            var places = raid.SequenceOf(GuildRaidBelladona.PlacesSequence);
            Assert.Equal(6, slots.Distinct().Count());
            Assert.Equal(6, places.Distinct().Count());
            Assert.Equal(3, places.Count(p => p < 12));
            foreach (int slot in slots)
            {
                var boat = Reserva.Slots[slot];
                Assert.Contains(GuildRaidStagings.IdOf(boat.MapId, GuildRaidBelladona.BoatShown(boat.Number)), GuildRaidStagings.Standing(raid, boat.MapId));
            }
            Assert.Contains(GuildRaidStagings.IdOf(238300160, GuildRaidBelladona.YourTurn), GuildRaidStagings.Standing(raid, 238300160));
            Assert.Contains(GuildRaidStagings.IdOf(238299394, GuildRaidBelladona.NotYourTurn), GuildRaidStagings.Standing(raid, 238299394));
            GuildRaidStagings.Forget(raid);
        }

        private static async Task FireAsync(RaidInstance raid, int board, int letter, int number)
        {
            var balls = Reserva.Boards[board].Balls;
            int l = balls.First(b => b.Value == (true, letter)).Key, n = balls.First(b => b.Value == (false, number)).Key;
            await GuildRaidBelladona.PressAsync(null, raid, Reserva.Boards[board].MapId, l, GuildRaidBelladona.BallSkill);
            await GuildRaidBelladona.PressAsync(null, raid, Reserva.Boards[board].MapId, n, GuildRaidBelladona.BallSkill);
        }

        private static async Task RevealAllAsync(RaidInstance raid)
        {
            foreach (int slot in raid.SequenceOf(GuildRaidBelladona.SlotsSequence))
            {
                var boat = Reserva.Slots[slot];
                Assert.True(GuildRaidBelladona.Usable(raid, boat.MapId, boat.Element.Id));
                Assert.True(await GuildRaidBelladona.PressAsync(null, raid, boat.MapId, boat.Element.Id, GuildRaidBelladona.BoatSkill));
                Assert.False(GuildRaidBelladona.Usable(raid, boat.MapId, boat.Element.Id));
                Assert.DoesNotContain(GuildRaidStagings.IdOf(boat.MapId, GuildRaidBelladona.BoatShown(boat.Number)), GuildRaidStagings.Standing(raid, boat.MapId));
            }
        }

        /// <summary>
        /// The boats revealed float on their squares; the boards fire in turn and sink them; a miss
        /// floats every boat again, costs a life and gives [21,17] the first shot; the six sunk solve it.
        /// </summary>
        [Fact]
        public async Task The_boats_are_sunk_in_turn()
        {
            if (Reserva == null) return;
            var raid = Raid(602);
            // Board 0: A1, A2, A3. Board 1: A1, B2, C3.
            GuildRaidBelladona.Set(raid, new[] { 0, 1, 2, 3, 4, 5 }, new[] { 0, 1, 2, 12, 16, 20 });
            await RevealAllAsync(raid);
            Assert.Contains(GuildRaidStagings.IdOf(238299394, GuildRaidBelladona.Floating(2, 2)), GuildRaidStagings.Standing(raid, 238299394));

            // Board 1 cannot fire on board 0's turn.
            await FireAsync(raid, 1, 1, 1);
            Assert.Equal(0, raid.Get(GuildRaidBelladona.TurnVariable));

            await FireAsync(raid, 0, 1, 1);                // board 1's A1: sunk
            Assert.Equal(1, raid.GoalAt(GuildRaidBelladona.Goal));
            Assert.Equal(1, raid.Get(GuildRaidBelladona.TurnVariable));
            Assert.Contains(GuildRaidStagings.IdOf(238299394, GuildRaidBelladona.Sunk(1, 1)), GuildRaidStagings.Standing(raid, 238299394));
            await FireAsync(raid, 1, 1, 2);                // board 0's A2: sunk
            Assert.Equal(2, raid.GoalAt(GuildRaidBelladona.Goal));

            await FireAsync(raid, 0, 4, 3);                // board 1's D3: nothing there
            Assert.Equal(0, raid.GoalAt(GuildRaidBelladona.Goal));
            Assert.Equal(Gardens.Health - 1, raid.Health);
            Assert.Equal(0, raid.Get(GuildRaidBelladona.TurnVariable));
            Assert.DoesNotContain(GuildRaidStagings.IdOf(238299394, GuildRaidBelladona.Sunk(1, 1)), GuildRaidStagings.Standing(raid, 238299394));
            Assert.Contains(GuildRaidStagings.IdOf(238299394, GuildRaidBelladona.Floating(1, 1)), GuildRaidStagings.Standing(raid, 238299394));

            // All six, in turn.
            await FireAsync(raid, 0, 1, 1);
            await FireAsync(raid, 1, 1, 1);
            await FireAsync(raid, 0, 2, 2);
            await FireAsync(raid, 1, 1, 2);
            await FireAsync(raid, 0, 3, 3);
            await FireAsync(raid, 1, 1, 3);
            Assert.True(GuildRaidBelladona.Solved(raid));
            Assert.Equal(2000, raid.Score);
            Assert.Equal(Gardens.Health, raid.Health);
            Assert.Contains(GuildRaidStagings.IdOf(238299905, "#SD Logo6_spawn"), GuildRaidStagings.Standing(raid, 238299905));
            GuildRaidStagings.Forget(raid);
        }

        /// <summary>A boat not revealed is not there to be hit: a shot at its square misses.</summary>
        [Fact]
        public async Task A_boat_not_revealed_is_missed()
        {
            if (Reserva == null) return;
            var raid = Raid(603);
            GuildRaidBelladona.Set(raid, new[] { 0, 1, 2, 3, 4, 5 }, new[] { 0, 1, 2, 12, 16, 20 });
            await FireAsync(raid, 0, 1, 1);
            Assert.Equal(0, raid.GoalAt(GuildRaidBelladona.Goal));
            Assert.Equal(Gardens.Health - 1, raid.Health);
            GuildRaidStagings.Forget(raid);
        }
    }
}

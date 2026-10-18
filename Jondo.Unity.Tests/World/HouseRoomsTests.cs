using System.Linq;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Managers;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// The Palacio de los Lagos encantados: its door leads to its own entrance, not to a residence;
    /// its rooms are the house's; the client's world graph joins them, stars and doors alike, the
    /// vault door to the treasure room's chests among them.
    /// In the MapManager collection, as every test that loads the maps.
    /// </summary>
    [Collection("MapManager")]
    public class HouseRoomsTests
    {
        private const long Street = 75629314;
        private const int Door = 475545;
        private const long Hall = 85459457;
        private const long Cave = 85459459, Treasure = 85460483, GardenRoom = 85460481, Bedroom = 85461505;

        private static void Load()
        {
            if (!MapManager.WalkableCells.ContainsKey(Hall)) MapManager.Initialize();
            Interactives.Initialize();
            Houses.Initialize();
        }

        [Fact]
        public void The_palace_door_leads_to_its_own_entrance()
        {
            Load();
            Assert.True(Houses.TryGetDoor(Street, Door, out var door));
            Assert.Equal(Hall, door.InteriorMapId);
            Assert.True(Houses.TryGetExit(Hall, out var exit));
            Assert.Equal(3518, exit.Gfx);

            Assert.Equal(14, Houses.RoomsOf(Hall).Count);
            Assert.Equal(Hall, Houses.HallOf(Treasure));
            Assert.True(Houses.IsInterior(Treasure));
            Assert.True(Houses.TryGetWayBack(Treasure, out var back));
            Assert.Equal(Door, back.ElementId);
            Assert.Equal(6, Houses.ChestsIn(Treasure).Count);
        }

        [Fact]
        public void The_world_graph_joins_every_room_by_its_stars_and_doors()
        {
            Load();
            TeleportManager.Initialize();
            var rooms = new[] { Hall }.Concat(Houses.RoomsOf(Hall)).ToList();

            // Both halves of the entrance's star lead to the garden room, by element and by cell,
            // and nobody lands on a star.
            foreach (var star in Interactives.ElementsOf(Hall).Where(e => e.Gfx == 3507))
            {
                Assert.True(TeleportManager.TryGet(Hall, star.Id, out var byElement));
                Assert.Equal(GardenRoom, byElement.DestinationMapId);
                Assert.True(TeleportManager.TryGetCellTrigger(Hall, star.Cell, out var byCell));
                Assert.Equal(GardenRoom, byCell.DestinationMapId);
                Assert.DoesNotContain(Interactives.ElementsOf(GardenRoom),
                                      e => e.Gfx == 3507 && e.Cell == byElement.DestinationCellId);
            }

            // The vault door, to the treasure room; the bedroom's door, back to the garden room.
            var vault = Interactives.ElementsOf(Cave).Single(e => e.Gfx == 31534);
            Assert.True(TeleportManager.TryGet(Cave, vault.Id, out var toTreasure));
            Assert.Equal(Treasure, toTreasure.DestinationMapId);
            Assert.Contains(TeleportManager.On(Bedroom), r => r.DestinationMapId == GardenRoom);

            // Every room is reached and can be left.
            foreach (long room in rooms)
            {
                Assert.NotEmpty(TeleportManager.On(room));
                Assert.Contains(rooms, r => TeleportManager.On(r).Any(w => w.DestinationMapId == room));
            }
        }
    }
}

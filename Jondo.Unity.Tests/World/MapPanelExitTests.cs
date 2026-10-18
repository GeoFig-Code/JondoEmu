using System.Threading.Tasks;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.Tests.Economy;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// The map panel's «leave the dungeon» (kjw): out through the dungeon's exit, and nothing
    /// anywhere else.
    /// </summary>
    [Collection("MapManager")]
    public sealed class MapPanelExitTests
    {
        /// <summary>The Corte del Jalató Real's first room, and its exit -- the door it is entered by.</summary>
        private const long Room = 121373185, Exit = 120063489;

        [Fact]
        public async Task Leaving_a_dungeon_takes_the_player_out_by_its_exit()
        {
            if (MapManager.GetMapInfo(Room) == null) MapManager.Initialize();
            DungeonManager.Initialize();
            Assert.Equal(Exit, DungeonManager.WayOut(DungeonManager.OfRoom(Room)!));

            await using var pipe = await ClientPipe.OpenAsync(8_920_000_001, 8_920_100_001, Exit, "Sale-Ya");
            using (SessionContext.Push(pipe.Session))
            {
                // Outside a dungeon the option does nothing.
                Assert.False(await DungeonHandler.LeaveAsync(pipe.ToClient));
                Assert.Equal(Exit, pipe.Session.State.MapId);

                pipe.Session.State.MapId = Room;
                Assert.True(await DungeonHandler.LeaveAsync(pipe.ToClient));
                Assert.Equal(Exit, pipe.Session.State.MapId);
            }
        }
    }
}

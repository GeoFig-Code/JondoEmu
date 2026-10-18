using System.Linq;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.Tests.Economy;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// The karts of route 666: seven stops named by the client's transport hints, a vehicle at each,
    /// and the fare its text gives; a kart lists the other stops and takes one there for 100 kamas.
    /// In the MapManager collection, as every test that loads the maps.
    /// </summary>
    [Collection("MapManager")]
    public class TransportTests
    {
        private const long North = 171706884;      // [-23,-7], the Altos de los Aullidos
        private const long South = 172232704;      // [-24,29]

        private static void Load()
        {
            if (!MapManager.WalkableCells.ContainsKey(North)) MapManager.Initialize();
            Interactives.Initialize();
            Transports.Initialize();
        }

        [Fact]
        public void The_karts_of_route_666_are_seven_stops_at_100_kamas()
        {
            Load();
            var kart = Transports.Networks.Single();
            Assert.Equal("Kart de Nodoymas", kart.Name);
            Assert.Equal(100, kart.Cost);
            Assert.Equal(7, kart.Stops.Count);
            Assert.Equal(North, Transports.On(North)!.Value.Stop.MapId);
            Assert.Equal(538780, Transports.VehiclesOn(North).Single().Id);

            // A kart on a map's border shows on its neighbour too, and is the same stop's.
            var border = kart.Stops.First(s => s.Maps.Count > 1);
            long neighbour = border.Maps.First(m => m != border.MapId);
            Assert.Equal(border.MapId, Transports.On(neighbour)!.Value.Stop.MapId);
        }

        [Fact]
        public async Task A_kart_lists_its_stops_and_takes_one_there_for_the_fare()
        {
            Load();
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(DatabaseManager.WorldConnectionString))
            {
                connection.Open();
                DatabaseManager.MoveScrollsOutOfTheBase(connection);
            }
            await using var pipe = await ClientPipe.OpenAsync(8_850_000_001, 8_850_100_001, North, "Kart");
            pipe.Session.State.Kamas = 1000;
            using (SessionContext.Push(pipe.Session))
            {
                var vehicle = Transports.VehiclesOn(North).Single();
                await TransportTravelHandler.OpenAsync(pipe.ToClient, vehicle, Transports.UseSkill);
                await pipe.Next(Op.Iwn);
                await pipe.Next(Op.Hjj);

                await ZaapTravelHandler.TravelAsync(pipe.ToClient,
                    ConnectionProtocol.Push(Op.Hjc, Pb.New().Var(2, Zaapis.Kind).Var(3, South).Build()));
                Assert.Equal(South, pipe.Session.State.MapId);
                Assert.Equal(900, pipe.Session.State.Kamas);
                Assert.Contains(pipe.Session.State.CellId, MapManager.WalkableCells[South]);
            }
        }
    }
}

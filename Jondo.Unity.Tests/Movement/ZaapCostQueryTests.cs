using System.Linq;
using System.Threading.Tasks;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.Tests.Economy;
using Xunit;

namespace Jondo.Unity.Tests.Movement
{
    /// <summary>
    /// hir: what a zaap trip to a map costs from here, answered him with the map and the price,
    /// and no price for the map one stands on. In the MapManager collection, as every test that
    /// loads the maps.
    /// </summary>
    [Collection("MapManager")]
    public sealed class ZaapCostQueryTests
    {
        private const long Here = 191105026, There = 165153537;

        [Fact]
        public async Task The_zaap_cost_to_a_map_is_told_with_the_map()
        {
            if (MapManager.GetMapInfo(Here) == null) MapManager.Initialize();
            await using var pipe = await ClientPipe.OpenAsync(8_860_000_001, 8_860_100_001, Here, "Coste");
            using (SessionContext.Push(pipe.Session))
            {
                await ZaapTravelHandler.TellCostAsync(pipe.ToClient,
                    ConnectionProtocol.Push("hir", Pb.New().Var(2, There).Build()));
                var him = ProtoMessage.Parse(await pipe.Next("him")).Fields;
                Assert.Equal(There, him.Single(f => f.FieldNumber == 1).VarIntValue);
                Assert.Equal(ZaapTravelHandler.CostBetween(Here, There), him.Single(f => f.FieldNumber == 2).VarIntValue);
                Assert.True(ZaapTravelHandler.CostBetween(Here, There) > 0);

                await ZaapTravelHandler.TellCostAsync(pipe.ToClient,
                    ConnectionProtocol.Push("hir", Pb.New().Var(2, Here).Build()));
                var free = ProtoMessage.Parse(await pipe.Next("him")).Fields;
                Assert.DoesNotContain(free, f => f.FieldNumber == 2);
            }
        }
    }
}

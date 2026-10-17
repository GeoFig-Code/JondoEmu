using System.Linq;
using Jondo.Unity.Server.Managers;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// The Dream Market's two vendors: Neru Stalar sells in dream fragments, what the end of a
    /// dream pays, and Gobribe in their bags -- the guide's two tables, item for item.
    /// </summary>
    [Collection("npc shops")]
    public class DreamMarketTests
    {
        private const int NeruStalar = 7839, Gobribe = 8000;
        private const int Fragments = 32080, FragmentBag = 34274;

        [Fact]
        public void Neru_Stalar_sells_for_dream_fragments()
        {
            NpcShops.Initialize();
            TokenShops.Initialize();
            var shop = TokenShops.Of(NeruStalar);
            Assert.NotNull(shop);
            Assert.Equal(Fragments, shop!.TokenGid);
            Assert.Equal(new[] { 32079, 21964, 32078, 21965, 21966, 21967, 21969, 21968, FragmentBag },
                         NpcShops.CatalogueOf(NeruStalar).ToArray());
            Assert.Equal(2, TokenShops.PriceOf(shop, 32079));
            Assert.Equal(960, TokenShops.PriceOf(shop, 21968));
            Assert.Equal(1000, TokenShops.PriceOf(shop, FragmentBag));
        }

        [Fact]
        public void Gobribe_sells_for_bags_of_them()
        {
            NpcShops.Initialize();
            TokenShops.Initialize();
            var shop = TokenShops.Of(Gobribe);
            Assert.NotNull(shop);
            Assert.Equal(FragmentBag, shop!.TokenGid);
            Assert.Equal(9, NpcShops.CatalogueOf(Gobribe).Count);
            Assert.Equal(20, TokenShops.PriceOf(shop, 32143));
            Assert.Equal(40, TokenShops.PriceOf(shop, 32091));
        }
    }
}

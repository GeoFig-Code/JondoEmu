using System.IO;
using System.Linq;
using Jondo.Unity.Launcher;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// Gremia, the Guild Temple's seller, and the layer of hand-written shops.
    /// </summary>
    /// <remarks>
    /// Measured: her kbd in the capture of founding «Jondo» lists the guildalogem at 30,000, the book
    /// «Acerca de los gremios» at 500 and the guild shield at 100,000. Here the guildalogem costs one
    /// kama, which is this server's decision, and the other two what the capture says. She also sells
    /// the alliance gem for one kama: nothing in the client's data gives it, and founding an
    /// alliance needs one; the alliance's name and emblem potions, which nothing gives either; and
    /// the alliances' tax collector potions and prisms.
    /// </remarks>
    [Collection("npc shops")]
    public class GuildVendorTests
    {
        [Fact]
        public void Gremia_sells_the_guildalogem_for_one_kama()
        {
            NpcShops.Forget();
            NpcShops.ApplyAuthored(Paths.ContentFile(NpcShops.AuthoredFile));

            const int gremia = 7580;
            Assert.True(NpcShops.Sells(gremia));
            Assert.Equal(new[] { GuildHandler.GuildalogemTemplate, 30810, 13240, AllianceHandler.AllianceGemTemplate,
                                 AllianceHandler.RenamePotionTemplate, AllianceHandler.ReEmblemPotionTemplate },
                         NpcShops.CatalogueOf(gremia).Take(6));
            Assert.Equal(1, NpcShops.PriceOf(GuildHandler.GuildalogemTemplate));
            Assert.Equal(500, NpcShops.PriceOf(30810));
            Assert.Equal(100000, NpcShops.PriceOf(13240));
            Assert.Equal(1, NpcShops.PriceOf(AllianceHandler.AllianceGemTemplate));
            Assert.Equal(1, NpcShops.PriceOf(AllianceHandler.RenamePotionTemplate));
            Assert.Equal(1, NpcShops.PriceOf(AllianceHandler.ReEmblemPotionTemplate));

            NpcShops.Forget();
        }

        /// <summary>
        /// And the alliances' tax collector potions and prisms, which no vendor of the client's data
        /// sells: one of each band of subarea levels, at what their templates say.
        /// </summary>
        [Fact]
        public void Gremia_sells_the_tax_collector_potions_and_the_prisms()
        {
            NpcShops.Forget();
            NpcShops.ApplyAuthored(Paths.ContentFile(NpcShops.AuthoredFile));

            var catalogue = NpcShops.CatalogueOf(7580);
            int[] potions = { 27635, 27905, 27906, 14553, 27907, 27908, 27909, 27910, 27911, 27912 };
            int[] prisms = { 27913, 27914, 27915, 14293, 27916, 27917, 27918, 27919, 27920, 27921 };
            Assert.All(potions, gid => Assert.Contains(gid, catalogue));
            Assert.All(prisms, gid => Assert.Contains(gid, catalogue));
            Assert.All(potions, gid => Assert.Equal(100, NpcShops.PriceOf(gid)));
            Assert.All(prisms, gid => Assert.Equal(1, NpcShops.PriceOf(gid)));

            // And the seven resources a tax collector's equipment is crafted from.
            Assert.All(Enumerable.Range(27929, 7), gid => Assert.Contains(gid, catalogue));

            NpcShops.Forget();
        }

        /// <summary>
        /// Hal Ylanza, at the Temple of Alliances, sells Gremia's catalogue at Gremia's prices:
        /// her entry names him in «alsoSoldBy».
        /// </summary>
        [Fact]
        public void Hal_Ylanza_sells_what_Gremia_sells()
        {
            NpcShops.Forget();
            NpcShops.ApplyAuthored(Paths.ContentFile(NpcShops.AuthoredFile));

            const int halYlanza = 2142;
            Assert.True(NpcShops.Sells(halYlanza));
            Assert.Equal(NpcShops.CatalogueOf(7580), NpcShops.CatalogueOf(halYlanza));
            Assert.Contains(AllianceHandler.AllianceGemTemplate, NpcShops.CatalogueOf(halYlanza));

            NpcShops.Forget();
        }

        /// <summary>A file that is not there breaks nothing, and a half-written one does not either.</summary>
        [Fact]
        public void A_missing_or_empty_file_changes_nothing()
        {
            NpcShops.Forget();
            NpcShops.ApplyAuthored(Path.Combine(Path.GetTempPath(), "no-existe-" + Path.GetRandomFileName()));
            Assert.Equal(0, NpcShops.Count);

            string file = Path.GetTempFileName();
            try
            {
                File.WriteAllText(file, "{ \"shops\": [ { \"npc\": 1, \"items\": [] }, { \"npc\": 2 } ] }");
                NpcShops.ApplyAuthored(file);
                Assert.Equal(0, NpcShops.Count);
            }
            finally
            {
                File.Delete(file);
                NpcShops.Forget();
            }
        }
    }
}

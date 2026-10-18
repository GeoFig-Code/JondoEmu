using System;
using System.Linq;
using System.Threading.Tasks;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.World.Content;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// The Obra monocromática as the client's maps lay it out: its eight objects, its flowers, its
    /// centre and the Enclave's pedestals; the draw, the flowers activated in order, a wrong one,
    /// and the stagings that show all of it (hpm). In the MapManager collection, as every test that
    /// loads the interactives.
    /// </summary>
    [Collection("MapManager")]
    public class GuildRaidMonochromeTests
    {
        public GuildRaidMonochromeTests() => Interactives.Initialize();

        private static RaidKind Gardens => Raids.Of(Raids.EternalGardens);

        private static GuildRaidMonochrome.Work Obra => GuildRaidMonochrome.WorkOf(Gardens);

        private static RaidInstance Raid(long id)
        {
            var raid = new RaidInstance(id, Raids.EternalGardens, 1, 7001, DateTimeOffset.UtcNow, TimeSpan.FromHours(2));
            raid.SetHealth(Gardens.Health);
            return raid;
        }

        /// <summary>
        /// Eight objects, each with its map and its flower; the centre, [22,8], with the statue's
        /// steps; pedestals I and III on [11,21], II and IV on [12,20] (the guides), IV from [11,20] too.
        /// </summary>
        [Fact]
        public void The_obra_is_read_off_the_client_s_maps()
        {
            if (Obra == null) return;   // no world data

            Assert.Equal(new[] { "arakne", "bague", "bougie", "crayon", "fils", "kamas", "lanterne", "regle" }, Obra.Objects);
            Assert.Equal(238294016L, Obra.Flowers["bougie"].MapId);     // [22,9], the candle
            Assert.Equal(238295040L, Obra.Flowers["lanterne"].MapId);   // [23,9], the lantern
            Assert.All(Obra.Flowers.Values, f => Assert.Contains(f.Flower.Gfx, GuildRaidMonochrome.FlowerGfx));
            Assert.Equal(238294274L, Obra.Centre);
            Assert.Equal(new[] { 238297088L }, Obra.PedestalMaps[1]);
            Assert.Equal(new[] { 238297857L }, Obra.PedestalMaps[2]);
            Assert.Equal(new[] { 238297088L }, Obra.PedestalMaps[3]);
            Assert.Equal(new[] { 238297345L, 238297857L }, Obra.PedestalMaps[4]);   // IV is seen from [11,20] too
        }

        /// <summary>
        /// The draw puts the eight objects on the pedestals, each once, and one flower per pedestal on
        /// the map of one of its two objects; the raid has them all standing.
        /// </summary>
        [Fact]
        public void The_draw_puts_objects_on_the_pedestals_and_a_flower_for_each()
        {
            if (Obra == null) return;
            var raid = Raid(301);
            GuildRaidMonochrome.Draw(raid, new Random(7));

            var pedestals = raid.SequenceOf(GuildRaidMonochrome.PedestalsSequence);
            var flowers = raid.SequenceOf(GuildRaidMonochrome.FlowersSequence);
            Assert.Equal(Enumerable.Range(0, 8), pedestals.OrderBy(p => p));
            Assert.Equal(4, flowers.Count);
            for (int p = 0; p < 4; p++) Assert.Contains(flowers[p], new[] { pedestals[p * 2], pedestals[p * 2 + 1] });

            // Pedestal I's two objects stand on [11,21], the first and its "bis".
            long first = Obra.PedestalMaps[1][0];
            var standing = GuildRaidStagings.Standing(raid, first);
            Assert.Contains(GuildRaidStagings.IdOf(first, GuildRaidMonochrome.OnPedestal(Obra.Objects[pedestals[0]], 1, false)), standing);
            Assert.Contains(GuildRaidStagings.IdOf(first, GuildRaidMonochrome.OnPedestal(Obra.Objects[pedestals[1]], 1, true)), standing);

            // The four flowers stand on their maps, the other four maps show none.
            foreach (var (name, (map, flower)) in Obra.Flowers)
            {
                bool has = flowers.Select(f => Obra.Objects[f]).Contains(name);
                Assert.Equal(has, GuildRaidStagings.Standing(raid, map).Contains(GuildRaidStagings.IdOf(map, GuildRaidMonochrome.FlowerPrefix + name)));
                Assert.Equal(has, GuildRaidMonochrome.Offers(raid, map, flower.Id));
            }
            GuildRaidStagings.Forget(raid);
        }

        private static (long Map, int Element) FlowerOf(RaidInstance raid, int pedestal)
        {
            var flower = Obra.Flowers[Obra.Objects[raid.SequenceOf(GuildRaidMonochrome.FlowersSequence)[pedestal]]];
            return (flower.MapId, flower.Flower.Id);
        }

        /// <summary>
        /// The four flowers in the pedestals' order: each turns green and marks the centre; the
        /// fourth meets the goal, gives a life back and colours the Obra in the raid's colour.
        /// </summary>
        [Fact]
        public async Task The_flowers_in_order_solve_it()
        {
            if (Obra == null) return;
            var raid = Raid(302);
            raid.Set(GuildRaidGuardians.ColourVariable, 3);     // eau: blue
            GuildRaidMonochrome.Set(raid, new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, new[] { 1, 2, 5, 6 });
            raid.ChangeHealth(-2, Gardens.Health);

            for (int pedestal = 0; pedestal < 4; pedestal++)
            {
                var (map, element) = FlowerOf(raid, pedestal);
                Assert.True(GuildRaidMonochrome.Usable(raid, map, element));
                Assert.True(await GuildRaidMonochrome.PressAsync(null, raid, map, element, GuildRaidMonochrome.FlowerSkill));
                Assert.False(GuildRaidMonochrome.Usable(raid, map, element));
                Assert.Contains(GuildRaidStagings.IdOf(map, GuildRaidMonochrome.FlowerGreen), GuildRaidStagings.Standing(raid, map));
                Assert.Contains(GuildRaidStagings.IdOf(Obra.Centre, $"#SD Logo{pedestal + 1}_spawn"), GuildRaidStagings.Standing(raid, Obra.Centre));
            }

            Assert.True(GuildRaidMonochrome.Solved(raid));
            Assert.Equal(2000, raid.Score);
            Assert.Equal(Gardens.Health - 1, raid.Health);
            foreach (long map in Obra.Maps.Where(m => GuildRaidStagings.IdOf(m, GuildRaidMonochrome.Colouring) != 0))
            {
                Assert.Contains(GuildRaidStagings.IdOf(map, "Couleur_bleu"), GuildRaidStagings.Standing(raid, map));
                Assert.Contains(GuildRaidStagings.IdOf(map, GuildRaidMonochrome.Colouring), GuildRaidStagings.Standing(raid, map));
            }
            GuildRaidStagings.Forget(raid);
        }

        /// <summary>
        /// A flower out of order costs a life, takes the green ones and the centre's steps away and
        /// starts over; a map without the raid's flower offers nothing.
        /// </summary>
        [Fact]
        public async Task A_flower_out_of_order_costs_a_life_and_starts_over()
        {
            if (Obra == null) return;
            var raid = Raid(303);
            GuildRaidMonochrome.Set(raid, new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, new[] { 0, 3, 4, 7 });

            var (firstMap, firstFlower) = FlowerOf(raid, 0);
            await GuildRaidMonochrome.PressAsync(null, raid, firstMap, firstFlower, GuildRaidMonochrome.FlowerSkill);
            Assert.Equal(1, raid.Get(GuildRaidMonochrome.ProgressVariable));

            var (thirdMap, thirdFlower) = FlowerOf(raid, 2);
            await GuildRaidMonochrome.PressAsync(null, raid, thirdMap, thirdFlower, GuildRaidMonochrome.FlowerSkill);
            Assert.Equal(0, raid.Get(GuildRaidMonochrome.ProgressVariable));
            Assert.Equal(Gardens.Health - 1, raid.Health);
            Assert.True(GuildRaidMonochrome.Usable(raid, firstMap, firstFlower));
            Assert.DoesNotContain(GuildRaidStagings.IdOf(firstMap, GuildRaidMonochrome.FlowerGreen), GuildRaidStagings.Standing(raid, firstMap));
            Assert.DoesNotContain(GuildRaidStagings.IdOf(Obra.Centre, "#SD Logo1_spawn"), GuildRaidStagings.Standing(raid, Obra.Centre));
            Assert.False(GuildRaidMonochrome.Solved(raid));

            // The second pedestal's other object: its map shows no flower and offers none.
            string other = Obra.Objects[raid.SequenceOf(GuildRaidMonochrome.PedestalsSequence)[2]];
            var (otherMap, otherFlower) = Obra.Flowers[other];
            Assert.False(GuildRaidMonochrome.Offers(raid, otherMap, otherFlower.Id));
            Assert.False(await GuildRaidMonochrome.PressAsync(null, raid, otherMap, otherFlower.Id, GuildRaidMonochrome.FlowerSkill));
            GuildRaidStagings.Forget(raid);
        }

        /// <summary>hpm carries the progression, the intensity and the sequence: { f1, f2, f3 }.</summary>
        [Fact]
        public void A_staging_is_played_with_hpm()
        {
            var frame = Jondo.Unity.Server.Network.ProtoMessage.Parse(GuildRaidStagings.BuildPlay(1702, GuildRaidStagings.AtEnd));
            Assert.Equal(100L, frame.Fields.First(f => f.FieldNumber == 1).VarIntValue);
            Assert.Equal(100L, frame.Fields.First(f => f.FieldNumber == 2).VarIntValue);
            Assert.Equal(1702L, frame.Fields.First(f => f.FieldNumber == 3).VarIntValue);

            var fromStart = Jondo.Unity.Server.Network.ProtoMessage.Parse(GuildRaidStagings.BuildPlay(9999, GuildRaidStagings.FromStart));
            Assert.DoesNotContain(fromStart.Fields, f => f.FieldNumber == 1);
        }
    }
}

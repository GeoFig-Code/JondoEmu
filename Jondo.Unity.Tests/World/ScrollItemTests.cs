using System;
using System.Linq;
using System.Threading.Tasks;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Xunit;
using Wire = Jondo.Unity.Tests.Combat.PortalTests.Wire;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// The characteristic scrolls and the breads of the kind: what counts as one, how much they
    /// give, and the bounds their criteria keep.
    /// </summary>
    public class ScrollItemTests
    {
        private static DatabaseManager.ItemEffectData Effect(int id, int dice = 0, int side = 0)
            => new() { EffectId = id, DiceNum = dice, DiceSide = side };

        private static bool IsScroll(params DatabaseManager.ItemEffectData[] effects)
            => ScrollItemHandler.IsScroll(effects.ToList());

        /// <summary>
        /// A scroll, a bread that heals and raises agility, a potion that gives energy and
        /// intelligence; not a bread that only heals, nor an item that raises and gives an item.
        /// </summary>
        [Fact]
        public void Scrolls_and_the_breads_that_raise_for_good()
        {
            Assert.True(IsScroll(Effect(607, 1)));
            Assert.True(IsScroll(Effect(110, 20), Effect(609, 1)));
            Assert.True(IsScroll(Effect(611, 1), Effect(139, 150)));
            Assert.True(IsScroll(Effect(609, 100, 200), Effect(611, 100, 200), Effect(608, 100, 200),
                                 Effect(607, 100, 200), Effect(606, 100, 200), Effect(610, 100, 200)));

            Assert.False(IsScroll(Effect(110, 30)));
            Assert.False(IsScroll(Effect(607, 1), Effect(193, 0, 1)));
        }

        /// <summary>The bag's own figure first; else the template's figure, or a roll between its two.</summary>
        [Fact]
        public void The_amount_is_the_items_own_or_the_templates()
        {
            var dice = new Random(3);
            var rolled = new Equipment.Item { Uid = 1, Template = 797 };
            rolled.Effects.Add(new Equipment.ItemEffect(607, 40, 2, 102));
            Assert.Equal(40, ScrollItemHandler.AmountOf(rolled, Effect(607, 2, 102), dice));

            var plain = new Equipment.Item { Uid = 2, Template = 683 };
            Assert.Equal(1, ScrollItemHandler.AmountOf(plain, Effect(607, 1), dice));
            int roll = ScrollItemHandler.AmountOf(plain, Effect(607, 2, 102), dice);
            Assert.InRange(roll, 2, 102);
        }

        /// <summary>Nobody goes past 100, and nothing goes down.</summary>
        [Fact]
        public void Never_past_the_hundred()
        {
            Assert.Equal(21, ScrollItemHandler.Raised(20, 1));
            Assert.Equal(100, ScrollItemHandler.Raised(99, 5));
            Assert.Equal(100, ScrollItemHandler.Raised(100, 1));
            Assert.Equal(100, ScrollItemHandler.Raised(30, 200));
            Assert.Equal(30, ScrollItemHandler.Raised(30, -4));
        }

        /// <summary>«Tus características no te lo permiten.», byte for byte the refused agility scroll.</summary>
        [Fact]
        public void The_refusal_is_the_captures()
        {
            Assert.Equal("08011013", Convert.ToHexString(ConnectionProtocol.BuildInfoMessage(
                InfoMessages.Warning, InfoMessages.CharacteristicsForbid)).ToLowerInvariant());
        }

        /// <summary>
        /// Past 25 points of agility from scrolls the small one is refused -- «Tus características no
        /// te lo permiten.» -- and the medium, large and powerful ones are not; under 25 the small
        /// one goes. An achievement missing is still «Logro necesario», a level «No cumples los
        /// requisitos».
        /// </summary>
        [Fact]
        public async Task The_small_scroll_stops_at_25()
        {
            await using var wire = await Wire.Open(7501);
            using (SessionContext.Push(wire.Session))
            {
                var state = wire.Session.State;
                state.CharacterLevel = 60;

                state.ScrolledAgility = 30;
                Assert.False(ItemCriteria.Holds("ca<25"));
                Assert.Equal(InfoMessages.CharacteristicsForbid, ItemCriteria.RefusalOf("ca<25"));
                Assert.True(ItemCriteria.Holds("ca<50"));
                Assert.True(ItemCriteria.Holds("ca<80"));
                Assert.True(ItemCriteria.Holds("ca<100"));

                state.ScrolledAgility = 10;
                Assert.True(ItemCriteria.Holds("ca<25"));
                Assert.True(ItemCriteria.Holds("cs<25") == (state.ScrolledStrength < 25));

                state.ScrolledVitality = 100;
                Assert.False(ItemCriteria.Holds("cv<100"));

                Assert.Equal(InfoMessages.AchievementNeeded, ItemCriteria.RefusalOf("OA=1088&PL>49"));
                Assert.Equal(InfoMessages.RequirementsNotMet, ItemCriteria.RefusalOf("PL>99"));
            }
        }
    }
}

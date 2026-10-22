using System;
using System.Linq;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// Gargandias's temple: the weekend he may be faced, the season's offering and its cost, and
    /// what the altar opens on.
    /// </summary>
    public class GargandiasTests
    {
        /// <summary>A moment on Paris's clock in October 2026 (summer time, UTC+2, until the 25th).</summary>
        private static DateTimeOffset Paris(int day, int hour, int minute = 0)
            => new DateTimeOffset(2026, 10, day, hour, minute, 0, TimeSpan.FromHours(2));

        [Fact]
        public void The_titan_is_there_from_friday_evening_to_monday_morning()
        {
            // 16 October 2026 is a Friday, 19 a Monday.
            Assert.False(Gargandias.IsOpen(Paris(16, 18, 59)));
            Assert.True(Gargandias.IsOpen(Paris(16, 19, 0)));
            Assert.True(Gargandias.IsOpen(Paris(18, 23, 0)));
            Assert.True(Gargandias.IsOpen(Paris(19, 7, 59)));
            Assert.False(Gargandias.IsOpen(Paris(19, 8, 0)));
            Assert.False(Gargandias.IsOpen(Paris(14, 12, 0)));
        }

        [Fact]
        public void The_offering_costs_one_two_then_three()
        {
            Assert.Equal(new[] { 1, 2, 3, 3, 3 }, new[] { 0, 1, 2, 3, 4 }.Select(Gargandias.CostOf));
        }

        [Fact]
        public void Each_season_asks_for_its_family_s_drop()
        {
            Assert.Equal(31666, Gargandias.OfferingOf(OsavoraSeasons.Season.Naissance));   // raribóvoro's skin
            Assert.Equal(31623, Gargandias.OfferingOf(OsavoraSeasons.Season.Chasse));      // rhoarim's comb
            Assert.Equal(31523, Gargandias.OfferingOf(OsavoraSeasons.Season.Repos));       // kabombz's hairball
        }

        [Fact]
        public void The_altar_opens_on_the_titan_s_energy_only_on_the_weekend()
        {
            var clock = GargandiasHandler.Clock;
            try
            {
                GargandiasHandler.Clock = () => Paris(17, 12);
                Assert.Equal(GargandiasHandler.Open, GargandiasHandler.Opening(Gargandias.Altar).Question);
                GargandiasHandler.Clock = () => Paris(14, 12);
                Assert.Equal(GargandiasHandler.Closed, GargandiasHandler.Opening(Gargandias.Altar).Question);
                Assert.Equal(GargandiasHandler.Roar, GargandiasHandler.Opening(Gargandias.TitanNpc).Question);
            }
            finally
            {
                GargandiasHandler.Clock = clock;
            }
        }

        [Fact]
        public void The_altar_wears_the_season_s_look()
        {
            // A Repos week (19-26 October 2026, the 65th since the first Naissance): «$4;0;0;WE=230|WE=255»
            // wins, the Naissance's does not.
            var resolve = WorldEvents.Answering(_ => Jondo.Unity.World.Content.Answer.Unknown, Paris(21, 12));
            Assert.True(Jondo.Unity.World.Content.Criterion.Met("WE=230|WE=255", resolve));
            Assert.False(Jondo.Unity.World.Content.Criterion.Met("WE=228|WE=252", resolve));
        }

        [Fact]
        public void The_titan_is_a_world_event_on_the_weekend()
        {
            Assert.Contains(Gargandias.WorldEvent, WorldEvents.Active(Paris(17, 12)));
            Assert.DoesNotContain(Gargandias.WorldEvent, WorldEvents.Active(Paris(14, 12)));
        }
    }
}

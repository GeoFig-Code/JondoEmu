using System;
using Jondo.Unity.Server.Managers;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>Osavora's seasons: «une saison par semaine», «tous les lundis à 8 heure», Naissance, Chasse, Repos.</summary>
    public class OsavoraSeasonTests
    {
        private static readonly TimeSpan Paris = TimeSpan.FromHours(2);

        [Fact]
        public void The_seasons_turn_on_mondays_at_eight()
        {
            var first = new DateTimeOffset(OsavoraSeasons.Anchor, Paris);
            Assert.Equal(DayOfWeek.Monday, first.DayOfWeek);
            Assert.Equal(OsavoraSeasons.Season.Naissance, OsavoraSeasons.At(first));
            Assert.Equal(OsavoraSeasons.Season.Naissance, OsavoraSeasons.At(first.AddDays(7).AddMinutes(-1)));
            Assert.Equal(OsavoraSeasons.Season.Chasse, OsavoraSeasons.At(first.AddDays(7)));
            Assert.Equal(OsavoraSeasons.Season.Repos, OsavoraSeasons.At(first.AddDays(14)));
            Assert.Equal(OsavoraSeasons.Season.Naissance, OsavoraSeasons.At(first.AddDays(21)));
            Assert.Equal(OsavoraSeasons.Season.Repos, OsavoraSeasons.At(first.AddDays(-1)));

            // Across the clocks going back (25 October 2026): still Monday 8:00 in Paris, now +01:00.
            var thursday = new DateTimeOffset(2026, 10, 22, 15, 0, 0, Paris);
            var next = OsavoraSeasons.NextChange(thursday);
            Assert.Equal(new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.FromHours(1)), next);
            Assert.NotEqual(OsavoraSeasons.At(thursday), OsavoraSeasons.At(next));
            Assert.Equal(OsavoraSeasons.At(thursday), OsavoraSeasons.At(next.AddMinutes(-1)));
        }

        /// <summary>The entry map's sequences as the client's map has them: 1 Naissance, 2 Chasse, 3 Repos.</summary>
        [Fact]
        public void The_maps_play_their_season()
        {
            if (!OsavoraSeasons.IsSeasonal(234751496)) return;   // no client data
            Assert.Equal(1, OsavoraSeasons.StagingOn(234751496, OsavoraSeasons.Season.Naissance));
            Assert.Equal(2, OsavoraSeasons.StagingOn(234751496, OsavoraSeasons.Season.Chasse));
            Assert.Equal(3, OsavoraSeasons.StagingOn(234751496, OsavoraSeasons.Season.Repos));
            Assert.Equal(0, OsavoraSeasons.StagingOn(TravelersTower.PortalRoom, OsavoraSeasons.Season.Repos));
            Assert.False(OsavoraSeasons.IsSeasonal(TravelersTower.PortalRoom));
        }
    }
}

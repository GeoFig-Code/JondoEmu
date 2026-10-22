using System;
using System.IO;
using System.Linq;
using Jondo.Unity.Launcher;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.World.Almanax;
using Xunit;
using Season = Jondo.Unity.Server.Managers.AlmanaxTemple.Season;

namespace Jondo.Unity.Tests.Quests
{
    /// <summary>
    /// The Almanax's temple: today's season, its hall and its room, and today's saint alone in it.
    /// INFERRED: no capture enters the temple; the seasons are the offerings' prayers', the rooms'
    /// months those of the 80 saints the client places.
    /// </summary>
    [Collection(AlmanaxTests.AlmanaxClock)]
    public class AlmanaxTempleTests
    {
        private static DateTime On(int month, int day) => new DateTime(2026, month, day, 10, 0, 0);

        [Theory]
        [InlineData(3, 19, Season.Winter)]
        [InlineData(3, 20, Season.Spring)]
        [InlineData(6, 20, Season.Spring)]
        [InlineData(6, 21, Season.Summer)]
        [InlineData(9, 21, Season.Summer)]
        [InlineData(9, 22, Season.Autumn)]
        [InlineData(12, 20, Season.Autumn)]
        [InlineData(12, 21, Season.Winter)]
        [InlineData(1, 1, Season.Winter)]
        public void The_season_changes_where_the_prayers_change(int month, int day, Season season)
        {
            // «Reza ante el altar de verano» up to 21 September, «de otoño» from the 22nd.
            Assert.Equal(season, AlmanaxTemple.SeasonOf(On(month, day)));
        }

        [Theory]
        // The summer's three rooms, where the client places 80 saints: a month each, from the 21st.
        [InlineData(6, 21, 101453313)]
        [InlineData(7, 1, 101453313)]
        [InlineData(7, 20, 101453313)]
        [InlineData(7, 21, 101453315)]
        [InlineData(8, 20, 101453315)]
        [InlineData(8, 21, 101453317)]
        [InlineData(9, 17, 101453317)]
        [InlineData(9, 21, 101453317)]
        // The other seasons' rooms, numbered alike.
        [InlineData(9, 22, 101450241)]
        [InlineData(10, 22, 101450243)]
        [InlineData(12, 20, 101450245)]
        [InlineData(12, 21, 101451265)]
        [InlineData(1, 21, 101451267)]
        [InlineData(3, 19, 101451269)]
        [InlineData(3, 20, 101452289)]
        [InlineData(5, 21, 101452293)]
        public void Each_day_has_its_room(int month, int day, long room)
        {
            Assert.Equal(room, AlmanaxTemple.RoomOf(On(month, day)));
        }

        [Fact]
        public void The_ways_lead_to_today()
        {
            var now = On(10, 22);   // autumn, its second month

            // The sanctuary's door: today's hall, by its way out.
            Assert.Equal((101452297L, AlmanaxTemple.HallArrival),
                         AlmanaxTemple.Destination(AlmanaxTemple.Sanctuary, AlmanaxTemple.SanctuaryDoorCell, now));

            // In any hall, autumn's door leads to today's room and the others to their rooms asleep.
            foreach (var season in new[] { Season.Spring, Season.Summer, Season.Autumn, Season.Winter })
            {
                long hall = AlmanaxTemple.HallOf(season);
                Assert.Equal((101450243L, AlmanaxTemple.RoomArrival), AlmanaxTemple.Destination(hall, 346, now));
                Assert.Equal((101453319L, AlmanaxTemple.RoomArrival), AlmanaxTemple.Destination(hall, 273, now));
                Assert.Equal((101452295L, AlmanaxTemple.RoomArrival), AlmanaxTemple.Destination(hall, 244, now));
                Assert.Equal((101451271L, AlmanaxTemple.RoomArrival), AlmanaxTemple.Destination(hall, 375, now));
                Assert.Equal((AlmanaxTemple.Sanctuary, AlmanaxTemple.SanctuaryDoorCell),
                             AlmanaxTemple.Destination(hall, AlmanaxTemple.HallExitCell, now));
                Assert.Null(AlmanaxTemple.Destination(hall, 300, now));
            }

            // A room's ways out: today's hall, by the door of the room's season.
            Assert.Equal((101452297L, 346), AlmanaxTemple.Destination(101450243, 437, now));
            Assert.Equal((101452297L, 273), AlmanaxTemple.Destination(101453319, 452, now));
            Assert.Null(AlmanaxTemple.Destination(101450243, 384, now));
            Assert.Null(AlmanaxTemple.Destination(AlmanaxTemple.Sanctuary, 451, now));
        }

        [Fact]
        public void Only_today_s_saint_stands_in_today_s_room()
        {
            if (!File.Exists(Paths.AlmanaxJson)) return;

            var clock = Almanax.Clock;
            try
            {
                Almanax.Load();
                Npcs.Initialize();
                var calendar = new AlmanaxCalendar();

                // 5 July: the summer's first room, and nobody else's saint in the three.
                Almanax.Clock = () => On(7, 5);
                int saint = calendar.DayOf(On(7, 5))!.Saint;
                var here = Npcs.Of(101453313);
                Assert.Contains(here, s => s.NpcId == saint && s.Cell == AlmanaxTemple.SaintCell);
                foreach (long room in new long[] { 101453313, 101453315, 101453317 })
                    Assert.DoesNotContain(Npcs.Of(room), s => Almanax.IsSaint(s.NpcId) && s.NpcId != saint);

                // 25 July: the second room's, and the first's is gone.
                Almanax.Clock = () => On(7, 25);
                int next = calendar.DayOf(On(7, 25))!.Saint;
                Assert.Contains(Npcs.Of(101453315), s => s.NpcId == next);
                Assert.DoesNotContain(Npcs.Of(101453313), s => s.NpcId == saint);
            }
            finally
            {
                AlmanaxTemple.Reset();
                Almanax.Clock = clock;
                Almanax.Reset();
            }
        }
    }
}

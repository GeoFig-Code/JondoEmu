using System;
using System.Collections.Generic;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Managers;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// One of each archmonster per zone, until something kills it: a beaten group lets its
    /// archmonster go, and once its respawn time is over a group that comes out in the zone can
    /// bring it back.
    /// </summary>
    [Collection("MapManager")]
    public class ArchmonsterTests
    {
        /// <summary>Monster 47 and its rare version 2309, which belongs to subarea 169.</summary>
        private const int Ordinary = 47;
        private const int Rare = 2309;
        private const int SubArea = 169;

        [Fact]
        public void A_beaten_archmonster_can_stand_again_and_only_once()
        {
            Load();
            try
            {
                // Standing nowhere, it may come back in its ordinary version's place.
                var ids = new List<int> { Ordinary };
                Archimonsters.MayComeBack(SubArea, ids);
                Assert.Equal(new[] { Rare }, ids);

                long first = Stand(mapId: 1);
                Assert.NotEqual(0, first);

                // While it stands, no other group anywhere keeps it, and none brings it back.
                for (long group = 1; group <= 200; group++)
                {
                    var other = new List<int> { Rare };
                    Assert.Equal(0, Archimonsters.Thin(2, group, other));
                    Assert.Equal(new[] { Ordinary }, other);
                }
                var again = new List<int> { Ordinary };
                Archimonsters.MayComeBack(SubArea, again);
                Assert.Equal(new[] { Ordinary }, again);

                // Beaten on another map, it is not let go; beaten on its own, it is, once its
                // respawn time is over.
                Archimonsters.Release(2, new[] { Rare });
                Assert.Equal(0, Archimonsters.Thin(3, first, new List<int> { Rare }));
                Archimonsters.Release(1, new[] { Ordinary, Rare });
                Assert.Equal(0, Stand(mapId: 3));
                RareMonsterPool.Clock = () => DateTime.UtcNow.AddHours(3);
                Assert.NotEqual(0, Stand(mapId: 3));
            }
            finally
            {
                Unload();
            }
        }

        [Fact]
        public void A_beaten_archmonster_is_back_after_two_hours_give_or_take_fifteen_minutes()
        {
            Load();
            var now = DateTime.UtcNow;
            try
            {
                RareMonsterPool.Clock = () => now;
                Assert.Equal(Rare, Archimonsters.Thin(1, 1, new List<int> { Rare }, draw: false));
                Archimonsters.Release(1, new[] { Rare });

                RareMonsterPool.Clock = () => now.AddMinutes(104);
                Assert.Equal(0, Archimonsters.Thin(2, 2, new List<int> { Rare }, draw: false));

                RareMonsterPool.Clock = () => now.AddMinutes(136);
                Assert.Equal(Rare, Archimonsters.Thin(2, 2, new List<int> { Rare }, draw: false));
            }
            finally
            {
                Unload();
            }
        }

        /// <summary>The first group that gets to keep it on that map: one in ten does.</summary>
        private static long Stand(long mapId)
        {
            for (long group = 1; group <= 1000; group++)
            {
                if (Archimonsters.Thin(mapId, group, new List<int> { Rare }) == Rare) return group;
            }
            return 0;
        }

        private static void Load()
        {
            using var connection = new SqliteConnection(DatabaseManager.WorldConnectionString);
            connection.Open();
            Archimonsters.Initialize(connection);
        }

        /// <summary>Nobody else's groups are thinned by what was placed here.</summary>
        private static void Unload()
        {
            RareMonsterPool.Clock = () => DateTime.UtcNow;
            using var empty = new SqliteConnection("Data Source=:memory:");
            empty.Open();
            using var create = empty.CreateCommand();
            create.CommandText = "CREATE TABLE MonsterTemplates (Id INTEGER, Data TEXT);";
            create.ExecuteNonQuery();
            Archimonsters.Initialize(empty);
        }

        /// <summary>
        /// A day the Almanax triples the speed («aumenta un 200%»): three groups in ten keep one, and
        /// a beaten one is back in a third of the time.
        /// </summary>
        [Fact]
        public void The_almanax_s_speed_triples_the_odds_and_shortens_the_wait()
        {
            var now = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
            var normal = new RareMonsterPool(TimeSpan.FromHours(2));
            var tripled = new RareMonsterPool(TimeSpan.FromHours(2), _ => 300);
            int keptNormal = 0, keptTripled = 0;
            for (long group = 1; group <= 3000; group++)
            {
                if (normal.MayStand(group, group, Rare, draw: true)) keptNormal++;
                if (tripled.MayStand(group, group, Rare, draw: true)) keptTripled++;
            }
            Assert.InRange(keptNormal, 240, 360);
            Assert.InRange(keptTripled, 780, 1020);

            try
            {
                RareMonsterPool.Clock = () => now;
                tripled.Reserve(1, Rare);
                tripled.Release(1, new[] { Rare });
                RareMonsterPool.Clock = () => now.AddMinutes(46);   // (2 h + 15 min) / 3 = 45 min
                Assert.True(tripled.IsUp(Rare));
            }
            finally
            {
                RareMonsterPool.Clock = () => DateTime.UtcNow;
            }
        }
    }
}

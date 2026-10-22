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
    /// archmonster go, and a group that comes out in the zone can bring it back.
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
            using (var connection = new SqliteConnection(DatabaseManager.WorldConnectionString))
            {
                connection.Open();
                Archimonsters.Initialize(connection);
            }
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

                // Beaten on another map, it is not let go; beaten on its own, it is, and stays away
                // 2 h ± 15 min before it stands anywhere again.
                Archimonsters.Release(2, new[] { Rare });
                Assert.Equal(0, Archimonsters.Thin(3, first, new List<int> { Rare }));
                Archimonsters.Release(1, new[] { Ordinary, Rare });
                Assert.True(Archimonsters.IsAway(Rare));
                Assert.Equal(0, Stand(mapId: 3));
                var away = new List<int> { Ordinary };
                Archimonsters.MayComeBack(SubArea, away);
                Assert.Equal(new[] { Ordinary }, away);

                // Beaten three hours ago, it is back.
                Assert.Equal(0, Stand(mapId: 4));
                using (var connection = new SqliteConnection(DatabaseManager.WorldConnectionString))
                {
                    connection.Open();
                    Archimonsters.Initialize(connection);
                }
                Assert.NotEqual(0, Stand(mapId: 1));
                long threeHoursAgo = DateTimeOffset.UtcNow.AddHours(-3).ToUnixTimeMilliseconds();
                Archimonsters.Release(1, new[] { Rare }, threeHoursAgo);
                Assert.False(Archimonsters.IsAway(Rare));
                Assert.NotEqual(0, Stand(mapId: 3));
            }
            finally
            {
                // Nobody else's groups are thinned by what was placed here.
                using var empty = new SqliteConnection("Data Source=:memory:");
                empty.Open();
                using var create = empty.CreateCommand();
                create.CommandText = "CREATE TABLE MonsterTemplates (Id INTEGER, Data TEXT);";
                create.ExecuteNonQuery();
                Archimonsters.Initialize(empty);
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
    }
}

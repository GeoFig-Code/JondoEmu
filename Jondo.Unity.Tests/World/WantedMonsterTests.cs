using System;
using System.Collections.Generic;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Managers;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// One of each wanted monster in the world, taken out of the groups that do not keep it, and
    /// back in its zone 3 h ± 15 min after something kills it.
    /// </summary>
    [Collection("MapManager")]
    public class WantedMonsterTests
    {
        /// <summary>Akornaddikt the Squirrel, wanted in Astrub (subarea 95). It came in every group of 4,-18.</summary>
        private const int Akornaddikt = 463;
        private const int Astrub = 95;

        /// <summary>An ordinary monster of Astrub.</summary>
        private const int Ordinary = 236;

        [Fact]
        public void A_wanted_monster_stands_once_and_is_taken_out_elsewhere()
        {
            Load();
            try
            {
                int kept = 0;
                for (long group = 1; group <= 1000; group++)
                {
                    var ids = new List<int> { Ordinary, Akornaddikt };
                    if (WantedMonsters.Thin(group, group, ids) == Akornaddikt) kept++;
                    else Assert.Equal(new[] { Ordinary, 0 }, ids);
                }
                Assert.Equal(1, kept);
            }
            finally
            {
                Unload();
            }
        }

        [Fact]
        public void A_beaten_wanted_monster_joins_a_group_of_its_zone_after_three_hours()
        {
            Load();
            var now = DateTime.UtcNow;
            try
            {
                RareMonsterPool.Clock = () => now;
                Assert.Equal(Akornaddikt, WantedMonsters.Thin(1, 1, new List<int> { Akornaddikt }, draw: false));
                WantedMonsters.Release(1, new[] { Akornaddikt });

                // 3 h - 15 min: the groups that come out in Astrub bring its other wanted
                // monsters, never Akornaddikt.
                RareMonsterPool.Clock = () => now.AddMinutes(164);
                for (int n = 0; n < 50; n++)
                {
                    var early = new List<int> { Ordinary };
                    WantedMonsters.MayComeBack(Astrub, early);
                    WantedMonsters.Thin(100 + n, 100 + n, early, draw: false);
                    Assert.DoesNotContain(Akornaddikt, early);
                }

                // 3 h + 15 min: it joins the next group that comes out in Astrub.
                RareMonsterPool.Clock = () => now.AddMinutes(196);
                var group = new List<int> { Ordinary };
                WantedMonsters.MayComeBack(Astrub, group);
                Assert.Equal(new[] { Ordinary, Akornaddikt }, group);
                Assert.Equal(Akornaddikt, WantedMonsters.Thin(2, 2, group, draw: false));
            }
            finally
            {
                Unload();
            }
        }

        private static void Load()
        {
            using var connection = new SqliteConnection(DatabaseManager.WorldConnectionString);
            connection.Open();
            WantedMonsters.Initialize(connection);
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
            WantedMonsters.Initialize(empty);
        }
    }
}

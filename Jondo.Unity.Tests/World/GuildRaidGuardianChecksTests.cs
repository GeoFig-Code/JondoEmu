using System;
using System.Collections.Generic;
using System.Linq;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.World.Content;
using Jondo.Unity.World.Maps;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// What the guardians' checks go by, from the enigmas: the Vigilante's "mob" is the Enclave's
    /// statue, the Guardián's glyphs stand beside its arena's objects and the right ones are the
    /// objects whose maps had the Obra's flowers. In the MapManager collection, as every test that
    /// loads the interactives.
    /// </summary>
    [Collection("MapManager")]
    public class GuildRaidGuardianChecksTests
    {
        public GuildRaidGuardianChecksTests() => Interactives.Initialize();

        private static RaidKind Gardens => Raids.Of(Raids.EternalGardens);

        private static bool HasWorld => GuildRaidMonochrome.WorkOf(Gardens) != null;

        private static RaidInstance Raid(long id)
        {
            var raid = new RaidInstance(id, Raids.EternalGardens, 1, 7001, DateTimeOffset.UtcNow, TimeSpan.FromHours(2));
            raid.SetHealth(Gardens.Health);
            return raid;
        }

        /// <summary>"o_hint_N &lt; 5" and "o_hint_N &gt; 4", by N: 32067 and 32068 for the first, then 32076.. and 32083...</summary>
        private static int Right(int n) => n == 1 ? 32067 : 32074 + n;
        private static int Wrong(int n) => n == 1 ? 32068 : 32081 + n;

        /// <summary>
        /// The Guardián fights on the Enclave's centre's twin with the eight objects drawn on it,
        /// 238297090, not its bare one; any other group keeps the usual arena.
        /// </summary>
        [Fact]
        public void The_guardian_fights_where_the_objects_are()
        {
            if (!HasWorld) return;
            MobSpawnManager.EnsureMonsterData();
            var guardian = new MobSpawnManager.MobGroup { Members = GuildRaidGuardians.GroupOf(GuildRaidGuardians.Guardian)
                .Select(m => new MobSpawnManager.MobMember { Monster = new MobSpawnManager.MonsterData { Id = m } }).ToList() };
            var vigilante = new MobSpawnManager.MobGroup { Members = GuildRaidGuardians.GroupOf(GuildRaidGuardians.Vigilante)
                .Select(m => new MobSpawnManager.MobMember { Monster = new MobSpawnManager.MonsterData { Id = m } }).ToList() };

            Assert.Equal(238297090L, GuildRaidGuardians.ArenaOf(guardian, 238297345));
            Assert.Equal(0L, GuildRaidGuardians.ArenaOf(vigilante, 238294274));
            Assert.Equal(GuildRaidMonochrome.WorkOf(Gardens).Objects.OrderBy(o => o, StringComparer.Ordinal),
                         GuildRaidStagings.ObjectsOf(238297090).Select(o => o.Name).OrderBy(o => o, StringComparer.Ordinal));
        }

        /// <summary>A glyph beside each object: eight free walkable cells, each next to its own object.</summary>
        [Fact]
        public void A_glyph_goes_beside_each_object()
        {
            if (!HasWorld) return;
            if (!MapManager.WalkableCells.ContainsKey(238297090)) MapManager.Initialize();
            var objects = GuildRaidStagings.ObjectsOf(238297090).OrderBy(o => o.Name, StringComparer.Ordinal).ToList();
            var walkable = MapManager.GetFightWalkable(238297090);
            var cells = GuildRaidGuardians.BesideCells(walkable, new HashSet<int>(), objects.Select(o => o.Cell).ToList());

            Assert.Equal(8, cells.Distinct().Count());
            for (int i = 0; i < cells.Count; i++)
            {
                Assert.Contains(cells[i], walkable);
                Assert.True(MapGeometry.Distance(cells[i], objects[i].Cell) <= 2, $"{objects[i].Name}: {cells[i]} far from {objects[i].Cell}");
            }
        }

        /// <summary>
        /// The glyphs' checks follow the flowers: beside an object whose map had one, "&lt; 5" holds and
        /// "&gt; 4" does not; beside the others, the other way round. Without the glyphs' objects,
        /// nothing to go by.
        /// </summary>
        [Fact]
        public void The_right_glyphs_are_the_flowers_objects()
        {
            if (!HasWorld) return;
            var raid = Raid(801);
            var work = GuildRaidMonochrome.WorkOf(Gardens);
            // Pedestals in the objects' order; the flowers on the first object of each.
            GuildRaidMonochrome.Set(raid, Enumerable.Range(0, 8).ToList(), new[] { 0, 2, 4, 6 });
            var flowers = GuildRaidMonochrome.FlowerObjects(raid);
            Assert.Equal(new[] { 0, 2, 4, 6 }.Select(i => work.Objects[i]), flowers);

            const long fight = 9801;
            Assert.Null(GuildRaidGuardians.Holds(raid, fight, Right(1)));
            var beside = work.Objects.OrderBy(o => o, StringComparer.Ordinal).ToList();
            GuildRaidGuardians.SetHints(fight, beside);
            try
            {
                for (int n = 1; n <= 8; n++)
                {
                    bool right = flowers.Contains(beside[n - 1]);
                    Assert.Equal(right, GuildRaidGuardians.Holds(raid, fight, Right(n)));
                    Assert.Equal(!right, GuildRaidGuardians.Holds(raid, fight, Wrong(n)));
                }
            }
            finally
            {
                GuildRaidGuardians.Forget(fight);
                GuildRaidStagings.Forget(raid);
            }
        }

        /// <summary>The Vigilante's "mob" is the Enclave's statue: "mob == 2" holds with a Trilipán statue, "mob != 1" too.</summary>
        [Fact]
        public void The_vigilante_s_mob_is_the_enclave_s_statue()
        {
            var raid = Raid(802);
            Assert.Null(GuildRaidGuardians.Holds(raid, 0, 32101));        // no statue yet
            raid.Set(GuildRaidEnclave.StatueVariable, 2);
            Assert.True(GuildRaidGuardians.Holds(raid, 0, 32101));        // mob == 2
            Assert.False(GuildRaidGuardians.Holds(raid, 0, 32100));       // mob == 1
            Assert.False(GuildRaidGuardians.Holds(raid, 0, 32105));       // mob != 2
            Assert.True(GuildRaidGuardians.Holds(raid, 0, 32104));        // mob != 1
            Assert.Null(GuildRaidGuardians.Holds(raid, 0, 32043));        // a colour check
        }
    }
}

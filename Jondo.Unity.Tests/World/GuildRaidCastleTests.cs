using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.World.Content;
using Jondo.Unity.World.Fights;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// The Santuario's castle as the client's maps have it: its corridor from the hub to the tower,
    /// the ways between its rooms with the names the client keeps for them, what keeps the corridor
    /// and the tower closed, their monsters, their arenas and the manuscript's pages. In the
    /// MapManager collection, as every test that loads the interactives.
    /// </summary>
    [Collection("MapManager")]
    public class GuildRaidCastleTests
    {
        public GuildRaidCastleTests() => Interactives.Initialize();

        private static RaidKind Gardens => Raids.Of(Raids.EternalGardens);

        private static bool HasWorld => DatabaseManager.MapsOfSubArea(Gardens.Floors[^1]).Count > 0
                                        && Interactives.ElementsOf(238296325).Count > 0;

        /// <summary>
        /// The castle walked from the hub: ten rooms, [16,16] to [17,10] (the guides' "position à
        /// copier" before the tower's door), then the tower's foot [16,9], its dungeons and its top.
        /// </summary>
        [Fact]
        public void The_corridor_is_the_ten_rooms_from_the_hub_to_the_tower()
        {
            if (!HasWorld) return;
            var castle = GuildRaidSanctuary.CastleOf(Gardens);
            Assert.Equal(238296325L, castle.Hub);
            Assert.Equal(new[] { 238296327L, 238296328L, 238295816L, 238295817L, 238295818L,
                                 238296331L, 238296842L, 238297355L, 238297357L, 238296846L }, castle.Corridor);
            Assert.Equal((238295822L, 238295316L, 238297364L), (castle.Foot, castle.Dungeon, castle.Top));
            Assert.Null(GuildRaidSanctuary.CastleOf(Raids.Of(Raids.Gigalodon)));
            Assert.Equal(6, GuildRaidSanctuary.MonstersPerRoom(Gardens));
        }

        /// <summary>
        /// Its ways carry the client's own names: the hub's door "Pasar al pasillo" into the first
        /// room, the [17,10] door "Pasar a la torre de la Princesa y de la Reina", the stairs down
        /// "Dirigirse a los calabozos" and up "Dirigirse a las alturas de la torre", the portals
        /// "Hacia la obra monocromática"... and back "Hacia la entrada del santuario".
        /// </summary>
        [Fact]
        public void The_castle_s_ways_carry_their_names()
        {
            if (!HasWorld) return;
            var routes = GuildRaidPassages.Derive();
            InteractiveTeleport From(long map, int element) => routes.Single(r => r.SourceMapId == map && r.ElementId == element);

            var hubDoor = From(238296325, 540971);
            Assert.Equal((238296327L, 386), (hubDoor.DestinationMapId, hubDoor.SkillNameId));
            Assert.Equal(238296325L, From(238296327, 541032).DestinationMapId);

            var towerDoor = From(238296846, 541027);
            Assert.Equal((238295822L, 389), (towerDoor.DestinationMapId, towerDoor.SkillNameId));

            var down = From(238295822, 541028);
            Assert.Equal((238295316L, GuildRaidPassages.DownSkill, 387), (down.DestinationMapId, down.SkillId, down.SkillNameId));
            var up = From(238295822, 541030);
            Assert.Equal((238297364L, GuildRaidPassages.UpSkill, 388), (up.DestinationMapId, up.SkillId, up.SkillNameId));
            Assert.Equal(238295822L, From(238295316, 541029).DestinationMapId);
            Assert.Equal(238295822L, From(238297364, 541031).DestinationMapId);

            // [15,13] and [16,12] stand on diagonal squares, joined by the door and the arch both see.
            var door = From(238295818, 541035);
            Assert.Equal((238296331L, 519, 385), (door.DestinationMapId, door.DestinationCellId, door.SkillNameId));
            Assert.Equal((238295818L, 278), (From(238296331, 541036).DestinationMapId, From(238296331, 541036).DestinationCellId));

            var hub = GuildRaidPassages.HubOf(Gardens).Value;
            Assert.Equal(new[] { 378, 381, 380, 379 },
                         new[] { 1, 2, 3, 4 }.Select(zone => From(hub.MapId, hub.Portals[zone].Id).SkillNameId));
            Assert.All(routes.Where(r => r.GfxId == GuildRaidPassages.ReturnPortalGfx), r => Assert.Equal(382, r.SkillNameId));
        }

        /// <summary>
        /// The corridor opens once the four guardians are beaten -- the goals its goal waits for --
        /// and the tower once the corridor's sixty are; the hub and the zones are never closed.
        /// </summary>
        [Fact]
        public void The_corridor_waits_for_the_guardians_and_the_tower_for_the_corridor()
        {
            if (!HasWorld) return;
            var raid = new RaidInstance(91, Raids.EternalGardens, 1, 7001, DateTimeOffset.UtcNow, TimeSpan.FromHours(1));
            var castle = GuildRaidSanctuary.CastleOf(Gardens);

            Assert.Equal(5, GuildRaidSanctuary.ClosedBy(raid, castle.Corridor[0])?.Id);
            Assert.Equal(GuildRaidManager.CorridorGoal, GuildRaidSanctuary.ClosedBy(raid, castle.Dungeon)?.Id);
            Assert.Null(GuildRaidSanctuary.ClosedBy(raid, castle.Hub));
            Assert.Null(GuildRaidSanctuary.ClosedBy(raid, 238294274));

            foreach (int guardian in new[] { 5, 6, 7 }) raid.SetGoal(guardian, 1);
            Assert.Equal(8, GuildRaidSanctuary.ClosedBy(raid, castle.Corridor[^1])?.Id);
            raid.SetGoal(8, 1);
            Assert.Null(GuildRaidSanctuary.ClosedBy(raid, castle.Corridor[^1]));
            Assert.Equal(GuildRaidManager.CorridorGoal, GuildRaidSanctuary.ClosedBy(raid, castle.Foot)?.Id);

            raid.SetGoal(GuildRaidManager.CorridorGoal, 59);
            Assert.NotNull(GuildRaidSanctuary.ClosedBy(raid, castle.Top));
            raid.SetGoal(GuildRaidManager.CorridorGoal, 60);
            Assert.Null(GuildRaidSanctuary.ClosedBy(raid, castle.Top));
        }

        /// <summary>
        /// A fight won in a corridor room counts its monsters for the corridor; one in the tower,
        /// or in a zone, does not.
        /// </summary>
        [Fact]
        public async Task Only_the_corridor_s_fights_count_for_the_corridor()
        {
            if (!HasWorld) return;
            var raid = new RaidInstance(92, Raids.EternalGardens, 1, 7001, DateTimeOffset.UtcNow, TimeSpan.FromHours(1));
            var castle = GuildRaidSanctuary.CastleOf(Gardens);
            var one = new Dictionary<int, int> { [8285] = 1 };

            await GuildRaidManager.AdvanceByFightAsync(raid, 5, one, 0, castle.Corridor[3]);
            await GuildRaidManager.AdvanceByFightAsync(raid, 5, one, 0, castle.Corridor[9]);
            Assert.Equal(2, raid.GoalAt(GuildRaidManager.CorridorGoal));

            await GuildRaidManager.AdvanceByFightAsync(raid, 5, new Dictionary<int, int> { [GuildRaidSanctuary.Princesa] = 1 }, 0, castle.Top);
            await GuildRaidManager.AdvanceByFightAsync(raid, 2, one, 0, 238297345);
            Assert.Equal(2, raid.GoalAt(GuildRaidManager.CorridorGoal));
            Assert.Equal(1, raid.GoalAt(10));
        }

        /// <summary>
        /// As a raid starts the castle is emptied of the world's groups: six monsters alone in each
        /// corridor room, of the castle's kinds and apart -- [16,15], with five cells well inside,
        /// too --, the Reina in the dungeons and the Princesa at the top.
        /// </summary>
        [Fact]
        public void The_castle_starts_with_its_corridor_and_its_bosses()
        {
            if (!HasWorld) return;
            if (MapManager.GetMapInfo(238296328) == null) MapManager.Initialize();
            if (MapManager.GetMapInfo(238296328) == null) return;
            MobSpawnManager.EnsureMonsterData();
            var castle = GuildRaidSanctuary.CastleOf(Gardens);
            var maps = DatabaseManager.MapsOfSubArea(Gardens.Floors[^1]);
            try
            {
                int placed = GuildRaidSanctuary.Populate(Gardens, new Random(7));
                foreach (long room in castle.Corridor)
                {
                    var groups = MobSpawnManager.GetMobsForMap(room);
                    Assert.True(groups.Count == 6, $"room {room}: {groups.Count} groups, inner cells {MobSpawnManager.GetInnerWalkableCells(room).Count}");
                    Assert.All(groups, g => Assert.Single(g.Members));
                    Assert.All(groups, g => Assert.Contains(g.Members[0].Monster.Id, GuildRaidGuardians.Companions));
                    Assert.Equal(6, groups.Select(g => g.CellId).Distinct().Count());
                }
                Assert.Equal(GuildRaidSanctuary.Reina, MobSpawnManager.GetMobsForMap(castle.Dungeon).Single().Members.Single().Monster.Id);
                Assert.Equal(GuildRaidSanctuary.Princesa, MobSpawnManager.GetMobsForMap(castle.Top).Single().Members.Single().Monster.Id);
                Assert.Empty(MobSpawnManager.GetMobsForMap(castle.Hub));
                Assert.Empty(MobSpawnManager.GetMobsForMap(castle.Foot));
                Assert.Equal(62, placed);
            }
            finally
            {
                foreach (long map in maps) MobSpawnManager.RestoreMap(map);
            }
        }

        /// <summary>
        /// A corridor fight is fought alone, its monster holding "Protectores del Pasillo" and
        /// standing for the scene; the tower's by eight; the hub's are left as they are.
        /// </summary>
        [Fact]
        public void The_corridor_is_fought_alone_with_the_protectors_bonuses()
        {
            if (!HasWorld) return;
            var castle = GuildRaidSanctuary.CastleOf(Gardens);
            FightInstance FightOn(long map, int monster)
            {
                var fight = new FightInstance(1, map);
                fight.AddPlayer(new Fighter { Id = 7001, TeamId = 0, MaxHP = 100, CurrentHP = 100 });
                fight.AddMonster(new Fighter { Id = -1, TeamId = 1, MaxHP = 100, CurrentHP = 100, IsMonster = true, MonsterId = monster });
                GuildRaidSanctuary.OnFightCreated(fight);
                return fight;
            }

            var corridor = FightOn(castle.Corridor[5], 8286);
            Assert.Equal(GuildRaidSanctuary.CorridorPeopleCap, corridor.PeopleCap);
            Assert.Contains(GuildRaidSanctuary.CorridorProtectors, corridor.Rojo[0].Buffs.Actitudes);
            Assert.Same(corridor.Rojo[0], corridor.SceneStandIn);
            Assert.True(GuildRaidSanctuary.IsCorridorFight(corridor));

            var tower = FightOn(castle.Dungeon, GuildRaidSanctuary.Reina);
            Assert.Equal(GuildRaidSanctuary.TowerPeopleCap, tower.PeopleCap);
            Assert.DoesNotContain(GuildRaidSanctuary.CorridorProtectors, tower.Rojo[0].Buffs.Actitudes);

            var zone = FightOn(238294274, 8285);
            Assert.Equal(0, zone.PeopleCap);
            Assert.Null(zone.SceneStandIn);
        }

        /// <summary>
        /// The Santuario fights on its maps' twins -- the unnamed map with the arena flags on the
        /// same square --, and its corridor, without twins, on the castle's two bare boards.
        /// </summary>
        [Fact]
        public void The_santuario_fights_on_its_maps_twins()
        {
            if (!HasWorld) return;
            if (MapManager.GetMapInfo(238294274) == null) MapManager.Initialize();
            if (MapManager.GetMapInfo(238294274) == null) return;

            Assert.Equal(238294018L, MapManager.ResolveArenaMapId(238294274));       // the Obra's middle
            Assert.Equal(238295313L, MapManager.ResolveArenaMapId(238295316));       // the dungeons
            Assert.Equal(238297361L, MapManager.ResolveArenaMapId(238297364));       // the top
            var boards = new[] { 238298890L, 238298892L };
            Assert.All(GuildRaidSanctuary.CastleOf(Gardens).Corridor, room => Assert.Contains(MapManager.ResolveArenaMapId(room), boards));
        }

        /// <summary>
        /// The manuscript stands once on each zone, its page the zone's: "Primera página del
        /// manuscrito" on the Obra to "Quinta" in the corridor's [16,12], torn with "Arrancar una
        /// página" -- and only by someone inside the raid.
        /// </summary>
        [Fact]
        public async Task The_manuscript_gives_each_zone_s_page()
        {
            if (!HasWorld) return;
            var pages = GuildRaidSanctuary.Manuscripts(Gardens).ToList();
            Assert.Equal(new[] { (238294016L, 34304), (238297857L, 34305), (238299394L, 34306), (238301953L, 34307), (238296331L, 34308) },
                         pages.Select(p => (p.MapId, p.Page)));

            InteractiveRegistry.Initialize();
            var registered = InteractiveRegistry.OnMap(238296331).Single(i => i.Element.Id == 541142);
            Assert.Equal(GuildRaidSanctuary.ManuscriptType, registered.Type);
            var tear = registered.Actions.Single();
            Assert.Equal((InteractiveActionKind.RaidManuscript, GuildRaidSanctuary.ManuscriptSkill, GuildRaidSanctuary.TearPageName),
                         (tear.Kind, tear.SkillId, tear.SkillNameId));

            Assert.False(await GuildRaidSanctuary.TearAsync(null, 987654, 238296331, 541142));
        }
    }
}

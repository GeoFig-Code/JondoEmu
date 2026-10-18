using System;
using System.Linq;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.World.Content;
using Jondo.Unity.World.Fights;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// The Sima's light drawn: the staging each band takes on a floor's map, coming up or going
    /// down; how long a band has before it fades; the arena of a fight lit to its floor's band, with
    /// the fight action that plays a staging (jwe 2192).
    /// </summary>
    public class GuildRaidLightsTests
    {
        /// <summary>A floor -1 map with its machine: every band and both ways.</summary>
        private const long Floor1Map = 239079947;

        [Fact]
        public void Each_band_has_its_staging()
        {
            if (!GuildRaidStagings.Has(Floor1Map, "Light_4")) return;   // no client data

            Assert.Equal("Light_0", GuildRaidLights.LightName(Floor1Map, 0, rising: false));
            Assert.Equal("Light_4", GuildRaidLights.LightName(Floor1Map, 4, rising: true));
            Assert.Equal("Light_2_UP", GuildRaidLights.LightName(Floor1Map, 2, rising: true));
            Assert.Equal("Light_2_DOWN", GuildRaidLights.LightName(Floor1Map, 2, rising: false));
            Assert.All(Enumerable.Range(0, 5), level =>
                Assert.True(GuildRaidStagings.Has(Floor1Map, GuildRaidLights.LightName(Floor1Map, level, rising: false))));
            Assert.Equal(0, GuildRaidStagings.IdOf(Floor1Map, "Light_0"));    // a uniqueId of 0 is one
            Assert.Equal(GuildRaidStagings.None, GuildRaidStagings.IdOf(Floor1Map, "Light_9"));

            // A map with no way down for a band takes its way up.
            long upOnly = FindUpOnly();
            if (upOnly != 0) Assert.Equal("Light_1_UP", GuildRaidLights.LightName(upOnly, 1, rising: false));
        }

        private static long FindUpOnly()
        {
            foreach (long map in DatabaseManager.MapsOfSubArea(Raids.Of(Raids.Gigalodon).Floors[0]))
                if (GuildRaidStagings.Has(map, "Light_1_UP") && !GuildRaidStagings.Has(map, "Light_1_DOWN")) return map;
            return 0;
        }

        /// <summary>A band lasts two minutes: what is left of it, and nothing to wait for in the dark.</summary>
        [Fact]
        public void A_band_fades_after_its_two_minutes()
        {
            var start = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
            var raid = new RaidInstance(501, Raids.Gigalodon, 1, 7001, start, TimeSpan.FromHours(1));
            raid.SetLight(1, 4, start);

            Assert.Equal(TimeSpan.FromMinutes(2), raid.UntilFade(1, start));
            Assert.Equal(TimeSpan.FromSeconds(30), raid.UntilFade(1, start + TimeSpan.FromSeconds(90)));
            Assert.Equal(3, raid.LightAt(1, start + TimeSpan.FromMinutes(2)));
            Assert.Equal(TimeSpan.FromMinutes(2), raid.UntilFade(1, start + TimeSpan.FromMinutes(2)));
            Assert.Equal(TimeSpan.MaxValue, raid.UntilFade(1, start + TimeSpan.FromMinutes(9)));
            Assert.Equal(TimeSpan.MaxValue, raid.UntilFade(2, start));
        }

        /// <summary>
        /// A fight on a lit floor lights its arena to the band it began with; a fight elsewhere is
        /// left as it is.
        /// </summary>
        [Fact]
        public void A_fight_s_arena_takes_its_floor_s_band()
        {
            var fight = new FightInstance(9_200_001, Floor1Map) { RaidLight = 2 };
            if (GuildRaidStagings.Has(fight.MapId, "Light_2_UP"))
                Assert.Equal("Light_2_UP", GuildRaidLights.ArenaLight(fight));
            Assert.Null(GuildRaidLights.ArenaLight(new FightInstance(9_200_002, Floor1Map)));
        }

        /// <summary>The fight action that plays a staging: jwe { f3 author, f14 2192, f15 { f1 intensity, f3 staging } }.</summary>
        [Fact]
        public void A_staging_in_a_fight_is_action_2192()
        {
            var fields = ProtoMessage.Parse(FightProtocol.BuildTriggerStaging(-7, 4)).Fields;
            Assert.Equal(-7L, fields.First(f => f.FieldNumber == 3).VarIntValue);
            Assert.Equal(2192L, fields.First(f => f.FieldNumber == 14).VarIntValue);
            var staging = ProtoMessage.Parse(fields.First(f => f.FieldNumber == 15).BytesValue).Fields;
            Assert.Equal(100L, staging.First(f => f.FieldNumber == 1).VarIntValue);
            Assert.DoesNotContain(staging, f => f.FieldNumber == 2);
            Assert.Equal(4L, staging.First(f => f.FieldNumber == 3).VarIntValue);
        }
    }
}

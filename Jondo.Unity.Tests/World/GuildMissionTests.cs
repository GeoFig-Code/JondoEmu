using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// The guild's weekly missions («Encargos»): twelve drawn in the ranks and categories chosen,
    /// stored for the week, and told to the client as the captures carry them.
    /// </summary>
    [Collection("guild raids")]
    public class GuildMissionTests : IDisposable
    {
        private readonly string _file;

        /// <summary>A Wednesday, and the Wednesday after: two weeks of the game.</summary>
        private static readonly DateTimeOffset ThisWeek = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset NextWeek = ThisWeek.AddDays(7);

        public GuildMissionTests()
        {
            _file = Path.Combine(Path.GetTempPath(), $"jondo-encargos-{Guid.NewGuid():N}.db");
            GuildStore.ConnectionStringOverride = $"Data Source={_file}";
        }

        public void Dispose()
        {
            GuildStore.ConnectionStringOverride = null;
            TestDatabases.ReleasePool(_file);
            try { File.Delete(_file); } catch (IOException) { }
        }

        private static string Hex(byte[] bytes) => string.Concat(bytes.Select(b => b.ToString("x2")));

        /// <summary>
        /// A mission as «muchas acciones en mi gremio como líder» carries it: its uuid, the mission
        /// and { {}, its first objective } -- 65 the regulation of bworks; 417 opens with 418.
        /// </summary>
        [Fact]
        public void A_mission_goes_as_the_capture_carries_it()
        {
            Assert.Equal("0a2461643932613534352d396463322d343638382d386463332d393166336534623063636466184122040a002041",
                         Hex(GuildProtocol.Mission(new GuildMissions.Drawn(0, 65, "ad92a545-9dc2-4688-8dc3-91f3e4b0ccdf", 65)).Build()));
            if (GuildMissions.All.Count == 0) return;   // no client data
            Assert.Equal(418, GuildMissions.FirstObjectiveOf(GuildMissions.MissionOf(417)));
            Assert.Equal(65, GuildMissions.FirstObjectiveOf(GuildMissions.MissionOf(65)));
        }

        /// <summary>
        /// The preferences: f1 the super categories, f2 the ranks; twelve drawn among them, none
        /// twice and none of the events'. None chosen is all.
        /// </summary>
        [Fact]
        public void Twelve_are_drawn_in_the_preferences()
        {
            byte[] jgs = Pb.New().Bytes(1, new byte[] { 1, 2, 3 }).Bytes(2, new byte[] { 4, 5 }).Build();
            var (categories, ranks) = GuildMissions.PreferencesOf(jgs);
            Assert.Equal(new[] { 1, 2, 3 }, categories);
            Assert.Equal(new[] { 4, 5 }, ranks);
            if (GuildMissions.All.Count == 0) return;   // no client data

            var drawn = GuildMissions.Draw(jgs, new Random(7));
            Assert.Equal(GuildMissions.PerWeek, drawn.Count);
            Assert.Equal(drawn.Count, drawn.Select(m => m.Id).Distinct().Count());
            Assert.All(drawn, m => Assert.Contains(m.SuperCategoryId, categories));
            Assert.All(drawn, m => Assert.Contains(m.RankId, ranks));

            var any = GuildMissions.Draw(Array.Empty<byte>(), new Random(7));
            Assert.Equal(GuildMissions.PerWeek, any.Count);
            Assert.DoesNotContain(any, m => m.SuperCategoryId == GuildMissions.EventsSuperCategory);
        }

        /// <summary>
        /// None without a tier; drawn once a tier is chosen, the same the rest of the week, and
        /// new the week after. The tab (jei) and the tier's answer (jdb) carry the twelve.
        /// </summary>
        [Fact]
        public void The_week_keeps_its_twelve()
        {
            if (GuildMissions.All.Count == 0) return;   // no client data
            var guild = GuildStore.Create(7101, "Encargados", 165, 8, 16744448, 9476018);
            Assert.Empty(GuildMissions.Of(guild.Id, ThisWeek));
            Assert.Equal("1a00", Hex(GuildProtocol.BuildMissions(Array.Empty<GuildMissions.Drawn>())));

            var week = GuildActivity.Choose(guild.Id, 1, Array.Empty<byte>(), ThisWeek);
            var missions = GuildMissions.Of(guild.Id, ThisWeek);
            Assert.Equal(GuildMissions.PerWeek, missions.Count);
            Assert.Equal(missions.Select(m => m.Uuid), GuildMissions.Of(guild.Id, ThisWeek.AddDays(1)).Select(m => m.Uuid));
            Assert.NotEqual(missions.Select(m => m.Uuid), GuildMissions.Of(guild.Id, NextWeek).Select(m => m.Uuid));

            var jei = ProtoMessage.Parse(GuildProtocol.BuildMissions(missions.ToList()));
            var jdf = ProtoMessage.Parse(ProtoMessage.Parse(jei.Fields.Single(f => f.FieldNumber == 1).BytesValue)
                                                     .Fields.Single(f => f.FieldNumber == 1).BytesValue);
            Assert.Equal(GuildMissions.PerWeek, jdf.Fields.Count(f => f.FieldNumber == 2));

            var jcy = ProtoMessage.Parse(ProtoMessage.Parse(GuildProtocol.BuildTierChosen(week, 250, 0, missions))
                                                     .Fields.Single(f => f.FieldNumber == 2).BytesValue);
            Assert.Equal("080118fa01", Hex(jcy.Fields.Single(f => f.FieldNumber == 2).BytesValue));
            Assert.Equal(GuildMissions.PerWeek, ProtoMessage.Parse(jcy.Fields.Single(f => f.FieldNumber == 3).BytesValue)
                                                            .Fields.Count(f => f.FieldNumber == 2));
        }

        /// <summary>The objective's state of a mission (jdx f4), whatever comes before it.</summary>
        private static string ObjectiveState(byte[] jdx)
            => Hex(ProtoMessage.Parse(jdx).Fields.Single(f => f.FieldNumber == 4).BytesValue);

        /// <summary>
        /// The objective's state is a oneof the client reads by its case (iz.ctor): f1 {} not
        /// started, f3 { count } on the way, f2 {} done; f4 the objective after it. The mission
        /// changed goes in a jdc, the mission done in a jen.
        /// </summary>
        [Fact]
        public void A_mission_carries_where_the_member_stands()
        {
            var drawn = new GuildMissions.Drawn(0, 65, "ad92a545-9dc2-4688-8dc3-91f3e4b0ccdf", 65);
            Assert.Equal("0a002041", ObjectiveState(GuildProtocol.Mission(drawn).Build()));
            Assert.Equal("1a02080c2041", ObjectiveState(GuildProtocol.Mission(drawn, new GuildMissions.Standing(12, false)).Build()));
            Assert.Equal("12002041", ObjectiveState(GuildProtocol.Mission(drawn, new GuildMissions.Standing(50, true)).Build()));

            var jdc = ProtoMessage.Parse(GuildProtocol.BuildMissionChanged(drawn, new GuildMissions.Standing(50, true)));
            Assert.Equal("12002041", ObjectiveState(jdc.Fields.Single(f => f.FieldNumber == 1).BytesValue));
            Assert.Equal("0841", Hex(GuildProtocol.BuildMissionDone(65)));
        }

        /// <summary>
        /// What a won fight is worth: a boss once; a family's monsters one each, outside dungeons
        /// («quitando mazmorras») and inside the area the objective names, if it names one.
        /// </summary>
        [Fact]
        public void A_fight_is_worth_what_the_objective_asks()
        {
            var boss = new GuildMissions.Objective(2, 2, 1) { MonsterId = 147, InDungeon = true };
            var family = new GuildMissions.Objective(25, 25, 1)
            {
                FamilyId = 6, Quantity = 50, SubareaId = -1, FamilyMonsters = new List<int> { 62, 178, 6901, 6902 },
            };
            var inArea = family with { AreaId = 63 };
            var beaten = new Dictionary<int, int> { [147] = 1, [62] = 3, [178] = 2, [99] = 4 };

            Assert.Equal(1, GuildMissionWatcher.Worth(boss, beaten, true, 30, 18));
            Assert.Equal(0, GuildMissionWatcher.Worth(boss, new Dictionary<int, int> { [62] = 1 }, true, 30, 18));
            Assert.Equal(5, GuildMissionWatcher.Worth(family, beaten, false, 10, 1));
            Assert.Equal(0, GuildMissionWatcher.Worth(family, beaten, true, 10, 1));
            Assert.Equal(0, GuildMissionWatcher.Worth(inArea, beaten, false, 10, 1));
            Assert.Equal(5, GuildMissionWatcher.Worth(inArea, beaten, false, 10, 63));
            Assert.Equal(1, GuildMissions.TargetOf(boss));
            Assert.Equal(50, GuildMissions.TargetOf(family));

            // «Terminar la franja 3: Sueño I»: that band at that intensity, and no other.
            var dream = new GuildMissions.Objective(166, 166, 1) { Intensity = 1, Stage = 3, SubareaId = 904 };
            Assert.Equal(1, GuildMissionWatcher.DreamWorth(dream, 1, 3));
            Assert.Equal(0, GuildMissionWatcher.DreamWorth(dream, 1, 2));
            Assert.Equal(0, GuildMissionWatcher.DreamWorth(dream, 2, 3));
            Assert.Equal(0, GuildMissionWatcher.DreamWorth(boss, 1, 3));
            Assert.Equal(0, GuildMissionWatcher.DreamWorth(dream with { SeedId = 8 }, 1, 3));

            if (GuildMissions.All.Count == 0) return;   // no client data
            Assert.Equal(147, GuildMissions.ObjectiveOf(2).MonsterId);
            Assert.Equal(new[] { 62, 178, 6901, 6902 }, GuildMissions.ObjectiveOf(25).FamilyMonsters);
        }

        /// <summary>
        /// «Un miembro solo puede realizar una vez cada encargo»: the count stops at the target, the
        /// mission is done once, and another member, or another week, starts from nothing. The tab
        /// shows each member his own.
        /// </summary>
        [Fact]
        public void A_member_does_each_mission_once()
        {
            if (GuildMissions.All.Count == 0) return;   // no client data
            var guild = GuildStore.Create(7102, "Regulares", 165, 8, 16744448, 9476018);
            GuildActivity.Choose(guild.Id, 1, Array.Empty<byte>(), ThisWeek);
            var mission = GuildMissions.Of(guild.Id, ThisWeek)[3];
            int target = GuildMissions.TargetOf(GuildMissions.ObjectiveOf(mission.ObjectiveId));

            if (target > 1)
            {
                var halfway = GuildMissions.Advance(guild.Id, 7102, mission, target - 1, ThisWeek);
                Assert.Equal(new GuildMissions.Standing(target - 1, false), halfway.Value.Now);
                Assert.False(halfway.Value.JustDone);
            }
            var done = GuildMissions.Advance(guild.Id, 7102, mission, 5, ThisWeek);
            Assert.Equal(new GuildMissions.Standing(target, true), done.Value.Now);
            Assert.True(done.Value.JustDone);
            Assert.Null(GuildMissions.Advance(guild.Id, 7102, mission, 5, ThisWeek));

            Assert.Equal(new GuildMissions.Standing(target, true), GuildMissions.StandingsOf(guild.Id, 7102, ThisWeek)[mission.Slot]);
            Assert.Empty(GuildMissions.StandingsOf(guild.Id, 7103, ThisWeek));
            Assert.Empty(GuildMissions.StandingsOf(guild.Id, 7102, NextWeek));

            var jei = ProtoMessage.Parse(GuildProtocol.BuildMissions(GuildMissions.Of(guild.Id, ThisWeek).ToList(),
                                                                     GuildMissions.StandingsOf(guild.Id, 7102, ThisWeek)));
            var jdf = ProtoMessage.Parse(ProtoMessage.Parse(jei.Fields.Single(f => f.FieldNumber == 1).BytesValue)
                                                     .Fields.Single(f => f.FieldNumber == 1).BytesValue);
            var states = jdf.Fields.Where(f => f.FieldNumber == 2).Select(f => ObjectiveState(f.BytesValue)).ToList();
            Assert.StartsWith("1200", states[3]);
            Assert.Equal(GuildMissions.PerWeek - 1, states.Count(s => s.StartsWith("0a00")));
        }
    }
}

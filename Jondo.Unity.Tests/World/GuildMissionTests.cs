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
            Assert.Equal("0a2430303030303030302d303030302d343030302d383030302d303030303030303030303038184122040a002041",
                         Hex(GuildProtocol.Mission(new GuildMissions.Drawn(0, 65, "00000000-0000-4000-8000-000000000008", 65)).Build()));
            if (GuildMissions.All.Count == 0) return;   // no client data
            Assert.Equal(418, GuildMissions.FirstObjectiveOf(GuildMissions.MissionOf(417)));
            Assert.Equal(65, GuildMissions.FirstObjectiveOf(GuildMissions.MissionOf(65)));
        }

        /// <summary>
        /// The window's two answers that went empty: the preferences back as the client reads them
        /// (jgq f3 { f1 jgs }), and the member's acknowledgments waiting as the rewards' first tile
        /// (jfz f1 { f1 }).
        /// </summary>
        [Fact]
        public void The_preferences_and_the_weekly_rewards_go_back_as_the_client_reads_them()
        {
            byte[] jgs = Pb.New().Bytes(1, new byte[] { 1, 2 }).Bytes(2, new byte[] { 4 }).Build();
            var jgq = ProtoMessage.Parse(GuildProtocol.BuildMissionPreferences(jgs)).Fields.Single();
            Assert.Equal(3, jgq.FieldNumber);
            var inner = ProtoMessage.Parse(jgq.BytesValue).Fields.Single();
            Assert.Equal(1, inner.FieldNumber);
            Assert.Equal(jgs, inner.BytesValue);

            Assert.Equal("0a03" + "08f001", Convert.ToHexString(GuildProtocol.BuildWeeklyRewards(240)).ToLowerInvariant());
            Assert.Equal("0a00", Convert.ToHexString(GuildProtocol.BuildWeeklyRewards(0)).ToLowerInvariant());
            Assert.Equal("0a0a" + "080a" + "1206" + "08b9fd01" + "1001",
                         Convert.ToHexString(GuildProtocol.BuildWeeklyRewards(10, new[] { (32441, 1) })).ToLowerInvariant());
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

        /// <summary>
        /// Rerolling: the same place, rank and category, a new uuid, nobody's progress, and never a
        /// mission seen this week -- until there is none left, which the client is told with f3 {}.
        /// Unpaid, nothing moves.
        /// </summary>
        [Fact]
        public void A_reroll_never_brings_back_a_mission_of_the_week()
        {
            Assert.Equal("1a00", Hex(GuildProtocol.BuildRerollRefused()));
            if (GuildMissions.All.Count == 0) return;   // no client data
            var guild = GuildStore.Create(7104, "Relanzadores", 165, 8, 16744448, 9476018);
            GuildActivity.Choose(guild.Id, 1, Array.Empty<byte>(), ThisWeek);
            var first = GuildMissions.Of(guild.Id, ThisWeek).ToList();
            var old = first[2];
            var kind = GuildMissions.MissionOf(old.MissionId);
            GuildMissions.Advance(guild.Id, 7104, old, 1, ThisWeek);

            Assert.Equal(GuildMissions.RerollOutcome.Unknown, GuildMissions.Reroll(guild.Id, "nope", ThisWeek, new Random(1), () => true).Outcome);
            Assert.Equal(GuildMissions.RerollOutcome.NotPaid, GuildMissions.Reroll(guild.Id, old.Uuid, ThisWeek, new Random(1), () => false).Outcome);
            Assert.Equal(first.Select(d => d.Uuid), GuildMissions.Of(guild.Id, ThisWeek).Select(d => d.Uuid));

            var seen = first.Select(d => d.MissionId).ToHashSet();
            var current = old;
            for (int i = 0; i < 1000; i++)
            {
                var (outcome, was, fresh) = GuildMissions.Reroll(guild.Id, current.Uuid, ThisWeek, new Random(i), () => true);
                if (outcome == GuildMissions.RerollOutcome.NoneLeft) break;
                Assert.Equal(GuildMissions.RerollOutcome.Done, outcome);
                Assert.Equal(current.Uuid, was.Uuid);
                Assert.Equal(old.Slot, fresh.Slot);
                Assert.NotEqual(current.Uuid, fresh.Uuid);
                Assert.True(seen.Add(fresh.MissionId), $"mission {fresh.MissionId} came back");
                var mission = GuildMissions.MissionOf(fresh.MissionId);
                Assert.Equal((kind.RankId, kind.SuperCategoryId), (mission.RankId, mission.SuperCategoryId));
                Assert.Equal(fresh.Uuid, GuildMissions.Of(guild.Id, ThisWeek)[old.Slot].Uuid);
                Assert.False(GuildMissions.StandingsOf(guild.Id, 7104, ThisWeek).ContainsKey(old.Slot));

                var jed = ProtoMessage.Parse(ProtoMessage.Parse(GuildProtocol.BuildRerolled(was.Uuid, fresh))
                                                         .Fields.Single(f => f.FieldNumber == 2).BytesValue);
                Assert.Equal(was.Uuid, System.Text.Encoding.UTF8.GetString(jed.Fields.Single(f => f.FieldNumber == 1).BytesValue));
                current = fresh;
            }
            Assert.Equal(GuildMissions.RerollOutcome.NoneLeft,
                         GuildMissions.Reroll(guild.Id, current.Uuid, ThisWeek, new Random(5), () => true).Outcome);
            Assert.Equal(GuildMissions.PerWeek, GuildMissions.Of(guild.Id, ThisWeek).Select(d => d.MissionId).Distinct().Count());
        }

        /// <summary>
        /// The interested members: kept by mission of the week, dropped with a reroll, and drawn in
        /// the jdx's f2 as the subscribers window reads them -- f1 connected, f2 name, f3 breed,
        /// f5 level, f6 done, f7 sex, f8 the character.
        /// </summary>
        [Fact]
        public void The_interested_members_ride_with_their_mission()
        {
            var drawn = new GuildMissions.Drawn(0, 65, "00000000-0000-4000-8000-000000000008", 65);
            var keka = new GuildProtocol.Subscriber(7105, "Keka", 18, 1, 200, true, true);
            Assert.Equal("080112044b656b61181228c8013001380140c137", Hex(GuildProtocol.SubscriberBlock(keka).Build()));
            var jdx = ProtoMessage.Parse(GuildProtocol.Mission(drawn, GuildMissions.Standing.None, new[] { keka }).Build());
            Assert.Equal(new[] { 1, 2, 3, 4 }, jdx.Fields.Select(f => f.FieldNumber));
            var jdk = ProtoMessage.Parse(GuildProtocol.BuildInterestAnswer(drawn, GuildMissions.Standing.None, new[] { keka }));
            var jdh = ProtoMessage.Parse(jdk.Fields.Single(f => f.FieldNumber == 2).BytesValue);
            Assert.Equal(2, jdh.Fields.Single().FieldNumber);

            if (GuildMissions.All.Count == 0) return;   // no client data
            var guild = GuildStore.Create(7105, "Interesados", 165, 8, 16744448, 9476018);
            GuildActivity.Choose(guild.Id, 1, Array.Empty<byte>(), ThisWeek);
            var missions = GuildMissions.Of(guild.Id, ThisWeek);
            GuildMissions.SetInterest(guild.Id, missions[1].Slot, 7105, true, ThisWeek);
            GuildMissions.SetInterest(guild.Id, missions[1].Slot, 7106, true, ThisWeek);
            GuildMissions.SetInterest(guild.Id, missions[4].Slot, 7105, true, ThisWeek);
            GuildMissions.SetInterest(guild.Id, missions[4].Slot, 7105, false, ThisWeek);
            var interest = GuildMissions.InterestOf(guild.Id, ThisWeek);
            Assert.Equal(new long[] { 7105, 7106 }, interest[missions[1].Slot].OrderBy(c => c));
            Assert.False(interest.ContainsKey(missions[4].Slot));
            Assert.Empty(GuildMissions.InterestOf(guild.Id, NextWeek));

            GuildMissions.Reroll(guild.Id, missions[1].Uuid, ThisWeek, new Random(2), () => true);
            Assert.False(GuildMissions.InterestOf(guild.Id, ThisWeek).ContainsKey(missions[1].Slot));
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
            var drawn = new GuildMissions.Drawn(0, 65, "00000000-0000-4000-8000-000000000008", 65);
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

            // «Terminar la prueba de sueños FONSOCAC»: the trial whose id is its seedId, no other.
            var trial = new GuildMissions.Objective(436, 434, 1) { SeedId = 8, SubareaId = 904 };
            Assert.Equal(1, GuildMissionWatcher.TrialWorth(trial, 8));
            Assert.Equal(0, GuildMissionWatcher.TrialWorth(trial, 17));
            Assert.Equal(0, GuildMissionWatcher.TrialWorth(dream, 8));

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

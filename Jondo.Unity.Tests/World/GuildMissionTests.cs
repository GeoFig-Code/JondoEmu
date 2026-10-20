using System;
using System.IO;
using System.Linq;
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
    }
}

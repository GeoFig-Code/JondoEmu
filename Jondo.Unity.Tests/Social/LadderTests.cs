using System;
using System.Collections.Generic;
using System.Linq;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Xunit;

namespace Jondo.Unity.Tests.Social
{
    /// <summary>
    /// The ladder window's tabs: the lines as the real server wrote them in «koliseo completo con
    /// invitacion-koli 2vs2-hasta final de combate», the rankings, the Koliseo's filters and the
    /// dreams' order by the client's own rule.
    /// </summary>
    [Collection("MapManager")]
    public class LadderTests : IDisposable
    {
        private static readonly Ladders.Person SacriMaster = new(4618496, 290, 11, 0, "Sacri-Master", 354);
        private readonly List<long> _koliseo = new();
        private readonly List<long> _dreams = new();
        private readonly int _seasons = KoliseoLadder.LastSeasonNumber();

        public void Dispose()
        {
            Ladders.PeopleOverride = null;
            foreach (long id in _koliseo) KoliseoLadder.Erase(id);
            KoliseoLadder.EraseSeasonsAfter(_seasons);
            foreach (long id in _dreams) Ladders.EraseDream(id);
        }

        private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

        private static void People(params (Ladders.Person Who, long Experience)[] people)
            => Ladders.PeopleOverride = () => people;

        // ─── The lines, byte for byte ───────────────────────────────────────────────────────

        [Fact]
        public void The_player_s_own_experience_line_is_the_capture_s()
        {
            var line = new Ladders.ExperienceLine(13353, SacriMaster, 23793536711);
            Assert.Equal("0a2608c79dd2d1581a1b10a202180b220c53616372692d4d617374657230e2023880f2990220a968",
                         Hex(LadderProtocol.BuildOwn(1, LadderProtocol.ExperienceLine(line))));
        }

        [Fact]
        public void The_player_s_own_achievements_line_is_the_capture_s()
        {
            var line = new Ladders.AchievementLine(20973, SacriMaster, 8093);
            Assert.Equal("0a2408eda301109d3f1a1b10a202180b220c53616372692d4d617374657230e2023880f29902",
                         Hex(LadderProtocol.BuildOwn(1, LadderProtocol.AchievementLine(line))));
        }

        [Fact]
        public void A_koliseo_line_and_a_woman_s_experience_line_are_the_capture_s()
        {
            var barry = new Ladders.Person(10236891, 294, 9, 0, "Barry-ll", 345);
            Assert.Equal("0801104e1a1710a6021809220842617272792d6c6c30d90238dbe7f004202e28bf1e",
                         Hex(LadderProtocol.KoliseoLine(new Ladders.KoliseoLine(1, barry, 78, 46, 3903)).Build()));

            var george = new Ladders.Person(4784683, 290, 16, 1, "George", 968);
            Assert.Equal("08e9daa8fa9ab0021a1710a2021810220647656f726765280130c80738ab84a4022001",
                         Hex(LadderProtocol.ExperienceLine(new Ladders.ExperienceLine(1, george, 10452596305257)).Build()));
        }

        /// <summary>iqn: f1 only for the dreamer, then intensity, rank, the character, turn, when and waves.</summary>
        [Fact]
        public void A_dream_line_carries_intensity_waves_turn_and_day()
        {
            var done = new DateTime(2026, 7, 5, 23, 25, 49, DateTimeKind.Utc).AddTicks(3426245);
            var line = new Ladders.DreamLine(11223, SacriMaster, false, 4, 6, 20, done);
            var iqn = ProtoMessage.Parse(LadderProtocol.DreamLine(line).Build());
            Assert.DoesNotContain(iqn.Fields, f => f.FieldNumber == 1);
            Assert.Equal(4, iqn.Fields.Single(f => f.FieldNumber == 2).VarIntValue);
            Assert.Equal(11223, iqn.Fields.Single(f => f.FieldNumber == 3).VarIntValue);
            Assert.Equal(20, iqn.Fields.Single(f => f.FieldNumber == 5).VarIntValue);
            Assert.Equal("2026-07-05T23:25:49.3426245Z", System.Text.Encoding.UTF8.GetString(iqn.Fields.Single(f => f.FieldNumber == 6).BytesValue));
            Assert.Equal(6, iqn.Fields.Single(f => f.FieldNumber == 7).VarIntValue);

            var dreamer = ProtoMessage.Parse(LadderProtocol.DreamLine(line with { Dreamer = true }).Build());
            Assert.Equal(1, dreamer.Fields.Single(f => f.FieldNumber == 1).VarIntValue);
        }

        [Fact]
        public void Without_a_line_the_own_answer_is_empty()
            => Assert.Empty(LadderProtocol.BuildOwn(1, null));

        // ─── The rankings ───────────────────────────────────────────────────────────────────

        [Fact]
        public void Experience_ranks_one_server_and_names_are_searched()
        {
            People((new Ladders.Person(9_300_001, 290, 1, 0, "Uno", 100), 5_000),
                   (new Ladders.Person(9_300_002, 290, 2, 1, "Dos", 150), 90_000),
                   (new Ladders.Person(9_300_003, 291, 3, 0, "Tres", 200), 900_000));

            var here = Ladders.Experience(290);
            Assert.Equal(new long[] { 9_300_002, 9_300_001 }, here.Select(l => l.Who.Id));
            Assert.Equal(new[] { 1, 2 }, here.Select(l => l.Rank));
            Assert.Equal(3, Ladders.Experience(0).Count);
            Assert.Equal(9_300_003, Ladders.Experience(0)[0].Who.Id);

            var found = Ladders.Named(Ladders.Experience(0), l => l.Who, "uno");
            Assert.Equal((9_300_001L, 3), (found.Single().Who.Id, found.Single().Rank));
            Assert.Empty(Ladders.Named(Ladders.Experience(0), l => l.Who, "  "));
        }

        [Fact]
        public void The_koliseo_ranks_the_placed_by_rating_with_the_tab_s_filters()
        {
            long a = 9_300_011, b = 9_300_012;
            foreach (long id in new[] { a, b }) { KoliseoLadder.Erase(id); _koliseo.Add(id); }
            People((new Ladders.Person(a, 290, 8, 0, "Ganador", 200), 0),
                   (new Ladders.Person(b, 290, 9, 0, "Perdedor", 35), 0));

            Assert.DoesNotContain(Ladders.Koliseo(1), l => l.Who.Id == a);    // not placed yet
            for (int i = 0; i < KoliseoLadder.PlacementFights; i++)
                KoliseoLadder.Record(1, new[] { (a, 200) }, new[] { (b, 35) });

            var lines = Ladders.Koliseo(1).Where(l => l.Who.Id == a || l.Who.Id == b).ToList();
            Assert.Equal(new[] { a, b }, lines.Select(l => l.Who.Id));
            Assert.Equal((100, 0), (lines[0].WinRate, lines[1].WinRate));
            Assert.NotEqual(KoliseoLeagues.None, lines[0].League);

            Assert.DoesNotContain(Ladders.Koliseo(1, breed: 8), l => l.Who.Id == b);
            Assert.Contains(Ladders.Koliseo(1, breed: 8), l => l.Who.Id == a);
            Assert.Contains(Ladders.Koliseo(1, firstLevel: 30), l => l.Who.Id == b);
            Assert.DoesNotContain(Ladders.Koliseo(1, firstLevel: 30), l => l.Who.Id == a);
            Assert.Contains(Ladders.Koliseo(1, firstLevel: 200), l => l.Who.Id == a);
            Assert.DoesNotContain(Ladders.Koliseo(2), l => l.Who.Id == a);    // another mode
        }

        [Fact]
        public void The_bands_and_the_win_rate()
        {
            Assert.True(Ladders.InBand(29, 20));
            Assert.False(Ladders.InBand(30, 20));
            Assert.True(Ladders.InBand(250, 200));
            Assert.Equal(67, Ladders.WinRate(2, 3));
            Assert.Equal(0, Ladders.WinRate(0, 0));
        }

        /// <summary>
        /// «ui.ladder.infiniteDreamRules»: intensity, then waves, then the earlier turn, then the day;
        /// each keeps his best, and the capture's groups show the dreamer first.
        /// </summary>
        [Fact]
        public void Dreams_rank_by_the_client_s_rule_and_each_keeps_his_best()
        {
            long dreamer = 9_300_021, friend = 9_300_022, other = 9_300_023;
            foreach (long id in new[] { dreamer, friend, other }) { Ladders.EraseDream(id); _dreams.Add(id); }
            People((new Ladders.Person(dreamer, 290, 1, 0, "Soñador", 200), 0),
                   (new Ladders.Person(friend, 290, 2, 0, "Amigo", 200), 0),
                   (new Ladders.Person(other, 290, 3, 0, "Otro", 200), 0));
            var day = new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);

            Ladders.RecordDream(new[] { new Ladders.DreamRecord(friend, false, 4, 10, 30, day.AddMinutes(5)),
                                        new Ladders.DreamRecord(dreamer, true, 4, 10, 30, day.AddMinutes(5)) });
            Ladders.RecordDream(new[] { new Ladders.DreamRecord(other, true, 4, 10, 25, day.AddDays(1)) });
            Assert.Equal(new[] { other, dreamer, friend }, Ladders.Dreams(290).Select(l => l.Who.Id));

            // A worse dream changes nothing; a higher intensity goes first.
            Assert.Empty(Ladders.RecordDream(new[] { new Ladders.DreamRecord(other, true, 3, 50, 1, day) }));
            Assert.Single(Ladders.RecordDream(new[] { new Ladders.DreamRecord(friend, true, 5, 1, 90, day.AddDays(2)) }));
            var lines = Ladders.Dreams(290);
            Assert.Equal(new[] { friend, other, dreamer }, lines.Select(l => l.Who.Id));
            Assert.Equal((5, 1, 90, true), (lines[0].Intensity, lines[0].Waves, lines[0].Turn, lines[0].Dreamer));
        }
    }
}

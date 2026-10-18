using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.World.Fights;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// The conquests broadcast to the whole world (ifa, iew): they run alone, or another test's
    /// pipe would read a prism it never asked about.
    /// </summary>
    [CollectionDefinition("conquests", DisableParallelization = true)]
    public class ConquestsCollection { }

    /// <summary>
    /// A prism beaten: weakened, vulnerable, its zone's King of the Hill, and the winner's
    /// priority (Translations 354323, 1149316, 1149317).
    /// </summary>
    [Collection("conquests")]
    public class ConquestTests : IDisposable
    {
        private readonly string _file;

        /// <summary>Three maps of subarea 838, and the prism stands on the first.</summary>
        private const long PrismMap = 144441344, OtherMap = 144441346, ThirdMap = 144442368;
        private const int Zone = 838;

        private const long CharacterA = 8_940_100_001, AccountA = 8_940_000_001;
        private const long CharacterB = 8_940_100_002, AccountB = 8_940_000_002;

        public ConquestTests()
        {
            _file = Path.Combine(Path.GetTempPath(), $"jondo-conquista-{Guid.NewGuid():N}.db");
            AllianceStore.ConnectionStringOverride = $"Data Source={_file}";
            PrismStore.ConnectionStringOverride = $"Data Source={_file}";
            Conquests.Clear();
        }

        public void Dispose()
        {
            AllianceStore.SetTakesPartInAva(CharacterA, false);
            AllianceStore.SetTakesPartInAva(CharacterB, false);
            ConquestHandler.Forget(CharacterA);
            ConquestHandler.Forget(CharacterB);
            Conquests.Clear();
            PrismStore.ConnectionStringOverride = null;
            AllianceStore.ConnectionStringOverride = null;
            TestDatabases.ReleasePool(_file);
            try { File.Delete(_file); } catch (IOException) { }
        }

        private static Conquests.Presence At(long alliance, long map, long character) => new(alliance, map, character);

        // ─── The rules ──────────────────────────────────────────────────────────

        /// <summary>Alone in the zone, an alliance dominates every minute and wins at the twentieth.</summary>
        [Fact]
        public void An_alliance_alone_wins_at_twenty_minutes()
        {
            var war = Conquests.Start(9001, defenderId: 1, nowMs: 0, endsUtcMs: 7_200_000);
            var present = new[] { At(2, PrismMap, 20), At(2, OtherMap, 21) };
            for (int minute = 1; minute < Conquests.DominationToWin; minute++)
                Assert.Equal(0, Conquests.Tick(war, present, minute * Conquests.TickMs));
            Assert.Equal(2, Conquests.Tick(war, present, 20 * Conquests.TickMs));

            var attacker = war.SideOf(2);
            Assert.Equal(20, attacker.Domination);
            Assert.Equal(2, attacker.Present);
            Assert.Equal(40, attacker.Scores[Conquests.ScoreOfMaps]);   // two maps a minute
            Assert.Equal(0, war.SideOf(1).Domination);                 // the defence was never there
        }

        /// <summary>A map's point to whoever outnumbers the rest on it; a tie on a map gives it to nobody.</summary>
        [Fact]
        public void The_maps_go_to_the_most_numerous()
        {
            var war = Conquests.Start(9002, 1, 0, 7_200_000);
            Conquests.Tick(war, new[]
            {
                At(1, PrismMap, 10), At(2, PrismMap, 20), At(2, PrismMap, 21),   // two against one
                At(1, OtherMap, 11), At(2, OtherMap, 22),                         // one against one
                At(1, ThirdMap, 12),                                              // alone
            }, Conquests.TickMs);

            Assert.Equal(1, war.SideOf(1).Scores[Conquests.ScoreOfMaps]);
            Assert.Equal(1, war.SideOf(2).Scores[Conquests.ScoreOfMaps]);
            Assert.Equal(3, war.SideOf(1).Present);
            Assert.Equal(3, war.SideOf(2).Present);

            // A tie in the score: each tied alliance gets its minute.
            Assert.Equal(1, war.SideOf(1).Domination);
            Assert.Equal(1, war.SideOf(2).Domination);
        }

        /// <summary>Reaching the twenty together, the defence keeps its prism.</summary>
        [Fact]
        public void The_defence_wins_a_tie_at_twenty()
        {
            var war = Conquests.Start(9003, 1, 0, 7_200_000);
            var present = new[] { At(1, PrismMap, 10), At(2, OtherMap, 20) };
            long winner = 0;
            for (int minute = 1; minute <= Conquests.DominationToWin && winner == 0; minute++)
                winner = Conquests.Tick(war, present, minute * Conquests.TickMs);
            Assert.Equal(1, winner);
        }

        /// <summary>Without a member in the zone there is no minute of domination, whatever the score.</summary>
        [Fact]
        public void An_absent_alliance_does_not_dominate()
        {
            var war = Conquests.Start(9004, 1, 0, 7_200_000);
            Conquests.FightOver(war, new long[] { 2 }, new long[] { 1 });
            Conquests.Tick(war, new[] { At(1, PrismMap, 10) }, Conquests.TickMs);

            Assert.Equal(5, war.SideOf(2).Total);
            Assert.Equal(0, war.SideOf(2).Domination);
            Assert.Equal(1, war.SideOf(1).Domination);
        }

        /// <summary>An aggression won gives its points, and lost takes them away.</summary>
        [Fact]
        public void Aggressions_give_and_take_points()
        {
            var war = Conquests.Start(9005, 1, 0, 7_200_000);
            Conquests.FightOver(war, new long[] { 2 }, new long[] { 1 });
            Conquests.FightOver(war, new long[] { 2 }, new long[] { 1 });
            Conquests.FightOver(war, new long[] { 1 }, new long[] { 2 });

            Assert.Equal(2 * Conquests.PointsPerFighter - Conquests.PointsPerFighter, war.SideOf(2).Scores[Conquests.ScoreOfFights]);
            Assert.Equal(Conquests.PointsPerFighter - 2 * Conquests.PointsPerFighter, war.SideOf(1).Scores[Conquests.ScoreOfFights]);
        }

        /// <summary>The winner's priority lasts its thirty minutes; a conquered prism's invulnerability shrinks with the prisms held.</summary>
        [Fact]
        public void The_priority_and_the_invulnerability()
        {
            Conquests.GivePriority(9006, 7, 30 * 60_000);
            Assert.Equal(7, Conquests.PriorityIn(9006, 29 * 60_000)?.AllianceId);
            Assert.Null(Conquests.PriorityIn(9006, 30 * 60_000));

            Assert.Equal(24 * 3_600_000L, Conquests.InvulnerabilityMs(24, 1));
            Assert.Equal(6 * 3_600_000L, Conquests.InvulnerabilityMs(24, 4));
            Assert.Equal(0, Conquests.InvulnerabilityMs(0, 4));
        }

        // ─── The store and the frames ───────────────────────────────────────────

        /// <summary>A table of before the states gets its columns, and a state set is there after a reload.</summary>
        [Fact]
        public void The_state_is_kept_and_an_old_table_gets_it()
        {
            using (var old = new SqliteConnection($"Data Source={_file}"))
            {
                old.Open();
                var create = old.CreateCommand();
                create.CommandText = @"
                    CREATE TABLE Prisms (Id INTEGER PRIMARY KEY AUTOINCREMENT, AllianceId INTEGER NOT NULL,
                        SubAreaId INTEGER NOT NULL, MapId INTEGER NOT NULL, CellId INTEGER NOT NULL DEFAULT 0,
                        Direction INTEGER NOT NULL DEFAULT 1, PlacedUtcMs INTEGER NOT NULL DEFAULT 0);
                    INSERT INTO Prisms (AllianceId, SubAreaId, MapId, CellId, Direction, PlacedUtcMs)
                    VALUES (3, 838, 144441344, 300, 1, 1000);";
                create.ExecuteNonQuery();
            }
            PrismStore.ConnectionStringOverride = $"Data Source={_file}";

            var prism = PrismStore.InSubArea(Zone);
            Assert.NotNull(prism);
            Assert.Equal(PrismStore.Normal, prism.State);

            PrismStore.SetState(prism, PrismStore.Weakened, 123_456);
            PrismStore.ConnectionStringOverride = $"Data Source={_file}";
            var again = PrismStore.InSubArea(Zone);
            Assert.NotSame(prism, again);
            Assert.Equal(PrismStore.Weakened, again.State);
            Assert.Equal(123_456, again.NextChangeUtcMs);
        }

        /// <summary>
        /// The ley: normal, as the captures' -- f2 the placing, no f3 --; weakened, f2 when it turns
        /// vulnerable, the time the conquests window counts down, and f3 its state.
        /// </summary>
        [Fact]
        public void The_state_goes_in_the_ley()
        {
            var alliance = new AllianceStore.Alliance { Uuid = "u", Name = "N", Tag = "T" };
            var prism = new PrismStore.Prism { PlacedUtcMs = 1_784_886_505_835 };

            var normal = Fields(PrismProtocol.State(prism, alliance).Build());
            Assert.Equal(1_784_886_505_835, normal[2].VarIntValue);
            Assert.False(normal.ContainsKey(3));
            Assert.Equal(1_784_886_505, normal[4].VarIntValue);

            prism.State = PrismStore.Weakened;
            prism.NextChangeUtcMs = 1_784_972_905_835;
            var weakened = Fields(PrismProtocol.State(prism, alliance).Build());
            Assert.Equal(1_784_972_905_835, weakened[2].VarIntValue);
            Assert.Equal(PrismStore.Weakened, (int)weakened[3].VarIntValue);
            Assert.Equal(1_784_886_505, weakened[4].VarIntValue);
        }

        /// <summary>The score: each alliance's line -- its roles, minutes, three kinds of points, members -- and the conquest's clocks.</summary>
        [Fact]
        public void The_score_frame()
        {
            var war = Conquests.Start(9007, 1, 1_784_886_000_000, 1_784_893_200_000);
            Conquests.Tick(war, new[] { At(1, PrismMap, 10), At(1, OtherMap, 11) }, 1_784_886_060_000);
            var alliance = new AllianceStore.Alliance { Uuid = "uuid-1", Name = "Defensa", Tag = "DEF" };

            var mep = ProtoMessage.Parse(KothProtocol.BuildUpdate(war, id => id == 1 ? alliance : null, 8)).Fields;
            var line = ProtoMessage.Parse(mep.Single(f => f.FieldNumber == 1).BytesValue).Fields;
            Assert.Equal(1_784_886_120_000, mep.Single(f => f.FieldNumber == 2).VarIntValue);
            string began = System.Text.Encoding.UTF8.GetString(mep.Single(f => f.FieldNumber == 3).BytesValue);
            Assert.Equal(1_784_886_000_000,
                new DateTimeOffset(DateTime.Parse(began, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)).ToUnixTimeMilliseconds());

            Assert.Equal(AllianceProtocol.Block(alliance).Build(), line.Single(f => f.FieldNumber == 1).BytesValue);
            var role = ProtoMessage.Parse(line.Single(f => f.FieldNumber == 2).BytesValue).Fields;
            Assert.Equal(2, role.Single(f => f.FieldNumber == 1).VarIntValue);
            Assert.Equal(8, role.Single(f => f.FieldNumber == 2).VarIntValue);
            Assert.Equal(1, line.Single(f => f.FieldNumber == 3).VarIntValue);
            var points = line.Where(f => f.FieldNumber == 5).Select(f => ProtoMessage.Parse(f.BytesValue).Fields).ToList();
            Assert.Equal(3, points.Count);
            Assert.Equal(2, points[0].Single(f => f.FieldNumber == 1).VarIntValue);   // the maps
            Assert.DoesNotContain(points[0], f => f.FieldNumber == 4);                 // kind 0
            Assert.Equal(1, points[1].Single(f => f.FieldNumber == 4).VarIntValue);
            Assert.Equal(2, points[2].Single(f => f.FieldNumber == 4).VarIntValue);
            Assert.Equal(2, line.Single(f => f.FieldNumber == 6).VarIntValue);
        }

        private static Dictionary<int, ProtoField> Fields(byte[] bytes)
            => ProtoMessage.Parse(bytes).Fields.ToDictionary(f => f.FieldNumber);

        // ─── The whole of it ────────────────────────────────────────────────────

        /// <summary>
        /// «Defensa»'s prism beaten by «Asalto»: weakened (95 and 94), vulnerable at its hour (86,
        /// 89, both fighters aggressable), refused to a new attack (406), and conquered by «Asalto»
        /// alone in the zone for twenty minutes: the score each minute, the end (mdz), the prism
        /// gone (iew), «Asalto»'s priority (1567) and its fighter back to not aggressable.
        /// </summary>
        [Fact]
        public async Task A_prism_is_weakened_turns_vulnerable_and_is_conquered()
        {
            if (MapManager.GetMapInfo(PrismMap) == null) MapManager.Initialize();
            if (MapManager.GetMapInfo(PrismMap)?.SubAreaId != Zone) return;   // no map data

            var defence = AllianceStore.Create(CharacterA, "Defensa", "DEF", 1, 1, 1, 1, 1);
            var assault = AllianceStore.Create(CharacterB, "Asalto", "ASA", 1, 1, 1, 1, 1);
            AllianceStore.SetTakesPartInAva(CharacterA, true);
            AllianceStore.SetTakesPartInAva(CharacterB, true);
            var prism = PrismStore.Place(defence.Id, Zone, PrismMap, 300, 1);

            await using var a = await Economy.ClientPipe.OpenAsync(AccountA, CharacterA, PrismMap);
            await using var b = await Economy.ClientPipe.OpenAsync(AccountB, CharacterB, PrismMap);

            // Beaten.
            long now = 1_784_886_000_000;
            await ConquestHandler.WeakenAsync(prism, new[] { assault.Id }, now);
            long hours = Math.Max(0, ServerSettings.Current.PrismWeakenedHours);
            Assert.Equal(PrismStore.Weakened, prism.State);
            Assert.Equal(now + hours * 3_600_000L, prism.NextChangeUtcMs);
            Assert.Equal(new[] { "ifa", "jsn", "lqn" }, (await a.Take(3)).Select(f => f.Opcode));
            var bSaw = await b.Take(3);
            Assert.Equal(new[] { "ifa", "jsn", "lqn" }, bSaw.Select(f => f.Opcode));
            Assert.Equal(ConnectionProtocol.BuildInfoMessage(2, 94, Zone.ToString()), bSaw[2].Payload);

            // Weakened, it is not fought.
            using (SessionContext.Push(b.Session))
                await PrismHandler.AttackAsync(b.ToClient);
            Assert.Equal(ConnectionProtocol.BuildInfoMessage(InfoMessages.Warning, 406), await b.Next(Op.Lqn));

            // Its hour come: vulnerable, the conquest open, both aggressable.
            now += hours * 3_600_000L;
            await ConquestHandler.TickAsync(now);
            Assert.Equal(PrismStore.Vulnerable, prism.State);
            Assert.NotNull(Conquests.In(Zone));
            var aSaw = await a.Take(4);
            Assert.Equal(new[] { "ifa", "jsn", "lqn", "hon" }, aSaw.Select(f => f.Opcode));
            Assert.Equal(ConnectionProtocol.BuildInfoMessage(2, 86, Zone.ToString()), aSaw[2].Payload);
            Assert.Equal(AllianceProtocol.BuildAggressableStatus(CharacterA, AllianceProtocol.AvaAggressable), aSaw[3].Payload);
            bSaw = await b.Take(4);
            Assert.Equal(ConnectionProtocol.BuildInfoMessage(2, 89, Zone.ToString()), bSaw[2].Payload);
            Assert.Equal(AllianceProtocol.AvaAggressable, AllianceProtocol.AggressableStatus(CharacterB));

            // «Defensa» leaves; «Asalto» holds the zone twenty minutes.
            await a.DisposeAsync();
            for (int minute = 1; minute <= Conquests.DominationToWin; minute++)
            {
                await ConquestHandler.TickAsync(now + minute * Conquests.TickMs);
                var score = ProtoMessage.Parse(await b.Next(Op.Mep)).Fields;
                Assert.Equal(2, score.Count(f => f.FieldNumber == 1));
            }

            Assert.Equal(AllianceProtocol.Block(assault).Build(),
                         ProtoMessage.Parse(await b.Next(Op.Mdz)).Fields.Single(f => f.FieldNumber == 1).BytesValue);
            await b.Next(Op.Kmu);
            Assert.Equal(PrismProtocol.BuildGone(Zone), await b.Next(Op.Iew));
            Assert.Equal(ConnectionProtocol.BuildSystemMessage(1567, Zone.ToString(), "30"), await b.Next(Op.Lqn));
            Assert.Equal(AllianceProtocol.BuildAggressableStatus(CharacterB, AllianceProtocol.AvaNotAggressable), await b.Next(Op.Hon));
            Assert.Null(PrismStore.InSubArea(Zone));
            Assert.Null(Conquests.In(Zone));

            // «Asalto» alone may place there now, and its prism is born pacified.
            var (state, until) = ConquestHandler.BirthOf(defence.Id, Zone, now + 21 * Conquests.TickMs);
            Assert.Equal(PrismStore.Normal, state);
            (state, until) = ConquestHandler.BirthOf(assault.Id, Zone, now + 21 * Conquests.TickMs);
            Assert.Equal(PrismStore.Pacified, state);
            Assert.Equal(now + 21 * Conquests.TickMs
                         + Conquests.InvulnerabilityMs(ServerSettings.Current.ConquestInvulnerabilityHours, 1), until);
            Assert.Null(Conquests.PriorityIn(Zone, now + 21 * Conquests.TickMs));
        }

        /// <summary>
        /// «Agredir» (an hph without its f2) outside a conquest is refused (421); an aggression
        /// of a conquest's zone over gives its points.
        /// </summary>
        [Fact]
        public async Task Aggressions_need_a_conquest_and_count_in_it()
        {
            if (MapManager.GetMapInfo(PrismMap) == null) MapManager.Initialize();
            if (MapManager.GetMapInfo(PrismMap)?.SubAreaId != Zone) return;

            var defence = AllianceStore.Create(CharacterA, "Defensa", "DEF", 1, 1, 1, 1, 1);
            var assault = AllianceStore.Create(CharacterB, "Asalto", "ASA", 1, 1, 1, 1, 1);
            AllianceStore.SetTakesPartInAva(CharacterA, true);
            AllianceStore.SetTakesPartInAva(CharacterB, true);
            PrismStore.Place(defence.Id, Zone, PrismMap, 300, 1);

            await using var a = await Economy.ClientPipe.OpenAsync(AccountA, CharacterA, PrismMap);
            await using var b = await Economy.ClientPipe.OpenAsync(AccountB, CharacterB, PrismMap);
            using (SessionContext.Push(b.Session))
                await ChallengeDuelHandler.OfferAsync(b.ToClient, ConnectionProtocol.Push(Op.Hph, Pb.New().Var(1, CharacterA).Build()));
            Assert.Equal(ConnectionProtocol.BuildInfoMessage(InfoMessages.Warning, 421), await b.Next(Op.Lqn));
            await a.Quiet();

            var war = Conquests.Start(Zone, defence.Id, 0, 7_200_000);
            var fight = new FightInstance(1, PrismMap) { Reglas = FightRules.Agresion };
            fight.Azul.Add(new Fighter { Id = CharacterB });
            fight.Rojo.Add(new Fighter { Id = CharacterA });
            await ConquestHandler.AggressionEndedAsync(fight, attackersWon: true);
            Assert.Equal(Conquests.PointsPerFighter, war.SideOf(assault.Id).Scores[Conquests.ScoreOfFights]);
            Assert.Equal(-Conquests.PointsPerFighter, war.SideOf(defence.Id).Scores[Conquests.ScoreOfFights]);
        }

        /// <summary>The aggression's rules: the kam's type 1, the usual clock, nothing to win, a defeat that costs.</summary>
        [Fact]
        public void The_aggression_rules()
        {
            var rules = FightRules.Agresion;
            Assert.Equal(1, rules.TipoDelKam);
            Assert.Equal(450, rules.RelojDeColocacion);
            Assert.False(rules.HayRetos);
            Assert.False(rules.ReparteBotin);
            Assert.False(rules.EnfrenteHayMonstruos);
            Assert.True(rules.DefeatCosts);
        }
    }
}

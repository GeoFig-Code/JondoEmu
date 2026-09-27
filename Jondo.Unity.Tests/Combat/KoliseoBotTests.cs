using System;
using System.Linq;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.World.Fights;
using Jondo.Unity.World.Maps;
using Xunit;

namespace Jondo.Unity.Tests.Combat
{
    /// <summary>
    /// The Koliseo's megabots: the fourth card of the window, the megabot's sheet as asked for,
    /// and a turn thought out with its class's own spells for every class.
    /// </summary>
    [Collection("koliseo")]
    public class KoliseoBotTests
    {
        private const int Centre = 300;

        private static int CellAt(int from, int distance)
        {
            foreach (int cell in Enumerable.Range(0, 560))
                if (MapGeometry.Distance(from, cell) == distance) return cell;
            return -1;
        }

        [Fact]
        public void The_fourth_card_is_an_open_1v1_against_megabots()
        {
            var mode = Assert.Single(KoliseoHandler.Modes, m => m.Index == KoliseoHandler.MegabotMode);
            Assert.Equal((1, true, false), (mode.TeamSize, mode.Open, mode.Inner));

            // Its entry: mode 3, open, not a default mode, a 1v1, running for the season.
            var ltd = ProtoMessage.Parse(KoliseoHandler.BuildModes(KoliseoHandler.Modes));
            var entry = ltd.Fields.Where(f => f.FieldNumber == 1).Select(f => ProtoMessage.Parse(f.BytesValue)).Last();
            Assert.Equal(3, entry.Fields.Single(f => f.FieldNumber == 1).VarIntValue);
            Assert.Equal(1, entry.Fields.Single(f => f.FieldNumber == 3).VarIntValue);
            var settings = ProtoMessage.Parse(entry.Fields.Single(f => f.FieldNumber == 2).BytesValue);
            Assert.DoesNotContain(settings.Fields, f => f.FieldNumber == 1);
            Assert.Equal(1, settings.Fields.Single(f => f.FieldNumber == 4).VarIntValue);
            var season = KoliseoLadder.Current();
            string start = System.Text.Encoding.UTF8.GetString(settings.Fields.Single(f => f.FieldNumber == 2).BytesValue);
            Assert.Equal(season.StartUtc, DateTime.Parse(start, null, System.Globalization.DateTimeStyles.AdjustToUniversal));
            Assert.Single(settings.Fields, f => f.FieldNumber == 3);
        }

        [Fact]
        public void A_megabot_is_level_200_with_12_ap_6_mp_1500_everywhere_and_6666_life()
        {
            var spec = KoliseoBots.Create(8);
            try
            {
                Assert.True(KoliseoBots.IsBot(spec.Id));
                Assert.Equal("Megabot Yopuka", spec.Name);
                var bot = KoliseoBots.BuildFighter(spec);
                Assert.True(bot.IsBot);
                Assert.False(bot.IsMonster);
                Assert.True(bot.IsReady);
                Assert.Equal((200, 12, 6, 6666, 6666), (bot.Level, bot.MaxAP, bot.MaxMP, bot.MaxHP, bot.CurrentHP));
                Assert.Equal((1500, 1500, 1500, 1500), (bot.Strength, bot.Intelligence, bot.Chance, bot.Agility));
                Assert.NotEmpty(bot.BotLook!);
                // A level-200 Yopuka's spells, one of each pair, every one at its highest grade.
                Assert.True(bot.SpellIds.Count >= 20);
                foreach (int spell in bot.SpellIds)
                    Assert.Equal(SpellTable.GradeFor(spell, 200), bot.SpellGrades[spell]);
                var pairs = SpellTable.PairsOf(8);
                Assert.All(pairs, pair => Assert.True(bot.SpellIds.Count(s => pair.Holds(s)) <= 1));
            }
            finally { KoliseoBots.Forget(spec.Id); }
        }

        /// <summary>For every class, the megabot finds something worth doing to an enemy in reach.</summary>
        [Fact]
        public void Every_class_of_megabot_plays_its_turn_with_its_spells()
        {
            foreach (int breed in SpellTable.ClassBreeds)
            {
                var spec = KoliseoBots.Create(breed);
                try
                {
                    var bot = KoliseoBots.BuildFighter(spec);
                    bot.TeamId = 1;
                    bot.CellId = Centre;
                    var enemy = new Fighter
                    {
                        Id = 1, TeamId = 0, CellId = CellAt(Centre, 3), MaxHP = 3000, CurrentHP = 3000,
                        CurrentAP = 11, CurrentMP = 6, Level = 200,
                    };
                    var spells = FightHandler.TacticsOf(bot);
                    Assert.True(spells.Count > 0, $"class {breed} has no spell the tactics can weigh");
                    var action = MonsterTactics.Next(new MonsterTactics.Board { Fighters = new[] { bot, enemy } }, bot, spells);
                    Assert.True(action != null, $"a megabot of class {breed} does nothing");
                }
                finally { KoliseoBots.Forget(spec.Id); }
            }
        }
    }
}

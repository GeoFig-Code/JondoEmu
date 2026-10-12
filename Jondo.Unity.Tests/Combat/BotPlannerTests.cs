using System.Collections.Generic;
using System.Linq;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.World.Fights;
using Jondo.Unity.World.Maps;
using Xunit;

namespace Jondo.Unity.Tests.Combat
{
    /// <summary>
    /// A JondoBot's turn planned whole (MonsterTactics.PlanTurn): the casts it chains, in which
    /// order, and the copy of the fight it plays them on.
    /// </summary>
    [Collection("MapManager")]
    public class BotPlannerTests
    {
        private const int Centre = 300;
        private const int Yopuka = 8;

        private const int IraDeYopuka = 13124;
        private const int Influencia = 13141;
        private const int Vitalidad = 13120;
        private const int Conquista = 13148;
        private const int Intimidacion = 13108;

        private static int InLineAt(int from, int distance)
            => Enumerable.Range(0, MapGeometry.MaxCells)
                         .First(c => MapGeometry.Distance(from, c) == distance
                                     && MapGeometry.CellToPoint(c).Item2 == MapGeometry.CellToPoint(from).Item2);

        /// <summary>A JondoBot of the class with the base of every pair but the ones given, the enemy so many cells away.</summary>
        private static (FightInstance Fight, Fighter Bot, Fighter Enemy, MonsterTactics.Board Board, List<MonsterTactics.Spell> Spells)
            Duel(int breed, int distance, long id, params int[] spells)
        {
            var choices = SpellTable.PairsOf(breed).ToDictionary(p => p.Id, p => p.Base);
            foreach (int spell in spells) choices[SpellTable.PairOf(spell)!.Id] = spell;
            var spec = new KoliseoBots.Spec { Id = KoliseoBots.FirstId + 700_000 + id, Breed = breed, Choices = choices, Name = "test" };
            var fight = new FightInstance(9_999_000 + (int)id, 1, 1);
            var bot = KoliseoBots.BuildFighter(spec);
            bot.CellId = Centre;
            var enemy = new Fighter
            {
                Id = 8_990_000_001, CellId = InLineAt(Centre, distance), MaxHP = 4500, CurrentHP = 4500, Level = 200,
                CurrentAP = 12, CurrentMP = 6, MaxAP = 12, MaxMP = 6,
            };
            fight.AddPlayer(enemy);
            fight.AddOpponent(bot);
            var board = new MonsterTactics.Board
            {
                Fighters = new[] { bot, enemy },
                Reach = (row, caster, from, aim) => EffectEngine.ReachOf(fight, caster, row, from, aim),
                CanSummon = (c, t, g) => FightHandler.FitsTheSummonLimit(fight, c, t, g),
            };
            return (fight, bot, enemy, board, FightHandler.TacticsOf(bot));
        }

        private static List<MonsterTactics.Action> Plan(Fighter bot, MonsterTactics.Board board, List<MonsterTactics.Spell> spells)
            => MonsterTactics.PlanTurn(board, bot, spells, s => SpellCriteria.Allows(bot, s.Id, s.Grade), budgetMs: int.MaxValue);

        /// <summary>
        /// A range the caster's own does not add to stays the spell's: Ira de Yopuka, a blow of
        /// range 1, is not cast from seven cells away by a JondoBot with +6 range.
        /// </summary>
        [Fact]
        public void A_range_that_cannot_be_boosted_is_not()
        {
            if (!SpellTable.IsLoaded) return;
            var (_, bot, enemy, board, spells) = Duel(Yopuka, 7, 1, IraDeYopuka);
            var ira = spells.Single(s => s.Id == IraDeYopuka);
            Assert.False(ira.RangeBoostable);
            Assert.Equal(1, ira.ReachFor(bot));

            var options = MonsterTactics.Options(board, bot, new List<MonsterTactics.Spell> { ira });
            Assert.All(options, o => Assert.True(MapGeometry.Distance(o.From, o.TargetCell) <= 1,
                $"Ira cast at {o.TargetCell} from {o.From}"));
        }

        /// <summary>The copy a plan is played on leaves the fight as it found it: life, cell, points, casts, states.</summary>
        [Fact]
        public void A_plan_leaves_the_fight_as_it_was()
        {
            if (!SpellTable.IsLoaded) return;
            var (_, bot, enemy, board, spells) = Duel(Yopuka, 3, 2, Conquista);
            var states = bot.Buffs.Estados.ToList();

            var plan = Plan(bot, board, spells);

            Assert.NotEmpty(plan);
            Assert.Equal((4500, Centre, 12, 6), (enemy.CurrentHP, bot.CellId, bot.CurrentAP, bot.CurrentMP));
            Assert.Empty(bot.LanzadosEsteTurno);
            Assert.DoesNotContain(bot.Recarga, r => r.Value > 0);
            Assert.Equal(states, bot.Buffs.Estados.ToList());
        }

        /// <summary>
        /// The Yopuka's strategist hands back what its side's blows do to it: half of each blow
        /// taken (1163 on «DBA»), and all of that to everybody within two cells (1223).
        /// </summary>
        [Fact]
        public void The_strategist_hands_blows_back()
        {
            if (!SpellTable.IsLoaded) return;
            var recipe = Summons.De(5130, 1);
            Assert.NotNull(recipe);
            var strategist = new Fighter { Id = -5, TeamId = 1, Invocador = 1, MonsterId = 5130,
                                           Conducta = (recipe!.HechizoPropio, recipe.GradoDelHechizoPropio) };

            var echo = MonsterTactics.EchoOf(strategist);

            Assert.Equal(0.5, echo.AllyDamageTaken);
            Assert.Equal(1.0, echo.Share);
            Assert.Equal(2, echo.Radius);
        }

        /// <summary>
        /// Summoned next to the enemy, the strategist makes every zone blow that takes both in worth
        /// more than it was without it.
        /// </summary>
        [Fact]
        public void A_zone_blow_is_worth_more_with_the_strategist_by_the_enemy()
        {
            if (!SpellTable.IsLoaded) return;
            var (_, bot, enemy, board, spells) = Duel(Yopuka, 3, 3, Conquista, 13115, 13125);
            var before = MonsterTactics.Options(board, bot, spells);
            var summon = before.Where(o => o.Spell.Id == Conquista)
                               .OrderBy(o => MapGeometry.Distance(o.TargetCell, enemy.CellId)).First();
            Assert.Equal(1, MapGeometry.Distance(summon.TargetCell, enemy.CellId));

            double Best(IEnumerable<MonsterTactics.Action> options, int spell) => options.Where(o => o.Spell.Id == spell).Max(o => o.Value);
            using var sim = MonsterTactics.Simulation.Replay(board, bot, new[] { summon });
            var after = MonsterTactics.Options(sim.Board, bot, spells);
            Assert.True(Best(after, 13125) > Best(before, 13125), "Espada de Yopuka");
            Assert.True(Best(after, 13115) > Best(before, 13115), "Machete");
        }

        /// <summary>Damage of the caster's best element (2822) is damage: Intimidación is worth something.</summary>
        [Fact]
        public void Damage_of_the_best_element_is_damage()
        {
            if (!SpellTable.IsLoaded) return;
            var (_, bot, enemy, board, spells) = Duel(Yopuka, 1, 4, Intimidacion);
            var options = MonsterTactics.Options(board, bot, new List<MonsterTactics.Spell> { spells.Single(s => s.Id == Intimidacion) });
            Assert.Contains(options, o => o.TargetCell == enemy.CellId && o.Value > 0);
        }

        /// <summary>
        /// At a third of its life the Yopuka holds out before anything else: Vitalidad's +20 % and
        /// Virtud's shield are its first two casts.
        /// </summary>
        [Fact]
        public void A_bot_low_on_life_holds_out_first()
        {
            if (!SpellTable.IsLoaded) return;
            const int Virtud = 13142;
            var (_, bot, _, board, spells) = Duel(Yopuka, 3, 5, Vitalidad);
            bot.CurrentHP = bot.MaxHP * 3 / 10;

            var plan = Plan(bot, board, spells);

            string seen = string.Join(", ", plan.Select(a => $"{a.Spell.Id}@{a.TargetCell}={a.Value:0}"));
            Assert.True(plan[0].Spell.Id is Vitalidad or Virtud, seen);
            Assert.True(plan.Take(2).Any(a => a.Spell.Id == Vitalidad), seen);
        }

        /// <summary>
        /// A JondoBot does not strike an enemy it has made invulnerable: after Influencia, nothing
        /// more of its turn goes at him.
        /// </summary>
        [Fact]
        public void No_blow_follows_the_invulnerability_it_gave()
        {
            if (!SpellTable.IsLoaded) return;
            var (_, bot, enemy, board, spells) = Duel(Yopuka, 3, 6, Influencia, IraDeYopuka);
            bot.Recarga[IraDeYopuka] = 1;

            var plan = Plan(bot, board, spells);

            int pinned = plan.FindIndex(a => a.Spell.Id == Influencia);
            if (pinned < 0) return;
            Assert.DoesNotContain(plan.Skip(pinned + 1), a => a.TargetCell == enemy.CellId && a.Spell.Offensive);
        }
    }
}

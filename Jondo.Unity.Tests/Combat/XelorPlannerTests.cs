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
    /// A Xelor JondoBot's turn: the copy of the fight its plans are played on telefrags, gives the
    /// telefrag's AP and takes the Xelor back as the engine does, and the plan puts the Cómplice
    /// where its teleport will throw the enemy.
    /// </summary>
    [Collection("MapManager")]
    public class XelorPlannerTests
    {
        private const int Centre = 300;
        private const int Xelor = 5;

        private const int Complice = 13287;
        private const int Perturbacion = 13288;
        private const int GolpeDeXelor = 13252;
        private const int Rebobinamiento = 13243;
        private const int Aguja = 13244;
        private const int Teletransportacion = 13249;

        /// <summary>The enemy's state after a telefrag (251), and the Xelor's once-a-turn mark of Perturbación's +2 AP (6112).</summary>
        private const int EnemyTelefrag = 251;

        private static int InLineAt(int from, int distance)
            => Enumerable.Range(0, MapGeometry.MaxCells)
                         .First(c => MapGeometry.Distance(from, c) == distance
                                     && MapGeometry.CellToPoint(c).Item2 == MapGeometry.CellToPoint(from).Item2);

        private sealed record Duel(FightInstance Fight, Fighter Bot, Fighter Enemy, MonsterTactics.Board Board,
                                   List<MonsterTactics.Spell> Spells);

        /// <summary>A Xelor JondoBot with Teletransportación, Aguja and Rebobinamiento, the enemy so many cells away.</summary>
        private static Duel Make(int distance, long id)
        {
            var choices = SpellTable.PairsOf(Xelor).ToDictionary(p => p.Id, p => p.Base);
            foreach (int spell in new[] { Teletransportacion, Aguja, Rebobinamiento })
                choices[SpellTable.PairOf(spell)!.Id] = spell;
            var spec = new KoliseoBots.Spec { Id = KoliseoBots.FirstId + 720_000 + id, Breed = Xelor, Choices = choices, Name = "test" };
            var fight = new FightInstance(9_998_000 + (int)id, 1, 1);
            var bot = KoliseoBots.BuildFighter(spec);
            bot.CellId = Centre;
            bot.CasillaAlEmpezarTurno = Centre;
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
                Telefrags = () => fight.Telefrags,
            };
            return new Duel(fight, bot, enemy, board, FightHandler.TacticsOf(bot));
        }

        private static MonsterTactics.Action Cast(Duel duel, int spell, int aim, int from = Centre)
        {
            var tactic = duel.Spells.Single(s => s.Id == spell);
            var target = duel.Board.Fighters.FirstOrDefault(f => f.IsAlive && f.CellId == aim) ?? duel.Bot;
            return new MonsterTactics.Action(new List<int> { from }, tactic, target, aim, 0, 0,
                                             duel.Bot.CurrentAP, duel.Bot.CurrentMP);
        }

        /// <summary>Golpe de Xelor sends the enemy across the Xelor and its blow still lands on him, read where he stood as it began.</summary>
        [Fact]
        public void Golpe_de_Xelor_strikes_whom_it_sent_across()
        {
            if (!SpellTable.IsLoaded) return;
            var duel = Make(2, 1);
            int was = duel.Enemy.CellId;

            using var sim = MonsterTactics.Simulation.Replay(duel.Board, duel.Bot, new[] { Cast(duel, GolpeDeXelor, was) });

            Assert.Equal(MapGeometry.Reflejar(was, Centre), duel.Enemy.CellId);
            Assert.True(duel.Enemy.CurrentHP < 4500);
        }

        /// <summary>
        /// The Cómplice where Perturbación throws the enemy: they swap, a telefrag -- the enemy's
        /// Telefrag state and the Xelor's +2 AP; the same spell again telefrags again, and gives no
        /// AP: once per spell and turn, by a state its first cast left.
        /// </summary>
        [Fact]
        public void A_telefrag_gives_its_AP_once_per_spell()
        {
            if (!SpellTable.IsLoaded) return;
            var duel = Make(2, 2);
            int enemyAt = duel.Enemy.CellId, across = MapGeometry.Reflejar(enemyAt, Centre);
            using var sim = MonsterTactics.Simulation.Replay(duel.Board, duel.Bot, new[] { Cast(duel, Complice, across) });
            int ap = duel.Bot.CurrentAP;

            sim.Play(Cast(duel, Perturbacion, enemyAt));
            Assert.Equal(across, duel.Enemy.CellId);
            Assert.True(duel.Enemy.Buffs.TieneEstado(EnemyTelefrag));
            Assert.Equal(ap - 3 + 2, duel.Bot.CurrentAP);

            sim.Play(Cast(duel, Perturbacion, across));
            Assert.Equal(enemyAt, duel.Enemy.CellId);
            Assert.Equal(ap - 3 + 2 - 3, duel.Bot.CurrentAP);
        }

        /// <summary>Rebobinamiento cast on itself takes the Xelor back to where its turn began when the turn ends, and the copy takes it back again.</summary>
        [Fact]
        public void Rebobinamiento_takes_the_Xelor_back_as_its_turn_ends()
        {
            if (!SpellTable.IsLoaded) return;
            var duel = Make(6, 3);
            int walkedTo = InLineAt(Centre, 2);
            using var sim = MonsterTactics.Simulation.Replay(duel.Board, duel.Bot, new[] { Cast(duel, Rebobinamiento, walkedTo, walkedTo) });
            Assert.Equal(walkedTo, duel.Bot.CellId);
            int mark = sim.Mark();

            sim.EndTurn();
            Assert.Equal(Centre, duel.Bot.CellId);

            sim.Rewind(mark);
            Assert.Equal(walkedTo, duel.Bot.CellId);
        }

        /// <summary>
        /// Aguja on a telefragged enemy is a blow to come: its hook deals it when he loses the
        /// state, which he will. The turn's threat counts it.
        /// </summary>
        [Fact]
        public void Aguja_on_a_telefragged_enemy_counts_its_blow_to_come()
        {
            if (!SpellTable.IsLoaded) return;
            var duel = Make(2, 4);
            int enemyAt = duel.Enemy.CellId, across = MapGeometry.Reflejar(enemyAt, Centre);
            using var sim = MonsterTactics.Simulation.Replay(duel.Board, duel.Bot, new[]
            {
                Cast(duel, Complice, across), Cast(duel, Perturbacion, enemyAt),
            });
            Assert.True(duel.Enemy.Buffs.TieneEstado(EnemyTelefrag));
            double before = sim.Pending(duel.Bot);

            sim.Play(Cast(duel, Aguja, duel.Enemy.CellId));

            Assert.Equal(0, before);
            Assert.True(sim.Pending(duel.Bot) > 0);
        }

        /// <summary>
        /// The plan puts the Cómplice where one of the Xelor's teleports will throw the enemy, and
        /// throws him there: the enemy ends the turn telefragged, and the turn spends more AP than
        /// the twelve it began with.
        /// </summary>
        [Fact]
        public void A_Xelor_bot_summons_where_it_will_telefrag()
        {
            if (!SpellTable.IsLoaded) return;
            var duel = Make(2, 5);

            var plan = MonsterTactics.PlanTurn(duel.Board, duel.Bot, duel.Spells, s => SpellCriteria.Allows(duel.Bot, s.Id, s.Grade), budgetMs: int.MaxValue);

            string seen = string.Join(", ", plan.Select(a => $"{a.Spell.Id}@{a.TargetCell}<{a.From}"));
            Assert.True(plan.Sum(a => a.Spell.Cost) > 12, seen);
            using var sim = MonsterTactics.Simulation.Replay(duel.Board, duel.Bot, plan);
            Assert.Contains(sim.Board.Fighters, f => f.EsInvocado && f.Buffs.TieneEstado(244));
        }
    }
}

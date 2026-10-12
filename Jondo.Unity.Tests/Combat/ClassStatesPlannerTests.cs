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
    /// The class mechanics a JondoBot's plan plays on its copy of the fight beyond the Xelor's,
    /// the Pandawa's and the Tymador's: the Zobal's masks, one at a time; the Sadida's tree turned
    /// into a doll; and a mask naming several templates, any of them.
    /// </summary>
    [Collection("MapManager")]
    public class ClassStatesPlannerTests
    {
        private const int Centre = 300;

        private static int InLineAt(int from, int distance)
            => Enumerable.Range(0, MapGeometry.MaxCells)
                         .First(c => MapGeometry.Distance(from, c) == distance
                                     && MapGeometry.CellToPoint(c).Item2 == MapGeometry.CellToPoint(from).Item2);

        private sealed record Duel(Fighter Bot, Fighter Enemy, MonsterTactics.Board Board, List<MonsterTactics.Spell> Spells);

        /// <summary>A JondoBot of the class with the base of every pair, its attitudes cast as the fight begins.</summary>
        private static Duel Make(int breed, long id)
        {
            var choices = SpellTable.PairsOf(breed).ToDictionary(p => p.Id, p => p.Base);
            var spec = new KoliseoBots.Spec { Id = KoliseoBots.FirstId + 770_000 + id, Breed = breed, Choices = choices, Name = "test" };
            var fight = new FightInstance(9_998_900 + (int)id, 1, 1);
            var bot = KoliseoBots.BuildFighter(spec);
            bot.CellId = Centre;
            var enemy = new Fighter
            {
                Id = 8_990_000_001, CellId = InLineAt(Centre, 4), MaxHP = 4500, CurrentHP = 4500, Level = 200,
                CurrentAP = 12, CurrentMP = 6, MaxAP = 12, MaxMP = 6,
            };
            fight.AddPlayer(enemy);
            fight.AddOpponent(bot);
            foreach (int attitude in bot.Buffs.Actitudes.ToList())
                EffectEngine.Resolver(fight, bot, attitude, bot.Buffs.GradoDeActitud(attitude), bot, EffectEngine.AlLanzar, 1,
                                      celdaApuntada: bot.CellId);
            bot.CurrentAP = bot.MaxAP;
            bot.CurrentMP = bot.MaxMP;
            var board = new MonsterTactics.Board
            {
                Fighters = new[] { bot, enemy },
                Reach = (row, caster, from, aim) => EffectEngine.ReachOf(fight, caster, row, from, aim),
                CanSummon = (c, t, g) => FightHandler.FitsTheSummonLimit(fight, c, t, g),
            };
            return new Duel(bot, enemy, board, FightHandler.TacticsOf(bot));
        }

        private static MonsterTactics.Action Cast(Duel duel, MonsterTactics.Board board, int spell, int aim)
        {
            var tactic = duel.Spells.Single(s => s.Id == spell);
            var target = board.Fighters.FirstOrDefault(f => f.IsAlive && f.CellId == aim) ?? duel.Bot;
            return new MonsterTactics.Action(new List<int> { Centre }, tactic, target, aim, 0, 0, duel.Bot.CurrentAP, duel.Bot.CurrentMP);
        }

        /// <summary>
        /// A Zobal wears one mask: putting on another takes the one he wore off, three sub-spells
        /// down (18634 at grades 1, 2 and 5) -- the copy follows them that deep.
        /// </summary>
        [Fact]
        public void A_Zobal_wears_one_mask_at_a_time()
        {
            if (!SpellTable.IsLoaded) return;
            const int Classic = 98, Psychopath = 99, Coward = 100;
            var duel = Make(14, 1);
            Assert.True(duel.Bot.Buffs.TieneEstado(Classic));

            using var sim = MonsterTactics.Simulation.Replay(duel.Board, duel.Bot, new[] { Cast(duel, duel.Board, 13388, Centre) });
            Assert.True(duel.Bot.Buffs.TieneEstado(Psychopath));
            Assert.False(duel.Bot.Buffs.TieneEstado(Classic));

            sim.Play(Cast(duel, sim.Board, 13387, Centre));
            Assert.True(duel.Bot.Buffs.TieneEstado(Coward));
            Assert.False(duel.Bot.Buffs.TieneEstado(Psychopath));
        }

        /// <summary>
        /// The Sadida's dolls are trees turned into them (405, «mata al objetivo y lo sustituye»):
        /// on the copy, Árbol and then La Loca on it leave the doll where the tree stood.
        /// </summary>
        [Fact]
        public void A_Sadida_turns_its_tree_into_a_doll()
        {
            if (!SpellTable.IsLoaded) return;
            const int Arbol = 13519, LaLoca = 13564, Tree = 5894, Doll = 5896;
            var duel = Make(10, 2);
            int cell = InLineAt(Centre, 2);
            using var sim = MonsterTactics.Simulation.Replay(duel.Board, duel.Bot, new[] { Cast(duel, duel.Board, Arbol, cell) });
            Assert.Contains(sim.Board.Fighters, f => f.IsAlive && f.MonsterId == Tree && f.CellId == cell);
            Assert.True(MonsterTactics.ValueOfRows(sim.Board, duel.Spells.Single(s => s.Id == LaLoca), duel.Bot, Centre, cell,
                                                   new List<Fighter> { duel.Enemy }) > 0);

            sim.Play(Cast(duel, sim.Board, LaLoca, cell));

            Assert.DoesNotContain(sim.Board.Fighters, f => f.IsAlive && f.MonsterId == Tree);
            Assert.Contains(sim.Board.Fighters, f => f.IsAlive && f.MonsterId == Doll && f.CellId == cell);
        }

        /// <summary>Several F in a mask are several templates, any of them: the Tymador's bombs.</summary>
        [Fact]
        public void A_mask_of_several_templates_takes_any_of_them()
        {
            var tymador = new Fighter { Id = 1, TeamId = 1 };
            var bomb = new Fighter { Id = 2, TeamId = 1, IsMonster = true, MonsterId = 3113, Invocador = 1 };
            var other = new Fighter { Id = 3, TeamId = 1, IsMonster = true, MonsterId = 9999, Invocador = 1 };

            Assert.True(EffectEngine.CumpleLaMascara(tymador, bomb, "a,P,F3112,F3113,F3114,F5161"));
            Assert.False(EffectEngine.CumpleLaMascara(tymador, other, "a,P,F3112,F3113,F3114,F5161"));
            Assert.False(EffectEngine.CumpleLaMascara(tymador, bomb, "a,f3113"));
        }
    }
}

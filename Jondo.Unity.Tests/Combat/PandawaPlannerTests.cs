using System.Collections.Generic;
using System.Linq;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.World.Combat;
using Jondo.Unity.World.Fights;
using Jondo.Unity.World.Maps;
using Xunit;

namespace Jondo.Unity.Tests.Combat
{
    /// <summary>
    /// A Pandawa JondoBot's turn: sober and drunk, and carrying and throwing, on the copy of the
    /// fight its plans are played on and in what it may cast.
    /// </summary>
    [Collection("MapManager")]
    public class PandawaPlannerTests
    {
        private const int Centre = 300;
        private const int Pandawa = 12;

        private const int Sober = 3531;
        private const int Drunk = 498;

        private const int Karcham = 12787;
        private const int Propulsion = 12788;
        private const int PalmaExplosiva = 12781;
        private const int Fermentacion = 12819;

        private static int InLineAt(int from, int distance)
            => Enumerable.Range(0, MapGeometry.MaxCells)
                         .First(c => MapGeometry.Distance(from, c) == distance
                                     && MapGeometry.CellToPoint(c).Item2 == MapGeometry.CellToPoint(from).Item2);

        private sealed record Duel(FightInstance Fight, Fighter Bot, Fighter Enemy, MonsterTactics.Board Board,
                                   List<MonsterTactics.Spell> Spells);

        /// <summary>
        /// A Pandawa JondoBot with the base of every pair, the enemy so many cells away, and its
        /// attitudes cast as the fight begins (FightHandler, the cascade of passives): sober.
        /// </summary>
        private static Duel Make(int distance, long id)
        {
            var choices = SpellTable.PairsOf(Pandawa).ToDictionary(p => p.Id, p => p.Base);
            var spec = new KoliseoBots.Spec { Id = KoliseoBots.FirstId + 740_000 + id, Breed = Pandawa, Choices = choices, Name = "test" };
            var fight = new FightInstance(9_998_500 + (int)id, 1, 1);
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
                Telefrags = () => fight.Telefrags,
            };
            return new Duel(fight, bot, enemy, board, FightHandler.TacticsOf(bot));
        }

        private static MonsterTactics.Action Cast(Duel duel, int spell, int aim)
        {
            var tactic = duel.Spells.Single(s => s.Id == spell);
            var target = duel.Board.Fighters.FirstOrDefault(f => f.IsAlive && f.CellId == aim && !f.EstaCargado) ?? duel.Bot;
            return new MonsterTactics.Action(new List<int> { duel.Bot.CellId }, tactic, target, aim, 0, 0,
                                             duel.Bot.CurrentAP, duel.Bot.CurrentMP);
        }

        /// <summary>
        /// Portador keeps its bearer from casting (preventsSpellCast) but for the spells that ask for
        /// it: carrying, the Pandawa may throw -- Karcham, Propulsión -- and nothing else.
        /// </summary>
        [Fact]
        public void Carrying_only_the_throws_may_be_cast()
        {
            if (!SpellTable.IsLoaded) return;
            var duel = Make(1, 1);
            Assert.True(SpellCriteria.Allows(duel.Bot, PalmaExplosiva, 3));
            Assert.False(SpellCriteria.Allows(duel.Bot, Propulsion, 3));

            duel.Bot.Buffs.PonerEstado(EffectSupport.CarryingState);

            Assert.False(SpellCriteria.Allows(duel.Bot, PalmaExplosiva, 3));
            Assert.True(SpellCriteria.Allows(duel.Bot, Propulsion, 3));
            Assert.True(SpellCriteria.Allows(duel.Bot, Karcham, 3));
        }

        /// <summary>A state that silences its bearer lets no spell through that does not ask for it.</summary>
        [Fact]
        public void A_silenced_fighter_casts_nothing()
        {
            if (!SpellTable.IsLoaded) return;
            var silenced = new Fighter { Id = 77 };
            silenced.Buffs.PonerEstado(41);

            Assert.False(SpellCriteria.Allows(silenced, PalmaExplosiva, 3));
            Assert.True(SpellCriteria.Requires("HS=3|(HS=3531&HS!8)", 3));
            Assert.False(SpellCriteria.Requires("HS=3531&HS!3", 3));
        }

        /// <summary>
        /// On the copy of the fight, Karcham picks the enemy up -- onto the Pandawa's cell, Carrying
        /// and Carried -- and Propulsión throws him onto the aimed cell and strikes him there.
        /// </summary>
        [Fact]
        public void The_copy_carries_and_throws()
        {
            if (!SpellTable.IsLoaded) return;
            var duel = Make(1, 2);
            using var sim = MonsterTactics.Simulation.Replay(duel.Board, duel.Bot, new[] { Cast(duel, Karcham, duel.Enemy.CellId) });
            Assert.Equal(duel.Enemy.Id, duel.Bot.Carrying);
            Assert.Equal(Centre, duel.Enemy.CellId);
            Assert.True(duel.Bot.Buffs.TieneEstado(EffectSupport.CarryingState));
            Assert.True(duel.Enemy.Buffs.TieneEstado(EffectSupport.CarriedState));

            int landing = InLineAt(Centre, 3);
            sim.Play(Cast(duel, Propulsion, landing));

            Assert.Equal(landing, duel.Enemy.CellId);
            Assert.Equal(0, duel.Bot.Carrying);
            Assert.False(duel.Bot.Buffs.TieneEstado(EffectSupport.CarryingState));
            Assert.True(duel.Enemy.CurrentHP < 4500);
        }

        /// <summary>
        /// Comilona, played on the copy, makes the sober Pandawa drunk: the states its sub-spell
        /// takes and puts, and the drunk spells it opens.
        /// </summary>
        [Fact]
        public void Comilona_makes_the_sober_drunk()
        {
            if (!SpellTable.IsLoaded) return;
            var duel = Make(3, 3);
            Assert.True(duel.Bot.Buffs.TieneEstado(Sober));
            Assert.False(SpellCriteria.Allows(duel.Bot, Fermentacion, 3));

            using var sim = MonsterTactics.Simulation.Replay(duel.Board, duel.Bot, new[] { Cast(duel, 12804, Centre) });

            Assert.False(duel.Bot.Buffs.TieneEstado(Sober));
            Assert.True(duel.Bot.Buffs.TieneEstado(Drunk));
            Assert.True(SpellCriteria.Allows(duel.Bot, Fermentacion, 3));
        }

        /// <summary>
        /// Low on life next to the enemy, the sober Pandawa gets drunk to cast Fermentación, which
        /// only the drunk may: the switch is worth nothing alone, and what it opens is.
        /// </summary>
        [Fact]
        public void Low_on_life_the_Pandawa_gets_drunk_for_Fermentacion()
        {
            if (!SpellTable.IsLoaded) return;
            var duel = Make(1, 4);
            duel.Bot.CurrentHP = duel.Bot.MaxHP * 35 / 100;

            var plan = MonsterTactics.PlanTurn(duel.Board, duel.Bot, duel.Spells, s => SpellCriteria.Allows(duel.Bot, s.Id, s.Grade), budgetMs: int.MaxValue);

            string seen = string.Join(", ", plan.Select(a => $"{a.Spell.Id}@{a.TargetCell}"));
            Assert.True(plan.Any(a => a.Spell.Id == Fermentacion), seen);
        }
    }
}

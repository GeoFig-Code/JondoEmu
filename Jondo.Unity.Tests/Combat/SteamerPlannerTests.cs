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
    /// A Steamer JondoBot's turrets on the copy of the fight its plans are played on: evolved as
    /// the engine evolves them, one of each kind, and worth what they will do and give.
    /// </summary>
    [Collection("MapManager")]
    public class SteamerPlannerTests
    {
        private const int Centre = 300;
        private const int Steamer = 15;

        private const int Evolucion = 13832;
        private const int Arponeras = 13829;
        private const int Arponera = 5836;
        private const int EvolutionII = 134, EvolutionIII = 135;

        private static int InLineAt(int from, int distance)
            => Enumerable.Range(0, MapGeometry.MaxCells)
                         .First(c => MapGeometry.Distance(from, c) == distance
                                     && MapGeometry.CellToPoint(c).Item2 == MapGeometry.CellToPoint(from).Item2);

        private sealed record Duel(FightInstance Fight, Fighter Bot, Fighter Enemy, Fighter Turret,
                                   MonsterTactics.Board Board, List<MonsterTactics.Spell> Spells);

        /// <summary>A Steamer JondoBot, the enemy five cells away, and an Arponera of his put down two cells off, as the fight makes it.</summary>
        private static Duel Make(long id, bool variants = false)
        {
            // Evolución kept either way: Emboscada is a variant, Evolución a base.
            var choices = SpellTable.PairsOf(Steamer).ToDictionary(p => p.Id, p => variants ? p.Variant : p.Base);
            foreach (var pair in SpellTable.PairsOf(Steamer).Where(p => p.Base == Evolucion || p.Variant == Evolucion))
                choices[pair.Id] = Evolucion;
            var spec = new KoliseoBots.Spec { Id = KoliseoBots.FirstId + 790_000 + id, Breed = Steamer, Choices = choices, Name = "test" };
            var fight = new FightInstance(9_999_300 + (int)id, 1, 1);
            var bot = KoliseoBots.BuildFighter(spec);
            bot.CellId = Centre;
            var enemy = new Fighter
            {
                Id = 8_990_000_001, CellId = InLineAt(Centre, 5), MaxHP = 4500, CurrentHP = 4500, Level = 200,
                CurrentAP = 12, CurrentMP = 6, MaxAP = 12, MaxMP = 6,
            };
            fight.AddPlayer(enemy);
            fight.AddOpponent(bot);
            var recipe = Summons.De(Arponera, 3)!;
            var turret = new Fighter { Id = -500 - id, IsMonster = true, MonsterId = Arponera, CellId = InLineAt(Centre, 2) };
            Summons.Characteristics(turret, recipe, bot);
            turret.Conducta = (recipe.HechizoPropio, recipe.GradoDelHechizoPropio);
            fight.Invocar(turret, bot);
            turret.TeamId = bot.TeamId;
            turret.Invocador = bot.Id;
            var board = new MonsterTactics.Board
            {
                Fighters = new[] { bot, enemy, turret },
                Reach = (row, caster, from, aim) => EffectEngine.ReachOf(fight, caster, row, from, aim),
                CanSummon = (c, t, g) => FightHandler.FitsTheSummonLimit(fight, c, t, g),
                SpellsOf = FightHandler.TacticsOf,
            };
            return new Duel(fight, bot, enemy, turret, board, FightHandler.TacticsOf(bot));
        }

        private static MonsterTactics.Action Cast(Duel duel, int spell, int aim)
        {
            var tactic = duel.Spells.Single(s => s.Id == spell);
            var target = duel.Board.Fighters.FirstOrDefault(f => f.IsAlive && f.CellId == aim) ?? duel.Bot;
            return new MonsterTactics.Action(new List<int> { Centre }, tactic, target, aim, 0, 0, duel.Bot.CurrentAP, duel.Bot.CurrentMP);
        }

        /// <summary>
        /// Evolución takes the turret to II and then to III on the copy, as in the engine: the
        /// evolution passes through the turret back to the Steamer, who casts it at the turret
        /// (1017, «at the parent's caster») -- read as «at the candidate», it evolved the Steamer.
        /// </summary>
        [Fact]
        public void Evolucion_evolves_the_turret_as_the_engine_does()
        {
            if (!SpellTable.IsLoaded) return;
            var engine = Make(1);
            EffectEngine.Resolver(engine.Fight, engine.Bot, Evolucion, 3, engine.Turret, EffectEngine.AlLanzar, 1,
                                  celdaApuntada: engine.Turret.CellId);
            Assert.True(engine.Turret.Buffs.TieneEstado(EvolutionII));

            var copy = Make(2);
            using var sim = MonsterTactics.Simulation.Replay(copy.Board, copy.Bot, new[] { Cast(copy, Evolucion, copy.Turret.CellId) });
            Assert.Equal(engine.Turret.Buffs.Estados.OrderBy(s => s), copy.Turret.Buffs.Estados.OrderBy(s => s));
            Assert.False(copy.Bot.Buffs.TieneEstado(EvolutionII));

            copy.Turret.Buffs.QuitarEstado(6094);
            sim.Play(Cast(copy, Evolucion, copy.Turret.CellId));
            Assert.True(copy.Turret.Buffs.TieneEstado(EvolutionIII));
            Assert.False(copy.Turret.Buffs.TieneEstado(EvolutionII));
        }

        /// <summary>
        /// One turret of each kind: the copy's new Arponera casts its behaviour spell as it comes
        /// out, and the one already out goes, as in the fight.
        /// </summary>
        [Fact]
        public void A_new_turret_replaces_the_one_of_its_kind()
        {
            if (!SpellTable.IsLoaded) return;
            var duel = Make(3);
            int elsewhere = InLineAt(Centre, 3);

            using var sim = MonsterTactics.Simulation.Replay(duel.Board, duel.Bot, new[] { Cast(duel, Arponeras, elsewhere) });

            Assert.False(duel.Turret.IsAlive);
            Assert.Single(sim.Board.Fighters, f => f.IsAlive && f.MonsterId == Arponera);
        }

        /// <summary>
        /// Emboscada marks the enemy and has every offensive turret of the Steamer's at III cast its
        /// special at whoever carries the mark: the Arponera's Armada, three sub-casts down. Weighed
        /// with the mark on for what the spell casts after it -- as the engine resolves a sub-cast --
        /// and played on the copy, it is worth more, and deals more, with the turret at III.
        /// </summary>
        [Fact]
        public void Emboscada_has_the_turret_at_III_strike_too()
        {
            if (!SpellTable.IsLoaded) return;
            const int Emboscada = 13835;
            (double Worth, int Dealt) Ambush(long id, bool atIII)
            {
                var duel = Make(id, variants: true);
                // The turret's behaviour spell, cast as it comes out: the state its grade reads (3497).
                var recipe = Summons.De(Arponera, 3)!;
                EffectEngine.Resolver(duel.Fight, duel.Turret, recipe.HechizoPropio, recipe.GradoDelHechizoPropio, duel.Turret,
                                      EffectEngine.AlLanzar, 1, celdaApuntada: duel.Turret.CellId);
                if (atIII)
                    for (int i = 0; i < 2; i++)
                    {
                        EffectEngine.Resolver(duel.Fight, duel.Bot, Evolucion, 3, duel.Turret, EffectEngine.AlLanzar, 1,
                                              celdaApuntada: duel.Turret.CellId);
                        duel.Turret.Buffs.QuitarEstado(6094);
                    }
                var spell = duel.Spells.Single(s => s.Id == Emboscada);
                double worth = MonsterTactics.ValueOfRows(duel.Board, spell, duel.Bot, Centre, duel.Enemy.CellId,
                                                          new List<Fighter> { duel.Enemy });
                using var sim = MonsterTactics.Simulation.Replay(duel.Board, duel.Bot, new[] { Cast(duel, Emboscada, duel.Enemy.CellId) });
                return (worth, 4500 - duel.Enemy.CurrentHP);
            }

            var atI = Ambush(5, atIII: false);
            var atIII = Ambush(6, atIII: true);

            Assert.True(atIII.Worth > atI.Worth, $"worth {atI.Worth:0} -> {atIII.Worth:0}");
            Assert.True(atIII.Dealt > atI.Dealt, $"dealt {atI.Dealt} -> {atIII.Dealt}");
        }

        /// <summary>
        /// An evolved turret hits harder on its turn and gives its Steamer damage at its start: the
        /// turn that evolves it is worth more, and the plan does it.
        /// </summary>
        [Fact]
        public void Evolving_the_turret_is_worth_it_and_planned()
        {
            if (!SpellTable.IsLoaded) return;
            var duel = Make(4);
            var spells = duel.Spells;
            double before, after;
            using (var sim = MonsterTactics.Simulation.Replay(duel.Board, duel.Bot, new MonsterTactics.Action[0]))
            {
                before = MonsterTactics.Threat(sim, duel.Bot, spells);
                sim.Play(Cast(duel, Evolucion, duel.Turret.CellId));
                after = MonsterTactics.Threat(sim, duel.Bot, spells);
            }
            Assert.True(after > before + 100, $"threat {before:0} -> {after:0}");

            // Without the clock: a cold first plan runs out of its two seconds before the beam is through.
            var plan = MonsterTactics.PlanTurn(duel.Board, duel.Bot, spells, s => SpellCriteria.Allows(duel.Bot, s.Id, s.Grade),
                                               budgetMs: int.MaxValue);
            string seen = string.Join(", ", plan.Select(a => $"{a.Spell.Id}@{a.TargetCell}"));
            Assert.True(plan.Any(a => a.Spell.Id == Evolucion && a.TargetCell == duel.Turret.CellId), seen);
        }
    }
}

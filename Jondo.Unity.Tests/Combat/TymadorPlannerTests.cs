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
    /// A Tymador JondoBot's bombs on the copy of the fight its plans are played on: put down as
    /// the fight makes them, set off by Detonador, worth what their combo has grown.
    /// </summary>
    [Collection("MapManager")]
    public class TymadorPlannerTests
    {
        private const int Centre = 300;
        private const int Tymador = 13;

        private const int Explobomba = 13444;
        private const int Detonador = 13432;
        private const int ExplobombaTemplate = 3112;

        private static int InLineAt(int from, int distance)
            => Enumerable.Range(0, MapGeometry.MaxCells)
                         .First(c => MapGeometry.Distance(from, c) == distance
                                     && MapGeometry.CellToPoint(c).Item2 == MapGeometry.CellToPoint(from).Item2);

        private sealed record Duel(Fighter Bot, Fighter Enemy, MonsterTactics.Board Board, List<MonsterTactics.Spell> Spells);

        /// <summary>The same, with an Explobomba of the bot's already out on the fight, as the fight makes it.</summary>
        private static Duel WithABombOut(long id, int bombCell)
        {
            var duel = Make(id);
            var recipe = Summons.De(ExplobombaTemplate, 3)!;
            var bomb = new Fighter { Id = -950 - id, IsMonster = true, MonsterId = ExplobombaTemplate, CellId = bombCell };
            Summons.Characteristics(bomb, recipe, duel.Bot);
            bomb.TeamId = duel.Bot.TeamId;
            bomb.Invocador = duel.Bot.Id;
            var board = new MonsterTactics.Board
            {
                Fighters = duel.Board.Fighters.Concat(new[] { bomb }).ToList(),
                Reach = duel.Board.Reach,
                CanSummon = duel.Board.CanSummon,
            };
            return duel with { Board = board };
        }

        private static Duel Make(long id)
        {
            var choices = SpellTable.PairsOf(Tymador).ToDictionary(p => p.Id, p => p.Base);
            var spec = new KoliseoBots.Spec { Id = KoliseoBots.FirstId + 750_000 + id, Breed = Tymador, Choices = choices, Name = "test" };
            var fight = new FightInstance(9_998_700 + (int)id, 1, 1);
            var bot = KoliseoBots.BuildFighter(spec);
            bot.CellId = Centre;
            var enemy = new Fighter
            {
                Id = 8_990_000_001, CellId = InLineAt(Centre, 5), MaxHP = 4500, CurrentHP = 4500, Level = 200,
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
            return new Duel(bot, enemy, board, FightHandler.TacticsOf(bot));
        }

        private static MonsterTactics.Action Cast(Duel duel, MonsterTactics.Board board, int spell, int aim)
        {
            var tactic = duel.Spells.Single(s => s.Id == spell);
            var target = board.Fighters.FirstOrDefault(f => f.IsAlive && f.CellId == aim) ?? duel.Bot;
            return new MonsterTactics.Action(new List<int> { Centre }, tactic, target, aim, 0, 0, duel.Bot.CurrentAP, duel.Bot.CurrentMP);
        }

        /// <summary>
        /// The bomb the copy puts down is the fight's: its grade and its characteristics from its
        /// recipe and its summoner. And Detonador on it, next to the enemy, has the bomb cast its
        /// explosion: the enemy takes the blow.
        /// </summary>
        [Fact]
        public void Detonador_sets_the_bomb_off_on_the_enemy()
        {
            if (!SpellTable.IsLoaded) return;
            var duel = Make(1);
            int nextToHim = InLineAt(Centre, 4);
            using var sim = MonsterTactics.Simulation.Replay(duel.Board, duel.Bot, new[] { Cast(duel, duel.Board, Explobomba, nextToHim) });
            var bomb = sim.Board.Fighters.Single(f => f.MonsterId == ExplobombaTemplate);
            var recipe = Summons.De(ExplobombaTemplate, bomb.GradeIndex);
            Assert.NotNull(recipe);
            Assert.True(bomb.GradeIndex > 1);
            Assert.Equal(Summons.VidaDelInvocado(recipe!.Vida, duel.Bot.StatLevel, recipe.VidaFija), bomb.MaxHP);

            sim.Play(Cast(duel, sim.Board, Detonador, nextToHim));

            Assert.True(duel.Enemy.CurrentHP < 4500);
        }

        /// <summary>
        /// A bomb that has grown its combo blows up harder, as the fight deals it: worth more to set
        /// off -- put down by the enemy and out of the Tymador's own radius.
        /// </summary>
        [Fact]
        public void A_grown_bomb_is_worth_more_to_set_off()
        {
            if (!SpellTable.IsLoaded) return;
            var duel = Make(2);
            int nextToHim = InLineAt(Centre, 4);
            using var sim = MonsterTactics.Simulation.Replay(duel.Board, duel.Bot, new[] { Cast(duel, duel.Board, Explobomba, nextToHim) });
            var bomb = sim.Board.Fighters.Single(f => f.MonsterId == ExplobombaTemplate);
            var detonador = duel.Spells.Single(s => s.Id == Detonador);
            double Worth() => MonsterTactics.ValueOfRows(sim.Board, detonador, duel.Bot, Centre, nextToHim, new List<Fighter> { duel.Enemy });
            double combo1 = Worth();

            var ladder = Combo.Ladder();
            bomb.Buffs.PonerEstado(ladder[5]);
            double combo6 = Worth();
            bomb.Buffs.QuitarEstado(ladder[5]);

            Assert.True(combo6 > combo1, $"combo I {combo1:0}, combo VI {combo6:0}");
        }

        /// <summary>
        /// Two bombs of a kind in line, two to seven cells apart, raise a wall between them
        /// (BombWalls): laid on the far side of the enemy, the second one walls him in -- the wall
        /// hits him as it is raised, and will again as his turn begins inside it.
        /// </summary>
        [Fact]
        public void A_bomb_laid_beyond_the_enemy_walls_him_in()
        {
            if (!SpellTable.IsLoaded) return;
            var duel = Make(3);
            using var sim = MonsterTactics.Simulation.Replay(duel.Board, duel.Bot, new[] { Cast(duel, duel.Board, Explobomba, InLineAt(Centre, 2)) });
            Assert.Equal(4500, duel.Enemy.CurrentHP);
            Assert.Equal(0, sim.WallsAtTheirTurnStart(duel.Bot));

            double hooks = sim.HookDamage;
            sim.Play(Cast(duel, sim.Board, Explobomba, InLineAt(Centre, 6)));

            Assert.True(duel.Enemy.CurrentHP < 4500);
            Assert.True(sim.HookDamage > hooks);
            Assert.True(sim.WallsAtTheirTurnStart(duel.Bot) > 0);
        }

        /// <summary>«Si una bomba está unida a otras por un muro y explota, hará explotar también a las otras bombas del muro.»</summary>
        [Fact]
        public void Setting_off_a_walled_bomb_sets_off_the_wall()
        {
            if (!SpellTable.IsLoaded) return;
            var duel = Make(4);
            using var sim = MonsterTactics.Simulation.Replay(duel.Board, duel.Bot, new[]
            {
                Cast(duel, duel.Board, Explobomba, InLineAt(Centre, 2)),
            });
            sim.Play(Cast(duel, sim.Board, Explobomba, InLineAt(Centre, 6)));
            var bombs = sim.Board.Fighters.Where(f => f.MonsterId == ExplobombaTemplate).ToList();
            Assert.Equal(2, bombs.Count(b => b.IsAlive));

            sim.Play(Cast(duel, sim.Board, Detonador, InLineAt(Centre, 2)));

            Assert.All(bombs, b => Assert.False(b.IsAlive));
        }

        /// <summary>A wall hits with half the combo of the bombs holding it up, the highest.</summary>
        [Fact]
        public void A_grown_wall_hits_harder()
        {
            if (!SpellTable.IsLoaded) return;
            int Wall(long id, bool grown)
            {
                var duel = Make(id);
                using var sim = MonsterTactics.Simulation.Replay(duel.Board, duel.Bot, new[] { Cast(duel, duel.Board, Explobomba, InLineAt(Centre, 2)) });
                if (grown) sim.Board.Fighters.Single(f => f.MonsterId == ExplobombaTemplate).Buffs.PonerEstado(Combo.Ladder()[5]);
                sim.Play(Cast(duel, sim.Board, Explobomba, InLineAt(Centre, 6)));
                return 4500 - duel.Enemy.CurrentHP;
            }

            Assert.True(Wall(5, grown: true) > Wall(6, grown: false));
        }

        /// <summary>
        /// With a bomb out in line with the enemy, the plan lays the next one beyond him: the
        /// bomb's options take the cells that wall him in, and the wall is worth its blows.
        /// </summary>
        [Fact]
        public void With_a_bomb_out_the_plan_walls_the_enemy_in()
        {
            if (!SpellTable.IsLoaded) return;
            var duel = WithABombOut(7, InLineAt(Centre, 2));
            var plan = MonsterTactics.PlanTurn(duel.Board, duel.Bot, duel.Spells, s => SpellCriteria.Allows(duel.Bot, s.Id, s.Grade),
                                               budgetMs: int.MaxValue);

            string seen = string.Join(", ", plan.Select(a => $"{a.Spell.Id}@{a.TargetCell}"));
            var (ex, ey) = MapGeometry.CellToPoint(duel.Enemy.CellId);
            int bx = MapGeometry.CellToPoint(InLineAt(Centre, 2)).X;
            // In the same row as the bomb and the enemy, the enemy between the two.
            Assert.True(plan.Any(a => a.Spell.Id == Explobomba && MapGeometry.CellToPoint(a.TargetCell).Y == ey
                                      && System.Math.Sign(MapGeometry.CellToPoint(a.TargetCell).X - ex) == -System.Math.Sign(bx - ex)), seen);
        }
    }
}

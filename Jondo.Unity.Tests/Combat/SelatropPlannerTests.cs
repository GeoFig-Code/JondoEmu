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
    /// A Selatrop JondoBot's portals: what is cast at one goes through the network and lands
    /// beyond its way out, harder; the copy of the fight lays them, walks, pushes and sends
    /// through them; the plan uses them.
    /// </summary>
    [Collection("MapManager")]
    public class SelatropPlannerTests
    {
        private const int Centre = 300;
        private const int Selatrop = 16;
        private const int Portal = 14574;
        private const int Estela = 14591;
        private const int Estupor = 14585;
        private const int Exilio = 14609;
        private const int Sermon = 14613;

        private static int InLineAt(int from, int distance)
            => Enumerable.Range(0, MapGeometry.MaxCells)
                         .First(c => MapGeometry.Distance(from, c) == distance
                                     && MapGeometry.CellToPoint(c).Item2 == MapGeometry.CellToPoint(from).Item2);

        private sealed record Duel(FightInstance Fight, Fighter Bot, Fighter Enemy, MonsterTactics.Board Board,
                                   List<MonsterTactics.Spell> Spells, int Entry, int Exit);

        /// <summary>
        /// A Selatrop JondoBot, the enemy eight cells away, and -- when asked -- two portals of his
        /// out: one next to him, the other next to the enemy, so that a spell cast at the first
        /// from where he stands comes out on the enemy. The bases of his pairs, but for the
        /// <paramref name="variants"/> asked for.
        /// </summary>
        private static Duel Make(long id, bool portals, params int[] variants)
        {
            var choices = SpellTable.PairsOf(Selatrop).ToDictionary(p => p.Id, p => variants.Contains(p.Variant) ? p.Variant : p.Base);
            var spec = new KoliseoBots.Spec { Id = KoliseoBots.FirstId + 830_000 + id, Breed = Selatrop, Choices = choices, Name = "test" };
            var fight = new FightInstance(9_999_500 + (int)id, 1, 1);
            var bot = KoliseoBots.BuildFighter(spec);
            bot.CellId = Centre;
            var enemy = new Fighter
            {
                Id = 8_990_000_001, CellId = InLineAt(Centre, 8), MaxHP = 4500, CurrentHP = 4500, Level = 200,
                CurrentAP = 12, CurrentMP = 6, MaxAP = 12, MaxMP = 6,
            };
            fight.AddPlayer(enemy);
            fight.AddOpponent(bot);
            var (bx, by) = MapGeometry.CellToPoint(Centre);
            var (ex, ey) = MapGeometry.CellToPoint(enemy.CellId);
            int entry = MapGeometry.PointToCell(bx, by + 1), exit = MapGeometry.PointToCell(ex, ey - 1);
            if (portals)
            {
                fight.Portales.Add(new Portal { Id = 1, Owner = bot.Id, Cell = entry, DiceNum = 2, LayingSpell = Portal, Active = true });
                fight.Portales.Add(new Portal { Id = 2, Owner = bot.Id, Cell = exit, DiceNum = 2, LayingSpell = Portal, Active = true });
            }
            var board = new MonsterTactics.Board
            {
                Fighters = new[] { bot, enemy },
                Reach = (row, caster, from, aim) => EffectEngine.ReachOf(fight, caster, row, from, aim),
                CanSummon = (c, t, g) => FightHandler.FitsTheSummonLimit(fight, c, t, g),
                SpellsOf = FightHandler.TacticsOf,
                Portals = () => fight.Portales,
                ThroughPortal = on => fight.CastThroughPortal = on,
            };
            return new Duel(fight, bot, enemy, board, FightHandler.TacticsOf(bot), entry, exit);
        }

        private static MonsterTactics.Action Cast(Duel duel, MonsterTactics.Board board, int spell, int aim)
        {
            var tactic = duel.Spells.Single(s => s.Id == spell);
            var target = board.Fighters.FirstOrDefault(f => f.IsAlive && f.CellId == aim) ?? duel.Bot;
            return new MonsterTactics.Action(new List<int> { Centre }, tactic, target, aim, 0, 0, duel.Bot.CurrentAP, duel.Bot.CurrentMP);
        }

        /// <summary>The strongest of the bot's blows that cannot reach the enemy eight cells away by itself.</summary>
        private static MonsterTactics.Spell ShortBlow(Duel duel)
            => duel.Spells.Where(s => s.Offensive && s.HasRows && s.ThroughPortals && s.ReachFor(duel.Bot) < 8 && s.ReachFor(duel.Bot) >= 1)
                          .OrderByDescending(s => s.Damage).First();

        /// <summary>
        /// Aimed at a portal, a spell goes through the network as the fight projects it: the vector
        /// from the caster to the portal, laid from the way out -- here onto the enemy --, and the
        /// bonus of the cells between the two portals.
        /// </summary>
        [Fact]
        public void A_spell_aimed_at_a_portal_lands_beyond_the_way_out()
        {
            if (!SpellTable.IsLoaded) return;
            var duel = Make(1, portals: true);
            var spell = ShortBlow(duel);

            var through = MonsterTactics.ThroughPortals(duel.Board, spell, duel.Bot, Centre, duel.Entry);

            Assert.NotNull(through);
            Assert.Equal(duel.Enemy.CellId, through!.Value.Landing);
            Assert.Equal(duel.Exit, through.Value.Exit);
            Assert.Equal(2 * MapGeometry.Distance(duel.Entry, duel.Exit), through.Value.Bonus);
            var none = Make(2, portals: false);
            Assert.Null(MonsterTactics.ThroughPortals(none.Board, spell, none.Bot, Centre, duel.Entry));
        }

        /// <summary>The copy plays it so: the blow cast at the portal lands on the enemy eight cells off.</summary>
        [Fact]
        public void The_copy_strikes_through_the_portals()
        {
            if (!SpellTable.IsLoaded) return;
            var duel = Make(3, portals: true);
            var spell = ShortBlow(duel);

            using var sim = MonsterTactics.Simulation.Replay(duel.Board, duel.Bot, new[] { Cast(duel, duel.Board, spell.Id, duel.Entry) });

            Assert.True(duel.Enemy.CurrentHP < 4500);
            Assert.False(duel.Fight.CastThroughPortal);
        }

        /// <summary>
        /// The copy lays a portal as the fight does, on a network of its own: the first alone is
        /// off, the second turns both on; the fight's network is never touched.
        /// </summary>
        [Fact]
        public void The_copy_lays_portals_and_the_second_makes_the_network()
        {
            if (!SpellTable.IsLoaded) return;
            var duel = Make(4, portals: false);
            using var sim = MonsterTactics.Simulation.Replay(duel.Board, duel.Bot, new[] { Cast(duel, duel.Board, Portal, duel.Entry) });
            var network = sim.Board.Portals!();
            Assert.Single(network.All);
            Assert.False(network.All[0].Active);

            duel.Bot.LanzadosEsteTurno.Clear();
            sim.Play(Cast(duel, sim.Board, Portal, InLineAt(Centre, 4)));

            network = sim.Board.Portals!();
            Assert.Equal(2, network.All.Count);
            Assert.All(network.All, p => Assert.True(p.Active));
            Assert.Equal(0, duel.Fight.Portales.Count);
        }

        /// <summary>
        /// With its network out and the enemy beyond its reach, the plan strikes through it: a blow
        /// aimed at one of its portals -- the two it has, or one it lays to make the chain longer.
        /// </summary>
        [Fact]
        public void With_a_network_out_the_plan_strikes_through_it()
        {
            if (!SpellTable.IsLoaded) return;
            var duel = Make(5, portals: true);

            var plan = MonsterTactics.PlanTurn(duel.Board, duel.Bot, duel.Spells, s => SpellCriteria.Allows(duel.Bot, s.Id, s.Grade),
                                               budgetMs: int.MaxValue);

            var portals = new HashSet<int> { duel.Entry, duel.Exit };
            portals.UnionWith(plan.Where(a => a.Spell.LaysPortals).Select(a => a.TargetCell));
            string seen = string.Join(", ", plan.Select(a => $"{a.Spell.Id}@{a.TargetCell}"));
            Assert.True(plan.Any(a => a.Spell.Offensive && portals.Contains(a.TargetCell)), seen);
        }

        /// <summary>
        /// Its portals all on its own side and the enemy across the map, as in the Koliseo fight of
        /// 12/10/2026: the plan lays a way out by the enemy and strikes through the network. It
        /// struck him straight, turn after turn, and laid its portals by itself.
        /// </summary>
        [Fact]
        public void With_its_portals_on_its_side_the_plan_lays_a_way_out_by_the_enemy_and_strikes_through()
        {
            if (!SpellTable.IsLoaded) return;
            var duel = Make(12, portals: false, Exilio, Sermon, 14610, 14590);
            duel.Bot.CellId = 270;
            duel.Enemy.CellId = 396;
            int id = 1;
            foreach (int cell in new[] { 176, 148, 233 })
                duel.Fight.Portales.Add(new Portal { Id = id++, Owner = duel.Bot.Id, Cell = cell, DiceNum = 2, Value = 3, LayingSpell = Portal, Active = true });

            var plan = MonsterTactics.PlanTurn(duel.Board, duel.Bot, duel.Spells, s => SpellCriteria.Allows(duel.Bot, s.Id, s.Grade),
                                               budgetMs: int.MaxValue);

            string seen = string.Join(", ", plan.Select(a => $"{a.Spell.Id}@{a.TargetCell}<{a.From}"));
            var laid = plan.Where(a => a.Spell.LaysPortals).Select(a => a.TargetCell).ToList();
            Assert.True(laid.Any(c => MapGeometry.Distance(c, 396) <= 2), seen);
            var portals = laid.Concat(new[] { 176, 148, 233 }).ToHashSet();
            Assert.True(plan.Any(a => a.Spell.Offensive && portals.Contains(a.TargetCell)), seen);
        }

        /// <summary>
        /// A walk onto a portal that is on stops there and the network takes him on: the route
        /// comes out of the way out, for the one MP of the step onto the portal.
        /// </summary>
        [Fact]
        public void A_walk_onto_a_portal_comes_out_of_its_way_out()
        {
            if (!SpellTable.IsLoaded) return;
            var duel = Make(6, portals: true);

            var routes = MonsterTactics.Routes(duel.Board, duel.Bot);

            Assert.True(routes.TryGetValue(duel.Exit, out var route));
            Assert.Equal(new[] { Centre, duel.Entry, duel.Exit }, route!.Path);
            Assert.Equal(duel.Bot.CurrentMP - 1, route.MovementPoints);
            Assert.Equal(1, route.Steps);
            Assert.Equal(0, route.LostToTackles(duel.Bot));
            Assert.False(routes.ContainsKey(duel.Entry));
        }

        /// <summary>
        /// Estela lays a portal under the caster and jumps to the aim: the portal he left is on,
        /// in a network of the copy's own.
        /// </summary>
        [Fact]
        public void Estela_leaves_a_portal_where_the_caster_stood()
        {
            if (!SpellTable.IsLoaded) return;
            var duel = Make(7, portals: true);
            int aim = InLineAt(Centre, 2);

            using var sim = MonsterTactics.Simulation.Replay(duel.Board, duel.Bot, new[] { Cast(duel, duel.Board, Estela, aim) });

            Assert.Equal(aim, duel.Bot.CellId);
            var left = sim.Board.Portals!().At(Centre);
            Assert.NotNull(left);
            Assert.True(left!.Active);
            Assert.Equal(2, duel.Fight.Portales.Count);
        }

        /// <summary>Estupor lays a portal under each and swaps the two.</summary>
        [Fact]
        public void Estupor_swaps_the_two_onto_portals()
        {
            if (!SpellTable.IsLoaded) return;
            var duel = Make(8, portals: false, Estupor);
            int there = InLineAt(Centre, 3);
            duel.Enemy.CellId = there;

            using var sim = MonsterTactics.Simulation.Replay(duel.Board, duel.Bot, new[] { Cast(duel, duel.Board, Estupor, there) });

            Assert.Equal(there, duel.Bot.CellId);
            Assert.Equal(Centre, duel.Enemy.CellId);
            var network = sim.Board.Portals!();
            Assert.NotNull(network.At(Centre));
            Assert.NotNull(network.At(there));
        }

        /// <summary>Exilio lays a portal under the target and sends him through the network.</summary>
        [Fact]
        public void Exilio_sends_the_target_through_the_network()
        {
            if (!SpellTable.IsLoaded) return;
            var duel = Make(9, portals: true, Exilio);
            int there = InLineAt(Centre, 3);
            duel.Enemy.CellId = there;

            using var sim = MonsterTactics.Simulation.Replay(duel.Board, duel.Bot, new[] { Cast(duel, duel.Board, Exilio, there) });

            Assert.NotEqual(there, duel.Enemy.CellId);
            var network = sim.Board.Portals!();
            Assert.NotNull(network.At(there));
            Assert.Contains(duel.Enemy.CellId, new[] { duel.Entry, duel.Exit });
            Assert.True(network.At(there)!.Used);
        }

        /// <summary>
        /// Pushed onto a portal that is on, one goes through the network and what was left of
        /// the push goes on from its way out, the same way.
        /// </summary>
        [Fact]
        public void A_push_onto_a_portal_goes_on_from_its_way_out()
        {
            if (!SpellTable.IsLoaded) return;
            var duel = Make(10, portals: false, Sermon);
            var (bx, by) = MapGeometry.CellToPoint(Centre);
            int next = MapGeometry.PointToCell(bx, by + 1), behind = MapGeometry.PointToCell(bx, by + 2);
            int exit = MapGeometry.PointToCell(bx + 8, by);
            duel.Fight.Portales.Add(new Portal { Id = 1, Owner = duel.Bot.Id, Cell = behind, DiceNum = 2, LayingSpell = Portal, Active = true });
            duel.Fight.Portales.Add(new Portal { Id = 2, Owner = duel.Bot.Id, Cell = exit, DiceNum = 2, LayingSpell = Portal, Active = true });
            duel.Enemy.CellId = next;

            using var sim = MonsterTactics.Simulation.Replay(duel.Board, duel.Bot, new[] { Cast(duel, duel.Board, Sermon, next) });

            Assert.True(MapGeometry.Distance(duel.Enemy.CellId, exit) <= 1, $"the enemy is on {duel.Enemy.CellId}");
            Assert.All(sim.Board.Portals!().All, p => Assert.True(p.Used));
            Assert.All(duel.Fight.Portales.All, p => Assert.False(p.Used));
        }

        /// <summary>
        /// With the enemy beyond its walk and its blows, the plan goes through its network: walks
        /// through it, or strikes through it from the cell that lands the blow on him.
        /// </summary>
        [Fact]
        public void The_plan_goes_through_the_network_to_an_enemy_beyond_its_reach()
        {
            if (!SpellTable.IsLoaded) return;
            var duel = Make(11, portals: false, Exilio, Sermon);
            var (bx, by) = MapGeometry.CellToPoint(Centre);
            duel.Enemy.CellId = MapGeometry.PointToCell(bx + 9, by);
            duel.Fight.Portales.Add(new Portal { Id = 1, Owner = duel.Bot.Id, Cell = MapGeometry.PointToCell(bx, by + 2), DiceNum = 2, LayingSpell = Portal, Active = true });
            duel.Fight.Portales.Add(new Portal { Id = 2, Owner = duel.Bot.Id, Cell = MapGeometry.PointToCell(bx + 8, by), DiceNum = 2, LayingSpell = Portal, Active = true });

            var plan = MonsterTactics.PlanTurn(duel.Board, duel.Bot, duel.Spells, s => SpellCriteria.Allows(duel.Bot, s.Id, s.Grade),
                                               budgetMs: int.MaxValue);

            string seen = string.Join(", ", plan.Select(a => $"{a.Spell.Id}@{a.TargetCell} by {string.Join(">", a.Path)}"));
            var portals = duel.Fight.Portales.All.Select(p => p.Cell).ToHashSet();
            Assert.True(plan.Any(a => a.Path.Zip(a.Path.Skip(1)).Any(s => MapGeometry.Distance(s.First, s.Second) > 1)
                                      || (a.Spell.Offensive && portals.Contains(a.TargetCell))), seen);
        }
    }
}

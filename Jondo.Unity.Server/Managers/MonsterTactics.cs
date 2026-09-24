using System;
using System.Collections.Generic;
using System.Linq;
using Jondo.Unity.World.Fights;
using Jondo.Unity.World.Maps;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// A monster's turn, thought out: what is worth doing, from where, to whom -- and where to
    /// stand when it is done.
    /// </summary>
    /// <remarks>
    /// The turn it replaces walked once toward the nearest enemy and then went down its spell
    /// list in the order of its sheet, casting the first that reached as often as it could and
    /// giving up on any that was out of range or out of sight. So monsters repeated one spell,
    /// stood still behind a pillar, and never moved after attacking.
    ///
    /// This one weighs every spell it can pay for, against every target it can reach, from every
    /// cell its movement points can take it to, and takes the best -- again and again while it
    /// has points. What a spell is worth:
    ///
    ///   damage   the blow as the fight would roughly deal it, against the target's resistance,
    ///            with the zone's other enemies; a kill is worth half the victim's life on top,
    ///            and the weaker the victim the more it is worth -- they all go for the same one
    ///   heal     the life it gives back to the most wounded of its side, if any is wounded
    ///   debuff   AP and MP taken from an enemy that still has them
    ///   buff     once a turn, and only when nothing better is at hand
    ///   summon   once a turn, when a cell next to it is free
    ///
    /// divided by what it costs, and a little less for every cell it has to walk. The fight's
    /// own limits apply: per turn, per target, and the cooldown.
    ///
    /// When it has nothing left worth casting it places itself: a ranged one at the reach of its
    /// best spell and as far from the enemy as that allows, a melee one next to the weakest enemy
    /// it can reach -- where it locks -- and one with little life left and no heal, as far away
    /// as it can get.
    /// </remarks>
    public static class MonsterTactics
    {
        /// <summary>One of the monster's spells, as the tactics see it.</summary>
        public sealed class Spell
        {
            public int Id { get; init; }
            public int Grade { get; init; }
            public int Cost { get; init; }
            public int MinRange { get; init; }
            public int MaxRange { get; init; }
            public bool NeedsLineOfSight { get; init; }
            public bool InLine { get; init; }
            public int PerTurn { get; init; }
            public int PerTarget { get; init; }

            /// <summary>Average base damage of the spell's blow to enemies, and its element.</summary>
            public double Damage { get; init; }
            public ElementType Element { get; init; }

            /// <summary>The cells around the aimed one the blow also reaches: 0 for one cell.</summary>
            public int Zone { get; init; }

            /// <summary>Whether its blow also hurts the caster's side inside the zone.</summary>
            public bool HurtsAllies { get; init; }

            /// <summary>Life given back to one of its side.</summary>
            public double Heal { get; init; }

            /// <summary>AP and MP taken from an enemy.</summary>
            public int Removal { get; init; }

            /// <summary>Points of characteristics given to one of its side.</summary>
            public int Buff { get; init; }

            public bool Summons { get; init; }

            /// <summary>
            /// What a spell of pure mechanics is worth -- a state, a glyph, a teleport, a sub-cast,
            /// nothing that hurts or heals: the boss's own moves, which scored nothing and were
            /// never cast. Cast once a turn, on the enemy when its rows are for enemies
            /// (<see cref="UtilityOnEnemies"/>), on itself otherwise.
            /// </summary>
            public double Utility { get; init; }

            /// <summary>The level asks for a free cell: a leap lands NEXT to its target, never on him.</summary>
            public bool NeedsFreeCell { get; init; }
            public bool UtilityOnEnemies { get; init; }

            /// <summary>Cast on itself only: range zero.</summary>
            public bool OnSelf => MaxRange <= 0;

            public bool Offensive => Damage > 0 || Removal > 0;
            public bool Supportive => Heal > 0 || Buff > 0;
        }

        /// <summary>What the board says: where one can stand, who stands where, who sees whom.</summary>
        public sealed class Board
        {
            public Func<int, bool> Walkable { get; init; } = _ => true;
            public Func<int, int, bool> Sees { get; init; } = (_, _) => true;
            public IReadOnlyList<Fighter> Fighters { get; init; } = Array.Empty<Fighter>();

            public bool Occupied(int cell) => Fighters.Any(f => f.IsAlive && f.CellId == cell);
        }

        /// <summary>A step of the turn: walk this path, then cast this spell at this cell.</summary>
        public sealed record Action(IReadOnlyList<int> Path, Spell Spell, Fighter Target, int TargetCell, double Score)
        {
            public int From => Path[Path.Count - 1];
        }

        /// <summary>Walking a cell costs a little of what an action is worth: it spends the MP that places the monster afterwards.</summary>
        private const double WalkPenalty = 0.06;

        /// <summary>The next thing worth doing, or null when there is none.</summary>
        public static Action? Next(Board board, Fighter monster, IReadOnlyList<Spell> spells)
        {
            var enemies = board.Fighters.Where(f => f.IsAlive && f.TeamId != monster.TeamId).ToList();
            if (enemies.Count == 0) return null;
            var allies = board.Fighters.Where(f => f.IsAlive && f.TeamId == monster.TeamId).ToList();

            var reach = Reachable(board, monster);
            Action? best = null;

            foreach (var spell in spells)
            {
                if (spell.Cost < 0 || spell.Cost > monster.CurrentAP) continue;
                if (spell.Cost == 0 && spell.Utility <= 0 && !spell.Offensive) continue;
                if (monster.Recarga.TryGetValue(spell.Id, out int wait) && wait > 0) continue;
                monster.LanzadosEsteTurno.TryGetValue(spell.Id, out int thisTurn);
                if (spell.PerTurn > 0 && thisTurn >= spell.PerTurn) continue;
                // Support once a turn: a monster that buffs itself three times has wasted two.
                // And anything free once a turn too, or a free spell is cast until the cap.
                if ((!spell.Offensive || spell.Cost == 0) && thisTurn > 0) continue;

                foreach (var (cell, path) in reach)
                {
                    double walk = 1.0 - WalkPenalty * (path.Count - 1);
                    foreach (var (target, aim) in Targets(spell, monster, cell, enemies, allies))
                    {
                        if (!CanCast(board, spell, monster, cell, aim, target)) continue;
                        double value = Value(spell, monster, target, aim, cell, enemies, allies, board);
                        if (value <= 0) continue;

                        double score = value / Math.Max(1, spell.Cost) * walk;
                        if (best == null || score > best.Score)
                            best = new Action(path, spell, target, aim, score);
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// Where to stand once the casting is done: the path there, or just the monster's own cell.
        /// </summary>
        public static List<int> Reposition(Board board, Fighter monster, IReadOnlyList<Spell> spells)
        {
            var enemies = board.Fighters.Where(f => f.IsAlive && f.TeamId != monster.TeamId).ToList();
            var reach = Reachable(board, monster);
            if (enemies.Count == 0 || reach.Count <= 1) return new List<int> { monster.CellId };

            int Nearest(int cell) => enemies.Min(e => MapGeometry.Distance(cell, e.CellId));

            bool dying = monster.MaxHP > 0 && monster.CurrentHP * 100 / monster.MaxHP < 25
                         && !spells.Any(s => s.Heal > 0);
            var attack = spells.Where(s => s.Damage > 0 && !s.OnSelf).OrderByDescending(s => s.Damage / Math.Max(1, s.Cost)).FirstOrDefault();
            bool ranged = attack != null && (attack.MinRange >= 2 || attack.MaxRange >= 4);

            IEnumerable<KeyValuePair<int, List<int>>> cells = reach;
            KeyValuePair<int, List<int>> chosen;

            if (dying)
            {
                // Away from all of them, as far as its legs go.
                chosen = cells.OrderByDescending(kv => Nearest(kv.Key)).ThenBy(kv => kv.Value.Count).First();
            }
            else if (ranged)
            {
                // At the reach of its best spell from the nearest enemy, no closer than it must be.
                int want = attack!.MaxRange + monster.Range;
                chosen = cells.OrderBy(kv => Math.Abs(Nearest(kv.Key) - want))
                              .ThenByDescending(kv => Nearest(kv.Key))
                              .ThenBy(kv => kv.Value.Count).First();
            }
            else
            {
                // Next to the weakest enemy it can reach, where it locks him; else toward the nearest.
                var weakest = enemies.OrderBy(e => e.CurrentHP).ThenBy(e => MapGeometry.Distance(monster.CellId, e.CellId));
                chosen = default;
                foreach (var enemy in weakest)
                {
                    var next = cells.Where(kv => MapGeometry.Distance(kv.Key, enemy.CellId) == 1)
                                    .OrderBy(kv => kv.Value.Count).FirstOrDefault();
                    if (next.Value != null) { chosen = next; break; }
                }
                if (chosen.Value == null)
                    chosen = cells.OrderBy(kv => Nearest(kv.Key)).ThenBy(kv => kv.Value.Count).First();
            }
            return chosen.Value;
        }

        /// <summary>
        /// Every cell the monster can stand on this turn, with the path there: a search over the
        /// four neighbours of each cell, as far as its MP go, around whoever stands in the way.
        /// </summary>
        public static Dictionary<int, List<int>> Reachable(Board board, Fighter monster)
        {
            var paths = new Dictionary<int, List<int>> { [monster.CellId] = new List<int> { monster.CellId } };
            var queue = new Queue<int>();
            queue.Enqueue(monster.CellId);
            while (queue.Count > 0)
            {
                int here = queue.Dequeue();
                var path = paths[here];
                if (path.Count - 1 >= monster.CurrentMP) continue;
                foreach (int next in MapGeometry.GetNeighbors(here))
                {
                    if (paths.ContainsKey(next) || !board.Walkable(next) || board.Occupied(next)) continue;
                    paths[next] = new List<int>(path) { next };
                    queue.Enqueue(next);
                }
            }
            return paths;
        }

        /// <summary>Whom a spell can be aimed at, and at which cell.</summary>
        private static IEnumerable<(Fighter Target, int Aim)> Targets(Spell spell, Fighter monster, int from,
                                                                     List<Fighter> enemies, List<Fighter> allies)
        {
            if (spell.OnSelf)
            {
                yield return (monster, from);
                yield break;
            }
            if (spell.Offensive && spell.NeedsFreeCell)
            {
                foreach (var enemy in enemies)
                    foreach (int cell in MapGeometry.GetNeighbors(enemy.CellId))
                        yield return (enemy, cell);
            }
            else if (spell.Offensive)
                foreach (var enemy in enemies) yield return (enemy, enemy.CellId);
            if (spell.Heal > 0 || spell.Buff > 0)
                foreach (var ally in allies) yield return (ally, ally == monster ? from : ally.CellId);
            if (spell.Summons)
                foreach (int cell in MapGeometry.GetNeighbors(from)) yield return (monster, cell);
            if (spell.Utility > 0)
            {
                if (spell.UtilityOnEnemies)
                {
                    if (!spell.Offensive) foreach (var enemy in enemies) yield return (enemy, enemy.CellId);
                }
                else yield return (monster, from);
            }
        }

        private static bool CanCast(Board board, Spell spell, Fighter monster, int from, int aim, Fighter target)
        {
            if (spell.OnSelf) return aim == from;
            if (spell.Summons && target == monster && aim != from)
                return board.Walkable(aim) && !board.Occupied(aim) && MapGeometry.Distance(from, aim) <= Math.Max(1, spell.MaxRange);

            if (spell.NeedsFreeCell && (board.Occupied(aim) || !board.Walkable(aim))) return false;

            int distance = MapGeometry.Distance(from, aim);
            if (distance < spell.MinRange || distance > spell.MaxRange + monster.Range) return false;
            if (spell.InLine && !InLine(from, aim)) return false;
            if (spell.NeedsLineOfSight && distance > 1 && !board.Sees(from, aim)) return false;

            if (spell.PerTarget > 0 && monster.LanzadosPorObjetivo.TryGetValue((spell.Id, target.Id), out int onIt)
                && onIt >= spell.PerTarget)
                return false;
            return true;
        }

        private static double Value(Spell spell, Fighter monster, Fighter target, int aim, int from,
                                    List<Fighter> enemies, List<Fighter> allies, Board board)
        {
            double value = 0;
            bool onEnemy = target.TeamId != monster.TeamId;

            if (spell.Damage > 0 && (onEnemy || spell.OnSelf))
            {
                foreach (var hit in enemies.Where(e => Hit(spell, aim, e)))
                    value += Worth(spell, monster, hit);
                if (spell.HurtsAllies)
                    foreach (var own in allies.Where(a => Hit(spell, aim, a)))
                        value -= Blow(spell, monster, own);
            }

            if (spell.Removal > 0 && onEnemy && (target.CurrentAP > 0 || target.CurrentMP > 0))
                value += spell.Removal * (20 + target.Level / 4.0);

            if (spell.Heal > 0 && !onEnemy)
            {
                int missing = target.MaxHP - target.CurrentHP;
                if (missing * 10 >= target.MaxHP)
                {
                    value += Math.Min(spell.Heal, missing) * (target.CurrentHP * 2 < target.MaxHP ? 1.5 : 1.0);
                }
            }

            if (spell.Buff > 0 && !onEnemy)
                value += 10 + spell.Buff * 2 + monster.Level / 10.0;

            if (spell.Summons && target == monster && aim != from)
                value += 40 + monster.Level / 2.0;

            if (spell.Utility > 0 && (spell.UtilityOnEnemies ? onEnemy : target == monster))
                value += spell.Utility;

            return value;
        }

        /// <summary>
        /// What hitting an enemy is worth: the blow, a kill on top, and more the weaker he is --
        /// which is what makes the whole group go for the same one.
        /// </summary>
        private static double Worth(Spell spell, Fighter monster, Fighter enemy)
        {
            double blow = Blow(spell, monster, enemy);
            double worth = Math.Min(blow, enemy.CurrentHP);
            if (blow >= enemy.CurrentHP) worth += enemy.MaxHP * 0.5;
            if (enemy.MaxHP > 0) worth *= 1.0 + 0.5 * (1.0 - (double)enemy.CurrentHP / enemy.MaxHP);
            if (enemy.EsInvocado) worth *= 0.6;
            return worth;
        }

        /// <summary>
        /// The blow as the fight would roughly deal it: the base, raised by the caster's
        /// characteristic of its element, its power and its fixed damage, cut by the target's
        /// resistance. An estimate to compare spells with, not the fight's own reckoning.
        /// </summary>
        public static double Blow(Spell spell, Fighter caster, Fighter target)
        {
            int characteristic = Math.Max(0, caster.GetStatForElement(spell.Element));
            double raw = spell.Damage * (100 + characteristic + Math.Max(0, caster.Power)) / 100.0 + caster.FlatDamage;
            int resistance = Math.Clamp(target.GetResPctForElement(spell.Element), -100, 100);
            return Math.Max(0, raw * (100 - resistance) / 100.0);
        }

        private static bool Hit(Spell spell, int aim, Fighter who)
            => MapGeometry.Distance(aim, who.CellId) <= spell.Zone;

        /// <summary>Same row or same column of the diamond grid: a straight line.</summary>
        private static bool InLine(int a, int b)
        {
            var (ax, ay) = MapGeometry.CellToPoint(a);
            var (bx, by) = MapGeometry.CellToPoint(b);
            return ax == bx || ay == by;
        }
    }
}

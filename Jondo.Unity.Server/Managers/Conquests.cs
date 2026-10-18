using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The conquests of vulnerable prisms, the King of the Hill: their scores, their domination
    /// minutes and who wins; and the priority the winner then has to place its prism.
    /// </summary>
    /// <remarks>
    /// The rules are the client's help (Translations 1149316 and 1149317):
    /// <list type="bullet">
    /// <item>to win, an alliance must dominate the score for twenty minutes, in a row or not;</item>
    /// <item>beating players of enemy alliances in a fight gives points and a defeat takes them away;
    /// numerical superiority on the territory's maps gives them too;</item>
    /// <item>the scores of the alliances present are compared every minute, and the highest gets
    /// one more minute of domination, which needs one of its members in the zone; a tie gives each
    /// of the tied one;</item>
    /// <item>the winner then has thirty minutes alone to place a prism there.</item>
    /// </list>
    /// How many points a map or a fight is worth the help does not say: those are inferred
    /// (<see cref="PointsPerMap"/>, <see cref="PointsPerFighter"/>). The roles' own points -- the
    /// scout's, which the conquest window shows apart -- are not here: no role is learnt yet.
    /// The wars live in memory; a restart in the middle of one starts it again from its prism,
    /// which stays vulnerable in world.db.
    /// </remarks>
    public static class Conquests
    {
        /// <summary>«Dominar en puntuación durante 20 minutos».</summary>
        public const int DominationToWin = 20;

        /// <summary>«Las puntuaciones … se comparan cada minuto».</summary>
        public const long TickMs = 60_000;

        /// <summary>«Prioridad para poner un prisma durante 30 minutos».</summary>
        public const int PriorityMinutes = 30;

        /// <summary>
        /// The three scores, the client's AllianceAvaUi.ScoreType, as the conquest window lists
        /// them: the maps held by numbers, the fights, and the prism's (the scouts').
        /// </summary>
        public const int ScoreOfMaps = 0;
        public const int ScoreOfFights = 1;
        public const int ScoreOfPrism = 2;
        public const int ScoreKinds = 3;

        /// <summary>INFERRED: a point a minute for each of the zone's maps an alliance outnumbers the rest on.</summary>
        public const int PointsPerMap = 1;

        /// <summary>INFERRED: the points an aggression won gives for each enemy beaten, and a lost one takes.</summary>
        public const int PointsPerFighter = 5;

        /// <summary>An alliance taking part.</summary>
        public sealed class Side
        {
            public long AllianceId { get; init; }

            /// <summary>Its minutes of domination.</summary>
            public int Domination { get; set; }

            /// <summary>Its points by kind: <see cref="ScoreOfMaps"/>, <see cref="ScoreOfFights"/>, <see cref="ScoreOfPrism"/>.</summary>
            public long[] Scores { get; } = new long[ScoreKinds];

            /// <summary>Its members in the zone at the last minute.</summary>
            public int Present { get; set; }

            public long Total => Scores.Sum();
        }

        /// <summary>The conquest of one subarea.</summary>
        public sealed class War
        {
            public int SubAreaId { get; init; }

            /// <summary>The alliance whose prism is vulnerable.</summary>
            public long DefenderId { get; init; }

            public long StartedUtcMs { get; init; }

            /// <summary>When it runs out with no winner, and the defence keeps its prism.</summary>
            public long EndsUtcMs { get; init; }

            /// <summary>When the next minute is counted.</summary>
            public long NextTickUtcMs { get; set; }

            private readonly Dictionary<long, Side> _sides = new();

            /// <summary>The defender first, then the others as they came.</summary>
            public IReadOnlyList<Side> Sides
            {
                get { lock (_sides) return _sides.Values.ToList(); }
            }

            /// <summary>An alliance's side, which it gets the first time it is there.</summary>
            public Side SideOf(long allianceId)
            {
                lock (_sides)
                {
                    if (!_sides.TryGetValue(allianceId, out var side))
                        _sides[allianceId] = side = new Side { AllianceId = allianceId };
                    return side;
                }
            }

            public bool HasSide(long allianceId)
            {
                lock (_sides) return _sides.ContainsKey(allianceId);
            }
        }

        /// <summary>A member in the zone: his alliance and the map he is on.</summary>
        public readonly record struct Presence(long AllianceId, long MapId, long CharacterId);

        private static readonly ConcurrentDictionary<int, War> _wars = new();

        /// <summary>A conquest opening on a subarea; the one already running if there is one.</summary>
        public static War Start(int subAreaId, long defenderId, long nowMs, long endsUtcMs)
        {
            return _wars.GetOrAdd(subAreaId, _ =>
            {
                var war = new War
                {
                    SubAreaId = subAreaId,
                    DefenderId = defenderId,
                    StartedUtcMs = nowMs,
                    EndsUtcMs = endsUtcMs,
                    NextTickUtcMs = nowMs + TickMs,
                };
                war.SideOf(defenderId);
                return war;
            });
        }

        public static War In(int subAreaId) => _wars.TryGetValue(subAreaId, out var war) ? war : null;

        public static IReadOnlyList<War> All() => _wars.Values.ToList();

        public static void End(int subAreaId) => _wars.TryRemove(subAreaId, out _);

        /// <summary>
        /// One minute of a conquest: the maps' points to whoever outnumbers the rest on each, a
        /// minute of domination to the highest score among the alliances present, and the
        /// winner, the alliance id, once one has dominated twenty minutes; 0 while none has.
        /// </summary>
        /// <remarks>
        /// When more than one reaches the twenty at the same minute, the defence wins if it is
        /// among them, and else the highest score.
        /// </remarks>
        public static long Tick(War war, IReadOnlyCollection<Presence> present, long nowMs)
        {
            war.NextTickUtcMs = nowMs + TickMs;
            foreach (var side in war.Sides) side.Present = 0;
            foreach (var byAlliance in present.GroupBy(p => p.AllianceId))
                war.SideOf(byAlliance.Key).Present = byAlliance.Select(p => p.CharacterId).Distinct().Count();

            foreach (var map in present.GroupBy(p => p.MapId))
            {
                var counts = map.GroupBy(p => p.AllianceId)
                                .Select(g => (Alliance: g.Key, Count: g.Select(p => p.CharacterId).Distinct().Count()))
                                .OrderByDescending(c => c.Count)
                                .ToList();
                if (counts.Count == 1 || counts[0].Count > counts[1].Count)
                    war.SideOf(counts[0].Alliance).Scores[ScoreOfMaps] += PointsPerMap;
            }

            var there = war.Sides.Where(s => s.Present > 0).ToList();
            if (there.Count > 0)
            {
                long best = there.Max(s => s.Total);
                foreach (var side in there.Where(s => s.Total == best)) side.Domination++;
            }

            var winners = war.Sides.Where(s => s.Domination >= DominationToWin).ToList();
            if (winners.Count == 0) return 0;
            if (winners.Any(s => s.AllianceId == war.DefenderId)) return war.DefenderId;
            return winners.OrderByDescending(s => s.Total).ThenBy(s => s.AllianceId).First().AllianceId;
        }

        /// <summary>
        /// An aggression of the zone over: its winners' alliances get the points of the enemies they
        /// beat, its losers' lose theirs.
        /// </summary>
        public static void FightOver(War war, IReadOnlyCollection<long> winnerAlliances, IReadOnlyCollection<long> loserAlliances)
        {
            if (war == null) return;
            foreach (long alliance in winnerAlliances)
                war.SideOf(alliance).Scores[ScoreOfFights] += PointsPerFighter * loserAlliances.Count;
            foreach (long alliance in loserAlliances)
                war.SideOf(alliance).Scores[ScoreOfFights] -= PointsPerFighter;
        }

        // ─── The winner's priority ──────────────────────────────────────────────

        /// <summary>Who alone may place a prism in a subarea, and until when.</summary>
        public sealed record Priority(long AllianceId, long UntilUtcMs);

        private static readonly ConcurrentDictionary<int, Priority> _priorities = new();

        public static void GivePriority(int subAreaId, long allianceId, long untilUtcMs)
            => _priorities[subAreaId] = new Priority(allianceId, untilUtcMs);

        /// <summary>The priority on a subarea still running, or null.</summary>
        public static Priority PriorityIn(int subAreaId, long nowMs)
        {
            if (!_priorities.TryGetValue(subAreaId, out var priority)) return null;
            if (priority.UntilUtcMs > nowMs) return priority;
            _priorities.TryRemove(subAreaId, out _);
            return null;
        }

        public static void TakePriority(int subAreaId) => _priorities.TryRemove(subAreaId, out _);

        /// <summary>
        /// How long a prism the winner places stays pacified: the setting's hours divided by the
        /// prisms the alliance holds, that one included -- «cuanto más territorio posea, menor será
        /// el tiempo de invulnerabilidad».
        /// </summary>
        public static long InvulnerabilityMs(int hours, int prismsHeld)
            => hours <= 0 ? 0 : hours * 3_600_000L / Math.Max(1, prismsHeld);

        /// <summary>For the tests: no war, no priority.</summary>
        internal static void Clear()
        {
            _wars.Clear();
            _priorities.Clear();
        }
    }
}

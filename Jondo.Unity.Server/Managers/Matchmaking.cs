using System;
using System.Collections.Generic;
using System.Linq;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The automatic dungeon search (the client's DungeonMatchmaking): players, alone or as a whole
    /// party, wait for any of the dungeons they chose; when enough wait for one, they are offered
    /// it, and once all accept they are a party in its first room.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The capture «busqueda automatica de grupo-encuentra para una dung-aceptar-autoteleporte de
    /// todos y creacion de grupo-abandonarlo»: a classic search over 90 dungeons, a ready check of
    /// four places counting down from a minute, the four accepting one by one, then the party, the
    /// dungeon's key spent and everybody in room 1 of dungeon 141.
    /// </para>
    /// <para>
    /// Who is matched: the first to wait, and with them the next ones whose party still fits,
    /// until the group is exactly full. A dungeon is only offered to those who chose it.
    /// </para>
    /// </remarks>
    public static class Matchmaking
    {
        /// <summary>The ready check's places in the capture: a dungeon party of four.</summary>
        public const int GameGroupSize = 4;

        /// <summary>The ready check's minute: the capture's counts 59, 56, 54, 52 as the four accept.</summary>
        public const int ConfirmSeconds = 60;

        /// <summary>The group size in use: the server setting, or the game's.</summary>
        public static int GroupSize
        {
            get
            {
                int set = ServerSettings.Current.MatchmakingGroupSize;
                return set > 0 ? Math.Min(set, GameGroupSize) : GameGroupSize;
            }
        }

        /// <summary>One waiting: a player alone, or a party through its leader.</summary>
        public sealed class Seeker
        {
            public long LeaderId { get; init; }
            public List<long> Members { get; init; } = new();
            public List<int> Dungeons { get; init; } = new();
            public DateTime Since { get; init; } = DateTime.UtcNow;
        }

        /// <summary>A group offered a dungeon, waiting for everybody's yes.</summary>
        public sealed class Match
        {
            public int Id { get; init; }
            public int DungeonId { get; init; }
            public List<Seeker> Seekers { get; init; } = new();
            public HashSet<long> Accepted { get; } = new();
            public DateTime Deadline { get; init; }

            public IEnumerable<long> Members => Seekers.SelectMany(s => s.Members);
            public bool AllAccepted => Members.All(Accepted.Contains);
        }

        private static readonly List<Seeker> _queue = new();
        private static readonly Dictionary<int, Match> _matches = new();
        private static readonly object _lock = new();
        private static int _nextMatch;

        public static void Clear()
        {
            lock (_lock)
            {
                _queue.Clear();
                _matches.Clear();
            }
        }

        public static T Read<T>(Func<T> read)
        {
            lock (_lock) return read();
        }

        /// <summary>The search a character is part of, as its leader or one of its party.</summary>
        public static Seeker SeekerOf(long characterId)
        {
            lock (_lock) return _queue.FirstOrDefault(s => s.Members.Contains(characterId));
        }

        /// <summary>The offer a character has in front of him.</summary>
        public static Match MatchOf(long characterId)
        {
            lock (_lock) return _matches.Values.FirstOrDefault(m => m.Members.Contains(characterId));
        }

        /// <summary>How many wait for each dungeon: what the window shows by every dungeon (kis).</summary>
        public static Dictionary<int, int> Waiting()
        {
            lock (_lock)
            {
                var counts = new Dictionary<int, int>();
                foreach (var seeker in _queue)
                    foreach (int dungeon in seeker.Dungeons.Distinct())
                        counts[dungeon] = counts.GetValueOrDefault(dungeon) + seeker.Members.Count;
                return counts;
            }
        }

        /// <summary>A search starts, or replaces the one its leader had.</summary>
        public static void Join(Seeker seeker)
        {
            lock (_lock)
            {
                _queue.RemoveAll(s => s.Members.Intersect(seeker.Members).Any());
                _queue.Add(seeker);
            }
        }

        /// <summary>A search stops. True when there was one.</summary>
        public static bool Leave(long characterId)
        {
            lock (_lock) return _queue.RemoveAll(s => s.Members.Contains(characterId)) > 0;
        }

        /// <summary>
        /// A group out of the queue when one can be made: for each dungeon, from the longest waiting
        /// down, the searches that fit until exactly <paramref name="size"/> players. Null when none.
        /// A search with somebody gone (<paramref name="online"/>) is dropped first.
        /// </summary>
        public static Match TryMatch(int size, DateTime now, Func<long, bool> online = null)
        {
            lock (_lock)
            {
                if (online != null) _queue.RemoveAll(s => !s.Members.All(online));
                foreach (var first in _queue.OrderBy(s => s.Since).ToList())
                {
                    foreach (int dungeon in first.Dungeons)
                    {
                        var group = new List<Seeker>();
                        int count = 0;
                        foreach (var seeker in _queue.Where(s => s.Dungeons.Contains(dungeon)).OrderBy(s => s.Since))
                        {
                            if (count + seeker.Members.Count > size) continue;
                            group.Add(seeker);
                            count += seeker.Members.Count;
                            if (count == size) break;
                        }
                        if (count != size || !group.Contains(first)) continue;

                        foreach (var seeker in group) _queue.Remove(seeker);
                        var match = new Match
                        {
                            Id = ++_nextMatch,
                            DungeonId = dungeon,
                            Seekers = group,
                            Deadline = now.AddSeconds(ConfirmSeconds),
                        };
                        _matches[match.Id] = match;
                        return match;
                    }
                }
                return null;
            }
        }

        /// <summary>A yes to the offer. The match, and whether everybody has said it now.</summary>
        public static (Match Match, bool AllAccepted) Accept(long characterId)
        {
            lock (_lock)
            {
                var match = _matches.Values.FirstOrDefault(m => m.Members.Contains(characterId));
                if (match == null) return (null, false);
                match.Accepted.Add(characterId);
                if (!match.AllAccepted) return (match, false);
                _matches.Remove(match.Id);
                return (match, true);
            }
        }

        /// <summary>
        /// A no to the offer, or a search stopped while offered: the offer is gone, the searches of
        /// everybody else go back to the queue as they were -- their place included -- and the one
        /// who said no stops searching. The match, for telling them.
        /// </summary>
        public static Match Refuse(long characterId)
        {
            lock (_lock)
            {
                var match = _matches.Values.FirstOrDefault(m => m.Members.Contains(characterId));
                if (match == null) return null;
                _matches.Remove(match.Id);
                foreach (var seeker in match.Seekers.Where(s => !s.Members.Contains(characterId)))
                    _queue.Add(seeker);
                return match;
            }
        }

        /// <summary>
        /// The offers whose minute ran out: the searches that had all said yes go back to the queue,
        /// the others stop.
        /// </summary>
        public static List<Match> Expire(DateTime now)
        {
            lock (_lock)
            {
                var expired = _matches.Values.Where(m => m.Deadline <= now).ToList();
                foreach (var match in expired)
                {
                    _matches.Remove(match.Id);
                    foreach (var seeker in match.Seekers.Where(s => s.Members.All(match.Accepted.Contains)))
                        _queue.Add(seeker);
                }
                return expired;
            }
        }
    }
}

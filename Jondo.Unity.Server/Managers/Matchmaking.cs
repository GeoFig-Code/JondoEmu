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
    /// <para>
    /// Once made, the group keeps its own rules, the client's texts' (ui.groupSearch.
    /// dungeonMatchmaking): giving up the dungeon and putting somebody out are votes, and both
    /// must be unanimous -- «Debe ser unánime» --; whoever leaves a full group, or is put out of
    /// one, may not search for an hour -- «Ya no podrás utilizar la búsqueda automática de grupo
    /// durante 1 hora» --; and a group that gives up together pays nothing.
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
        private static readonly Dictionary<int, Group> _groups = new();
        private static readonly Dictionary<long, DateTime> _bannedUntil = new();
        private static readonly object _lock = new();
        private static int _nextMatch;

        public static void Clear()
        {
            lock (_lock)
            {
                _queue.Clear();
                _matches.Clear();
                _groups.Clear();
                _bannedUntil.Clear();
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

        // ─── The groups made ────────────────────────────────────────────────

        /// <summary>
        /// How long a vote stays open: the ready check's minute. An inference -- the client counts
        /// down whatever it is sent (kig f2 in seconds, ikw f3 in milliseconds).
        /// </summary>
        public const int VoteSeconds = 60;

        /// <summary>The wait before another vote of the same kind once one fails: two minutes, an inference.</summary>
        public const int VoteCooldownSeconds = 120;

        /// <summary>The search's ban for leaving a full group or being put out of one: «durante 1 hora».</summary>
        public const int BanSeconds = 3600;

        /// <summary>
        /// What a vote came to: the client's own order (fgr.beqj for giving up, fgr.beqp for
        /// putting out) -- favourable, not favourable, expired, and anything else unknown.
        /// </summary>
        public enum Outcome { Favorable = 0, NotFavorable = 1, Expired = 2, Undefined = 3 }

        /// <summary>A vote of the group: to give up the dungeon, or to put somebody out.</summary>
        public sealed class Vote
        {
            /// <summary>Whom it would put out; zero for giving up.</summary>
            public long Target { get; init; }

            /// <summary>Who votes: everybody, or everybody but the one it would put out.</summary>
            public List<long> Voters { get; init; } = new();

            /// <summary>The ballots in the order they came, as the notices carry them.</summary>
            public List<(long Voter, bool Yes)> Ballots { get; } = new();

            public DateTime Deadline { get; init; }

            /// <summary>Unanimous yes, one no, or nothing yet.</summary>
            public Outcome? Result
                => Ballots.Any(b => !b.Yes) ? Outcome.NotFavorable
                 : Voters.All(v => Ballots.Any(b => b.Voter == v)) ? Outcome.Favorable
                 : null;
        }

        /// <summary>A group the search made, kept while its party lasts.</summary>
        public sealed class Group
        {
            public int PartyId { get; init; }
            public int DungeonId { get; init; }

            /// <summary>How many it was made of: full while it still has as many.</summary>
            public int Size { get; init; }

            /// <summary>Where each one was when taken in: where giving up, or being put out, sends him back.</summary>
            public Dictionary<long, (long MapId, int CellId)> CameFrom { get; init; } = new();

            public Vote? Surrender { get; set; }
            public Vote? Kick { get; set; }
            public DateTime SurrenderAgainAt { get; set; }
            public DateTime KickAgainAt { get; set; }
        }

        /// <summary>What a ballot did.</summary>
        public enum Ballot { Started, Counted, Cooldown, Refused }

        /// <summary>A group made: its party, its dungeon and where its players came from.</summary>
        public static Group Made(int partyId, int dungeonId, IReadOnlyDictionary<long, (long MapId, int CellId)> cameFrom)
        {
            var group = new Group
            {
                PartyId = partyId,
                DungeonId = dungeonId,
                Size = cameFrom.Count,
                CameFrom = new Dictionary<long, (long, int)>(cameFrom),
            };
            lock (_lock) _groups[partyId] = group;
            return group;
        }

        /// <summary>The search's group a party is, or null for a party made by hand.</summary>
        public static Group? GroupOf(int partyId)
        {
            lock (_lock) return _groups.TryGetValue(partyId, out var group) ? group : null;
        }

        /// <summary>The group is over: given up, or its party gone.</summary>
        public static void Disband(int partyId)
        {
            lock (_lock) _groups.Remove(partyId);
        }

        /// <summary>
        /// A ballot on giving up (kjs): a yes starts the vote when none runs -- unless one failed
        /// a moment ago -- and any ballot of somebody who has not voted counts. The vote's result
        /// once it has one, and how long to wait on a cooldown.
        /// </summary>
        public static (Ballot What, Vote? Vote, Outcome? Result, TimeSpan Wait) VoteSurrender(
            int partyId, long voter, bool yes, IReadOnlyCollection<long> members, DateTime now)
        {
            lock (_lock)
            {
                if (!_groups.TryGetValue(partyId, out var group) || !members.Contains(voter))
                    return (Ballot.Refused, null, null, TimeSpan.Zero);
                var what = Ballot.Counted;
                if (group.Surrender == null)
                {
                    if (!yes) return (Ballot.Refused, null, null, TimeSpan.Zero);
                    if (now < group.SurrenderAgainAt) return (Ballot.Cooldown, null, null, group.SurrenderAgainAt - now);
                    group.Surrender = new Vote { Voters = members.ToList(), Deadline = now.AddSeconds(VoteSeconds) };
                    what = Ballot.Started;
                }
                var vote = group.Surrender;
                if (!vote.Voters.Contains(voter) || vote.Ballots.Any(b => b.Voter == voter))
                    return (Ballot.Refused, vote, null, TimeSpan.Zero);
                vote.Ballots.Add((voter, yes));
                var result = vote.Result;
                if (result != null) EndSurrender(group, result.Value, now);
                return (what, vote, result, TimeSpan.Zero);
            }
        }

        /// <summary>
        /// A ballot on putting somebody out: the client's ili is the yes -- the one that starts
        /// the vote and every other --, its ikn the no, naming nobody (<paramref name="target"/>
        /// zero). Nobody votes on himself; a vote starts only against a member, and not while
        /// another runs or one failed a moment ago.
        /// </summary>
        public static (Ballot What, Vote? Vote, Outcome? Result, TimeSpan Wait) VoteKick(
            int partyId, long voter, long target, bool yes, IReadOnlyCollection<long> members, DateTime now)
        {
            lock (_lock)
            {
                if (!_groups.TryGetValue(partyId, out var group) || !members.Contains(voter))
                    return (Ballot.Refused, null, null, TimeSpan.Zero);
                var what = Ballot.Counted;
                if (group.Kick == null)
                {
                    if (!yes || target == voter || !members.Contains(target)) return (Ballot.Refused, null, null, TimeSpan.Zero);
                    if (now < group.KickAgainAt) return (Ballot.Cooldown, null, null, group.KickAgainAt - now);
                    group.Kick = new Vote
                    {
                        Target = target,
                        Voters = members.Where(m => m != target).ToList(),
                        Deadline = now.AddSeconds(VoteSeconds),
                    };
                    what = Ballot.Started;
                }
                var vote = group.Kick;
                if (target != 0 && target != vote.Target) return (Ballot.Refused, vote, null, TimeSpan.Zero);
                if (!vote.Voters.Contains(voter) || vote.Ballots.Any(b => b.Voter == voter))
                    return (Ballot.Refused, vote, null, TimeSpan.Zero);
                vote.Ballots.Add((voter, yes));
                var result = vote.Result;
                if (result != null) EndKick(group, result.Value, now);
                return (what, vote, result, TimeSpan.Zero);
            }
        }

        /// <summary>
        /// The kick vote's cooldown left for a group, as the iku that answers the client's own ili
        /// after a failed vote -- every member's client sends one (fgr.beqp). Zero when none.
        /// </summary>
        public static TimeSpan KickCooldown(int partyId, DateTime now)
        {
            lock (_lock)
                return _groups.TryGetValue(partyId, out var group) && group.Kick == null && now < group.KickAgainAt
                    ? group.KickAgainAt - now
                    : TimeSpan.Zero;
        }

        private static void EndSurrender(Group group, Outcome result, DateTime now)
        {
            group.Surrender = null;
            if (result != Outcome.Favorable) group.SurrenderAgainAt = now.AddSeconds(VoteCooldownSeconds);
        }

        private static void EndKick(Group group, Outcome result, DateTime now)
        {
            group.Kick = null;
            if (result != Outcome.Favorable) group.KickAgainAt = now.AddSeconds(VoteCooldownSeconds);
        }

        /// <summary>The votes whose minute ran out, each with its group and whether it was a kick.</summary>
        public static List<(Group Group, Vote Vote, bool Kick)> ExpireVotes(DateTime now)
        {
            lock (_lock)
            {
                var expired = new List<(Group, Vote, bool)>();
                foreach (var group in _groups.Values)
                {
                    if (group.Surrender is { } surrender && surrender.Deadline <= now)
                    {
                        expired.Add((group, surrender, false));
                        EndSurrender(group, Outcome.Expired, now);
                    }
                    if (group.Kick is { } kick && kick.Deadline <= now)
                    {
                        expired.Add((group, kick, true));
                        EndKick(group, Outcome.Expired, now);
                    }
                }
                return expired;
            }
        }

        /// <summary>
        /// Somebody left the group: off its votes -- a vote that would have put him out is over
        /// --, and the vote's result if his going settled it. The group itself goes with its party.
        /// </summary>
        public static List<(Vote Vote, bool Kick, Outcome Result)> Left(int partyId, long characterId, DateTime now)
        {
            lock (_lock)
            {
                var settled = new List<(Vote, bool, Outcome)>();
                if (!_groups.TryGetValue(partyId, out var group)) return settled;
                group.CameFrom.Remove(characterId);
                if (group.Kick is { } kick)
                {
                    if (kick.Target == characterId)
                    {
                        group.Kick = null;
                        settled.Add((kick, true, Outcome.Undefined));
                    }
                    else if (kick.Voters.Remove(characterId))
                    {
                        kick.Ballots.RemoveAll(b => b.Voter == characterId);
                        if (kick.Result is { } result)
                        {
                            EndKick(group, result, now);
                            settled.Add((kick, true, result));
                        }
                    }
                }
                if (group.Surrender is { } surrender && surrender.Voters.Remove(characterId))
                {
                    surrender.Ballots.RemoveAll(b => b.Voter == characterId);
                    if (surrender.Result is { } result)
                    {
                        EndSurrender(group, result, now);
                        settled.Add((surrender, false, result));
                    }
                }
                return settled;
            }
        }

        /// <summary>Barred from the search until then.</summary>
        public static void Ban(long characterId, DateTime until)
        {
            lock (_lock) _bannedUntil[characterId] = until;
        }

        /// <summary>How long a character is still barred from the search; zero when he is not.</summary>
        public static TimeSpan BanLeft(long characterId, DateTime now)
        {
            lock (_lock)
                return _bannedUntil.TryGetValue(characterId, out var until) && until > now ? until - now : TimeSpan.Zero;
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jondo.Unity.Launcher;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The wanted posters: the criminals, the quests that want them, where they roam and the jailers
    /// they are handed to (datos/wanted_*.json, tools/extract_wanted.py), and where each one is
    /// standing right now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client's data: a criminal is a monster with isBounty 1 -- 87 of them, 81 wanted by a
    /// quest -- whose drops carry the criterion Qs=&lt;step&gt; of its quest. That quest has two
    /// objectives, «Localiser et neutraliser Gein» and «Ramener Gein au Krosmoglob du Colonel», and
    /// the jailer's replies say «Livrer Gein.» and «Déposer 500 kamas ... concernant Gein.».
    /// </para>
    /// <para>
    /// The guides (DPLN, «On recherche Gein», «On recherche Bouflouth»): the criminal «apparaît
    /// toujours» in its zone «mais il n'est pas toujours présent»; once beaten «il disparaît pendant
    /// plusieurs heures», unless nobody in the winning group could take him -- no quest, or done
    /// already --, and then «il réapparaît immédiatement». Whoever has the quest leaves the fight
    /// with him, and the jailer's «Livrer» ends it. The jailer, for 500 kamas, says whether he has
    /// been seen. The world's groups ship with the criminals in 6,352 of 38,744 of them: they are
    /// taken out as the groups are read, and each one is put back in ONE group of its zone.
    /// INFERRED: «several hours» is two to six, drawn; a beaten criminal nobody took comes back two
    /// minutes later, time enough for every fighter's end of fight to say whether he took him.
    /// </para>
    /// </remarks>
    public static class WantedPosters
    {
        /// <summary>What a jailer asks for his report.</summary>
        public const int InformationPrice = 500;

        /// <summary>How long a criminal stays away once somebody has taken him.</summary>
        public static readonly TimeSpan ShortestAbsence = TimeSpan.FromHours(2), LongestAbsence = TimeSpan.FromHours(6);

        /// <summary>How long a criminal beaten by nobody who wanted him takes to come back.</summary>
        public static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);

        /// <summary>One jailer's report on one criminal: the replies and messages that are his.</summary>
        public sealed record Report(int Npc, int Deliver, int Pay, int Found, int Seen, int Unseen);

        /// <summary>A criminal: its quest, its zone, the two objectives, and who takes it.</summary>
        public sealed record Criminal(int Quest, int Monster, IReadOnlyList<int> Subareas, int Catch, int Deliver,
                                      long Map, IReadOnlyList<Report> Jailers)
        {
            /// <summary>This jailer's report on him, or null when he is not handed to that one.</summary>
            public Report? To(int npc) => Jailers.FirstOrDefault(j => j.Npc == npc);
        }

        /// <summary>
        /// A jailer's bribe: «When were they last seen?» (Ask) → his first answer (First), «I could pay
        /// you» (Offer) → his second (Second), from which the «500 kamas» replies hang, and the replies
        /// every report shares. Some skip the haggling: First and Offer are zero, and Ask goes straight
        /// to Second.
        /// </summary>
        public sealed record Jailer(int Npc, int Ask, int First, int Offer, int Second, int Keep,
                                    int Listen, int Another, int Leave, int Shrug);

        private static Dictionary<int, Criminal> _byMonster = new();
        private static Dictionary<int, Criminal> _byQuest = new();
        private static Dictionary<int, Jailer> _jailers = new();
        private static bool _loaded;
        private static readonly object _gate = new();

        /// <summary>Where each criminal is standing: monster → (map, group).</summary>
        private static readonly Dictionary<int, (long Map, long Group)> _placed = new();

        /// <summary>When each criminal that is standing nowhere may come back, in Unix milliseconds.</summary>
        private static readonly Dictionary<int, long> _awayUntil = new();

        /// <summary>Reads the file, once.</summary>
        public static void Load()
        {
            lock (_gate)
            {
                if (_loaded) return;
                _loaded = true;
                string path = Paths.WantedJson;
                if (!File.Exists(path))
                {
                    Console.WriteLine($"[Wanted] {Path.GetFileName(path)} not found: no criminal is wanted.");
                    return;
                }
                try
                {
                    using var json = JsonDocument.Parse(File.ReadAllText(path));
                    Read(json.RootElement);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Wanted] Could not read {Path.GetFileName(path)}: {ex.Message}");
                }
                Console.WriteLine($"[Wanted] {_byMonster.Count} criminals wanted, {_jailers.Count} jailers.");
            }
        }

        /// <summary>Replaces the data with this document's, and forgets where everybody was. For the tests.</summary>
        public static void LoadFrom(JsonElement root)
        {
            lock (_gate)
            {
                _loaded = true;
                _placed.Clear();
                _awayUntil.Clear();
                Read(root);
            }
        }

        private static void Read(JsonElement root)
        {
            var byMonster = new Dictionary<int, Criminal>();
            var byQuest = new Dictionary<int, Criminal>();
            var jailers = new Dictionary<int, Jailer>();

            foreach (var row in root.GetProperty("wanted").EnumerateArray())
            {
                var reports = row.GetProperty("jailers").EnumerateArray().Select(j => new Report(
                    Int(j, "npc"), Int(j, "deliver"), Int(j, "pay"), Int(j, "found"), Int(j, "seen"), Int(j, "unseen"))).ToList();
                var criminal = new Criminal(Int(row, "quest"), Int(row, "monster"),
                    row.GetProperty("subareas").EnumerateArray().Select(z => z.GetInt32()).ToList(),
                    Int(row, "catch"), Int(row, "deliver"),
                    row.TryGetProperty("map", out var map) && map.ValueKind == JsonValueKind.Number ? map.GetInt64() : 0,
                    reports);
                byMonster[criminal.Monster] = criminal;
                byQuest[criminal.Quest] = criminal;
            }

            foreach (var j in root.GetProperty("jailers").EnumerateArray())
            {
                var jailer = new Jailer(Int(j, "npc"), Int(j, "ask"), Int(j, "first"), Int(j, "offer"), Int(j, "second"),
                                        Int(j, "keep"), Int(j, "listen"), Int(j, "another"), Int(j, "leave"), Int(j, "shrug"));
                jailers[jailer.Npc] = jailer;
            }

            _byMonster = byMonster;
            _byQuest = byQuest;
            _jailers = jailers;
        }

        private static int Int(JsonElement row, string name)
            => row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

        /// <summary>Whether a monster is a wanted criminal.</summary>
        public static bool Is(int monster) => _byMonster.ContainsKey(monster);

        public static Criminal? ByMonster(int monster) => _byMonster.TryGetValue(monster, out var c) ? c : null;

        public static Criminal? ByQuest(int quest) => _byQuest.TryGetValue(quest, out var c) ? c : null;

        public static Jailer? JailerOf(int npc) => _jailers.TryGetValue(npc, out var j) ? j : null;

        /// <summary>The criminals this NPC takes, in the client's order.</summary>
        public static IEnumerable<Criminal> HandedTo(int npc)
            => _byMonster.Values.Where(c => c.To(npc) != null).OrderBy(c => c.Monster);

        public static IReadOnlyCollection<Criminal> All => _byMonster.Values;

        // ---- Where they are ----

        /// <summary>Whether this criminal is standing in some group of its zone.</summary>
        public static bool IsAbout(int monster)
        {
            lock (_gate) return _placed.ContainsKey(monster);
        }

        /// <summary>Where this criminal stands, or null.</summary>
        public static (long Map, long Group)? WhereIs(int monster)
        {
            lock (_gate) return _placed.TryGetValue(monster, out var at) ? at : null;
        }

        /// <summary>The criminals that may be put somewhere now: standing nowhere, and not away.</summary>
        public static IReadOnlyList<Criminal> Due(long nowMs)
        {
            lock (_gate)
            {
                return _byMonster.Values
                    .Where(c => !_placed.ContainsKey(c.Monster)
                                && (!_awayUntil.TryGetValue(c.Monster, out long until) || until <= nowMs))
                    .OrderBy(c => c.Monster)
                    .ToList();
            }
        }

        /// <summary>This criminal now stands in that group.</summary>
        public static void Placed(int monster, long map, long group)
        {
            lock (_gate)
            {
                _placed[monster] = (map, group);
                _awayUntil.Remove(monster);
            }
        }

        /// <summary>
        /// The group he stood in has gone -- beaten, removed --: he stands nowhere, and comes back once
        /// <see cref="Grace"/> is over, unless somebody took him meanwhile (<see cref="Captured"/>).
        /// </summary>
        public static void Released(long map, long group, long nowMs)
        {
            lock (_gate)
            {
                foreach (var pair in _placed.Where(p => p.Value.Map == map && p.Value.Group == group).ToList())
                {
                    _placed.Remove(pair.Key);
                    long back = nowMs + (long)Grace.TotalMilliseconds;
                    _awayUntil[pair.Key] = _awayUntil.TryGetValue(pair.Key, out long until) ? Math.Max(until, back) : back;
                }
            }
        }

        /// <summary>Every criminal standing on that map stands nowhere now: the map was emptied or restored.</summary>
        public static void ReleasedFrom(long map, long nowMs)
        {
            List<long> groups;
            lock (_gate) groups = _placed.Values.Where(p => p.Map == map).Select(p => p.Group).ToList();
            foreach (long group in groups) Released(map, group, nowMs);
        }

        /// <summary>Somebody took him: he is away for two to six hours.</summary>
        public static TimeSpan Captured(int monster, long nowMs, Random dice)
        {
            var away = ShortestAbsence + TimeSpan.FromMinutes(dice.Next((int)(LongestAbsence - ShortestAbsence).TotalMinutes + 1));
            lock (_gate)
            {
                _placed.Remove(monster);
                long until = nowMs + (long)away.TotalMilliseconds;
                _awayUntil[monster] = _awayUntil.TryGetValue(monster, out long already) ? Math.Max(already, until) : until;
            }
            return away;
        }

        /// <summary>
        /// A group's members without its criminals, and how many were taken out: a group drawn from
        /// a zone's roster could carry one, and they are put back one to a zone by the spawner.
        /// </summary>
        public static int Strip(List<int> ids) => ids.RemoveAll(Is);
    }
}

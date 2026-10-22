using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The treasure hunts in progress: where each one started, its steps, the flags put on them,
    /// the tries left, and the rules that judge a flag, a dig and a give-up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured: «Interactivos varios/…-lanzar busqueda portal», the Travelers' Tower's Xelorium
    /// model launching its legendary hunt: the answer (hhk 2, 5) and the hunt (hhf) with five
    /// stages (f4), five steps a stage (f8), four tries (f6), its start at [2,4] and the first
    /// step known. The client (TreasureHunt, he) shows «Etapa f1+1/f4», the steps known, as many
    /// «?» as are left, and at the end «Validación» -- «Combate» on the last stage. A flag is put
    /// (hhv) on the map one stands on, taken away (hhh) while unjudged; the dig (hgj) judges a
    /// stage's flags.
    /// </para>
    /// <para>
    /// A step is the capture's «find the landmark that way» -- «882 to the south» --: the first map
    /// that way, within ten, that shows a landmark none before it shows, as the players' own
    /// pairing of maps and landmarks has it (TreasureHuntPois). Where no landmark fits, it is the
    /// client's «follow the direction for N maps» (hgr) instead. The hunts go by the world map's
    /// squares, walking round whatever is in the way, among the outdoor maps one can walk to from
    /// the capture's start. A stage starts where the last one was dug.
    /// </para>
    /// <para>
    /// Kept in world.db, in a table of its own created at first use:
    /// </para>
    /// <code>
    ///   CharacterTreasureHunts (CharacterId, QuestType, Legendary, Checkpoint, Retries, StartMap,
    ///                           Steps, Flags, Unchanged, StartedMs)
    /// </code>
    /// </remarks>
    public static class TreasureHunts
    {
        /// <summary>The hunt types (hgi): «Búsqueda del Tesoro», «Caza de portal», «Caza legendaria».</summary>
        public const int Classic = 0, Portal = 1, Legendary = 2;

        /// <summary>
        /// A hunt asked for (hhk f2): «Acción imposible», «no hay búsqueda», «ya tienes una»,
        /// «no disponible», «límite superado», and the one the client says nothing to.
        /// </summary>
        public const int RequestImpossible = 0, NoQuestFound = 1, AlreadyHaveQuest = 2, NotAvailable = 3,
                         LimitExceeded = 4, RequestOk = 5;

        /// <summary>A flag put or taken (hhq f1): only 1 is silent, 6 is «ya hay una en este mapa».</summary>
        public const int FlagError = 0, FlagOk = 1, FlagTooMany = 3, FlagImpossible = 4, FlagWrongIndex = 5,
                         FlagSameMap = 6;

        /// <summary>
        /// A dig (hht f1): the stage done, the hunt done, the flags wrong (with how many), the hunt
        /// lost, not on the last flag's map, and digging again with the same wrong flags.
        /// </summary>
        public const int DigImpossible = 0, DigNewStage = 1, DigFinished = 2, DigWrong = 3, DigLost = 4,
                         DigWrongMap = 5, DigNothingNew = 6;

        /// <summary>A flag's state (hha f1): put, judged right, judged wrong.</summary>
        public const int FlagUnknown = 0, FlagRight = 1, FlagWrong = 2;

        /// <summary>The directions a step goes (lbo): east, south, west, north.</summary>
        public const int East = 0, South = 2, West = 4, North = 6;

        /// <summary>The capture's hunt: five stages of five steps, four tries.</summary>
        public const int Checkpoints = 5, StepsPerCheckpoint = 5, Tries = 4;

        /// <summary>
        /// How far a step goes: «chaque indice se trouve dans un rayon de 1 à 10 maps du point de
        /// départ ou du dernier indice trouvé» (DofusPourLesNoobs, Les chasses aux trésors).
        /// </summary>
        public const int FewestMaps = 1, MostMaps = 10;

        /// <summary>
        /// How long a hunt cannot be given up. Measured: 9 and 18 seconds after the start, the real
        /// server answered «termina tu misión actual o espera 1 minuto/s más» both times.
        /// </summary>
        public static readonly TimeSpan GiveUpLock = TimeSpan.FromMinutes(1);

        /// <summary>The capture's start, [2,4]: the hunts go on the outdoor maps one can walk to from there.</summary>
        public const long MeasuredStart = 88081171;

        public sealed class Step
        {
            public int Direction { get; init; }
            public int Maps { get; init; }
            public long Target { get; init; }

            /// <summary>The landmark to look for, or 0 for a «follow the direction for N maps» step.</summary>
            public int Poi { get; init; }
        }

        public sealed class Flag
        {
            public long MapId { get; set; }
            public int State { get; set; }
        }

        public sealed class Hunt
        {
            public long CharacterId { get; init; }
            public int Type { get; init; }

            /// <summary>The legendary hunt's id in the client's data, 0 for the others.</summary>
            public int Legendary { get; init; }

            /// <summary>The quest the hunt was launched for (a treasure chest's), 0 for none.</summary>
            public int Quest { get; init; }

            /// <summary>How many stages it has (hhf f4) and how many steps each (f8).</summary>
            public int Stages { get; init; } = Checkpoints;
            public int StepsPerStage { get; init; } = StepsPerCheckpoint;

            public int Checkpoint { get; set; }
            public int Retries { get; set; } = Tries;
            public long StartMap { get; set; }
            public List<Step> Steps { get; set; } = new();
            public List<Flag> Flags { get; set; } = new();

            /// <summary>Whether no flag has moved since the last dig.</summary>
            public bool Unchanged { get; set; }

            public long StartedMs { get; init; }

            /// <summary>The steps the client is shown: the flagged ones and the next.</summary>
            public int Known => Math.Min(Steps.Count, Flags.Count + 1);

            public bool LastCheckpoint => Checkpoint + 1 >= Stages;

            /// <summary>Whether the last stage's flags are all judged right: the fight is to be had.</summary>
            public bool ReadyToFight => LastCheckpoint && Flags.Count == Steps.Count &&
                                        Flags.All(f => f.State == FlagRight);
        }

        // ─── The world the hunts walk ───────────────────────────────────────

        /// <summary>The outdoor maps a hunt can lead to, by square.</summary>
        public sealed class Grid
        {
            private readonly Dictionary<(int X, int Y), long> _byPosition = new();
            private readonly Dictionary<long, (int X, int Y)> _positions = new();

            public Grid(IEnumerable<(long MapId, int X, int Y)> maps)
            {
                foreach (var (mapId, x, y) in maps)
                {
                    _positions[mapId] = (x, y);
                    _byPosition.TryAdd((x, y), mapId);
                }
            }

            public IReadOnlyCollection<long> Maps => _positions.Keys;

            public bool Contains(long mapId) => _positions.ContainsKey(mapId);

            public (int X, int Y)? PositionOf(long mapId)
                => _positions.TryGetValue(mapId, out var position) ? position : null;

            /// <summary>The map on that square, or 0.</summary>
            public long MapAt(int x, int y) => _byPosition.TryGetValue((x, y), out long mapId) ? mapId : 0;

            /// <summary>Whether two maps are the same square of the hunts' world.</summary>
            public bool SamePlace(long a, long b)
            {
                if (a == b) return true;
                var pa = PositionOf(a);
                return pa != null && pa == PositionOf(b);
            }
        }

        private static Grid? _world;
        private static readonly object _worldLock = new();

        /// <summary>
        /// The outdoor maps one can walk to from the capture's start, through the edges the
        /// world data joins (MapScrolls). Empty when the maps are not loaded.
        /// </summary>
        public static Grid World
        {
            get
            {
                if (_world != null) return _world;
                lock (_worldLock)
                {
                    if (_world != null) return _world;
                    var reached = new List<(long, int, int)>();
                    var seen = new HashSet<long>();
                    var queue = new Queue<long>();
                    if (MapManager.GetMapInfo(MeasuredStart) is { Outdoor: true })
                    {
                        queue.Enqueue(MeasuredStart);
                        seen.Add(MeasuredStart);
                    }
                    while (queue.Count > 0)
                    {
                        long mapId = queue.Dequeue();
                        var info = MapManager.GetMapInfo(mapId);
                        if (info == null) continue;
                        reached.Add((mapId, info.PosX, info.PosY));
                        var scroll = MapManager.GetScrollAction(mapId);
                        if (scroll == null) continue;
                        foreach (long next in new[] { scroll.RightMapId, scroll.BottomMapId, scroll.LeftMapId, scroll.TopMapId })
                        {
                            if (next <= 0 || !seen.Add(next)) continue;
                            if (MapManager.GetMapInfo(next) is not { Outdoor: true }) continue;
                            queue.Enqueue(next);
                        }
                    }
                    _world = new Grid(reached);
                    Console.WriteLine($"[Hunts] {reached.Count} outdoor maps for the hunts.");
                    return _world;
                }
            }
        }

        private static (int Dx, int Dy) Delta(int direction) => direction switch
        {
            East => (1, 0),
            South => (0, 1),
            West => (-1, 0),
            _ => (0, -1),
        };

        private static readonly int[] Directions = { East, South, West, North };

        /// <summary>
        /// A stage's steps from a map: each so many squares one way, never straight back the way
        /// the last came, never twice on the same map nor on the start. Null when none fits.
        /// </summary>
        public static List<Step>? Route(Grid grid, long from, Random dice, int length = StepsPerCheckpoint)
            => Route(grid, from, dice, TreasureHuntPois.Of, length);

        /// <summary>
        /// The same, a step a landmark whenever one fits: the first map that way showing a landmark
        /// no map before it shows (<paramref name="poisOf"/>).
        /// </summary>
        public static List<Step>? Route(Grid grid, long from, Random dice, Func<long, IReadOnlyList<int>> poisOf,
                                        int length = StepsPerCheckpoint)
        {
            var start = grid.PositionOf(from);
            if (start == null) return null;

            for (int attempt = 0; attempt < 50; attempt++)
            {
                var steps = new List<Step>();
                var used = new HashSet<long> { from };
                var (x, y) = start.Value;
                int previous = -1;
                while (steps.Count < length)
                {
                    Step? found = null;
                    for (int tries = 0; tries < 40 && found == null; tries++)
                    {
                        int direction = Directions[dice.Next(Directions.Length)];
                        if (previous >= 0 && direction == (previous + 4) % 8) continue;
                        found = LandmarkStep(grid, x, y, direction, used, poisOf, dice);
                        if (found != null) break;
                        int maps = dice.Next(FewestMaps, MostMaps + 1);
                        var (dx, dy) = Delta(direction);
                        long target = grid.MapAt(x + dx * maps, y + dy * maps);
                        if (target == 0 || used.Contains(target)) continue;
                        found = new Step { Direction = direction, Maps = maps, Target = target };
                    }
                    if (found == null) break;
                    steps.Add(found);
                    used.Add(found.Target);
                    (x, y) = grid.PositionOf(found.Target)!.Value;
                    previous = found.Direction;
                }
                if (steps.Count == length) return steps;
            }
            return null;
        }

        /// <summary>
        /// A landmark step from a square one way: a landmark of a map within ten that no map before
        /// it that way shows, drawn among all such; null when there is none.
        /// </summary>
        private static Step? LandmarkStep(Grid grid, int x, int y, int direction, HashSet<long> used,
                                          Func<long, IReadOnlyList<int>> poisOf, Random dice)
        {
            var (dx, dy) = Delta(direction);
            var seen = new HashSet<int>();
            var fitting = new List<Step>();
            for (int maps = FewestMaps; maps <= MostMaps; maps++)
            {
                long mapId = grid.MapAt(x + dx * maps, y + dy * maps);
                if (mapId == 0) continue;
                var pois = poisOf(mapId);
                if (!used.Contains(mapId))
                    foreach (int poi in pois.Where(p => !seen.Contains(p)).Distinct())
                        fitting.Add(new Step { Direction = direction, Maps = maps, Target = mapId, Poi = poi });
                seen.UnionWith(pois);
            }
            return fitting.Count == 0 ? null : fitting[dice.Next(fitting.Count)];
        }

        /// <summary>
        /// A new hunt: its start drawn among the maps a whole stage can be laid from -- and, when
        /// <paramref name="startsOn"/> says so, among those it accepts.
        /// </summary>
        public static Hunt? Begin(Grid grid, long characterId, int type, int legendary, long nowMs, Random dice,
                                  int stages = Checkpoints, int stepsPerStage = StepsPerCheckpoint, int quest = 0,
                                  Func<long, bool>? startsOn = null)
        {
            var maps = grid.Maps.ToList();
            if (startsOn != null)
            {
                var fitting = maps.Where(startsOn).ToList();
                if (fitting.Count > 0) maps = fitting;
            }
            if (maps.Count == 0) return null;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                long start = maps[dice.Next(maps.Count)];
                var steps = Route(grid, start, dice, stepsPerStage);
                if (steps == null) continue;
                return new Hunt
                {
                    CharacterId = characterId,
                    Type = type,
                    Legendary = legendary,
                    Quest = quest,
                    Stages = stages,
                    StepsPerStage = stepsPerStage,
                    StartMap = start,
                    Steps = steps,
                    StartedMs = nowMs,
                };
            }
            return null;
        }

        // ─── The rules ──────────────────────────────────────────────────────

        /// <summary>
        /// A flag put on step <paramref name="index"/>, on the map one stands on: the next free
        /// one, or again on one judged wrong. Never two on the same map, never on a right one.
        /// </summary>
        public static int PutFlag(Grid grid, Hunt hunt, int index, long mapId)
        {
            if (index < 0 || index >= hunt.Steps.Count) return FlagTooMany;
            if (index > hunt.Flags.Count) return FlagWrongIndex;
            for (int i = 0; i < hunt.Flags.Count; i++)
                if (i != index && grid.SamePlace(hunt.Flags[i].MapId, mapId)) return FlagSameMap;

            if (index == hunt.Flags.Count)
            {
                hunt.Flags.Add(new Flag { MapId = mapId, State = FlagUnknown });
            }
            else
            {
                var flag = hunt.Flags[index];
                if (flag.State == FlagRight) return FlagImpossible;
                flag.MapId = mapId;
                flag.State = FlagUnknown;
            }
            hunt.Unchanged = false;
            return FlagOk;
        }

        /// <summary>
        /// A flag taken away, and those after it: the client asks for each of them in turn, and a
        /// step's flag means nothing without the one before. A right one stays.
        /// </summary>
        public static int RemoveFlag(Hunt hunt, int index)
        {
            if (index < 0 || index >= hunt.Steps.Count) return FlagWrongIndex;
            if (index >= hunt.Flags.Count) return FlagOk;
            if (hunt.Flags[index].State == FlagRight) return FlagImpossible;
            hunt.Flags.RemoveRange(index, hunt.Flags.Count - index);
            hunt.Unchanged = false;
            return FlagOk;
        }

        /// <summary>
        /// A dig on <paramref name="mapId"/>: every step flagged, standing on the last flag. Each flag
        /// judged; a wrong one costs a try, the last try the hunt. All right, the next stage starts
        /// here -- or, on the last, the fight is to be had (<see cref="DigFinished"/>).
        /// </summary>
        public static (int Result, int Wrong) Dig(Grid grid, Hunt hunt, long mapId, Random dice)
        {
            if (hunt.Flags.Count < hunt.Steps.Count) return (DigImpossible, 0);
            if (!grid.SamePlace(hunt.Flags[^1].MapId, mapId)) return (DigWrongMap, 0);
            if (hunt.ReadyToFight) return (DigFinished, 0);
            if (hunt.Unchanged) return (DigNothingNew, 0);

            int wrong = 0;
            for (int i = 0; i < hunt.Steps.Count; i++)
            {
                bool right = grid.SamePlace(hunt.Flags[i].MapId, hunt.Steps[i].Target);
                hunt.Flags[i].State = right ? FlagRight : FlagWrong;
                if (!right) wrong++;
            }
            hunt.Unchanged = true;

            if (wrong > 0)
            {
                hunt.Retries--;
                return (hunt.Retries <= 0 ? DigLost : DigWrong, wrong);
            }
            if (hunt.LastCheckpoint) return (DigFinished, 0);

            var steps = Route(grid, hunt.Steps[^1].Target, dice, hunt.StepsPerStage);
            if (steps == null) return (DigImpossible, 0);
            hunt.Checkpoint++;
            hunt.StartMap = hunt.Steps[^1].Target;
            hunt.Steps = steps;
            hunt.Flags = new List<Flag>();
            hunt.Unchanged = false;
            return (DigNewStage, 0);
        }

        /// <summary>The minutes still to wait before giving the hunt up, rounded up; 0 when it can be.</summary>
        public static int MinutesBeforeGivingUp(Hunt hunt, long nowMs)
        {
            long left = hunt.StartedMs + (long)GiveUpLock.TotalMilliseconds - nowMs;
            return left <= 0 ? 0 : (int)((left + 59_999) / 60_000);
        }

        // ─── Who has which ──────────────────────────────────────────────────

        private static readonly ConcurrentDictionary<long, Dictionary<int, Hunt>> _known = new();
        private static readonly object _tableLock = new();
        private static bool _tableReady;

        private static Dictionary<int, Hunt> Of(long characterId) => _known.GetOrAdd(characterId, Read);

        /// <summary>The character's hunt of that type, or null.</summary>
        public static Hunt? Get(long characterId, int type)
        {
            var hunts = Of(characterId);
            lock (hunts) return hunts.TryGetValue(type, out var hunt) ? hunt : null;
        }

        /// <summary>Every hunt the character has, by type.</summary>
        public static IReadOnlyList<Hunt> All(long characterId)
        {
            var hunts = Of(characterId);
            lock (hunts) return hunts.Values.ToList();
        }

        /// <summary>A hunt kept as it now is.</summary>
        public static void Save(Hunt hunt)
        {
            var hunts = Of(hunt.CharacterId);
            lock (hunts) hunts[hunt.Type] = hunt;
            Store(hunt);
        }

        /// <summary>A hunt over: given up, lost or won.</summary>
        public static void End(long characterId, int type)
        {
            var hunts = Of(characterId);
            lock (hunts) hunts.Remove(type);
            Delete(characterId, type);
        }

        /// <summary>For the tests: forget what was read, so the next read goes to the base.</summary>
        internal static void Forget(long characterId) => _known.TryRemove(characterId, out _);

        internal static string WriteSteps(IEnumerable<Step> steps)
            => string.Join(";", steps.Select(s => s.Poi > 0
                ? $"{s.Direction},{s.Maps},{s.Target},{s.Poi}"
                : $"{s.Direction},{s.Maps},{s.Target}"));

        internal static List<Step> ReadSteps(string text)
            => text.Split(';', StringSplitOptions.RemoveEmptyEntries)
                   .Select(part => part.Split(','))
                   .Select(p => new Step
                   {
                       Direction = int.Parse(p[0]), Maps = int.Parse(p[1]), Target = long.Parse(p[2]),
                       Poi = p.Length > 3 ? int.Parse(p[3]) : 0,
                   })
                   .ToList();

        internal static string WriteFlags(IEnumerable<Flag> flags)
            => string.Join(";", flags.Select(f => $"{f.MapId},{f.State}"));

        internal static List<Flag> ReadFlags(string text)
            => text.Split(';', StringSplitOptions.RemoveEmptyEntries)
                   .Select(part => part.Split(','))
                   .Select(p => new Flag { MapId = long.Parse(p[0]), State = int.Parse(p[1]) })
                   .ToList();

        private static Dictionary<int, Hunt> Read(long characterId)
        {
            var hunts = new Dictionary<int, Hunt>();
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT QuestType, Legendary, Checkpoint, Retries, StartMap, Steps, Flags, Unchanged, StartedMs, " +
                                      "Quest, Stages, StepsPerStage FROM CharacterTreasureHunts WHERE CharacterId = $c;";
                command.Parameters.AddWithValue("$c", characterId);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var hunt = new Hunt
                    {
                        CharacterId = characterId,
                        Type = reader.GetInt32(0),
                        Legendary = reader.GetInt32(1),
                        Checkpoint = reader.GetInt32(2),
                        Retries = reader.GetInt32(3),
                        StartMap = reader.GetInt64(4),
                        Steps = ReadSteps(reader.GetString(5)),
                        Flags = ReadFlags(reader.GetString(6)),
                        Unchanged = reader.GetInt32(7) != 0,
                        StartedMs = reader.GetInt64(8),
                        Quest = reader.GetInt32(9),
                        Stages = reader.GetInt32(10),
                        StepsPerStage = reader.GetInt32(11),
                    };
                    hunts[hunt.Type] = hunt;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Hunts] Could not read the hunts of {characterId}: {ex.Message}");
            }
            return hunts;
        }

        private static void Store(Hunt hunt)
        {
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "INSERT INTO CharacterTreasureHunts (CharacterId, QuestType, Legendary, Checkpoint, Retries, StartMap, " +
                    "Steps, Flags, Unchanged, StartedMs, Quest, Stages, StepsPerStage) " +
                    "VALUES ($c, $t, $l, $k, $r, $s, $st, $f, $u, $m, $q, $g, $p) " +
                    "ON CONFLICT(CharacterId, QuestType) DO UPDATE SET Legendary = excluded.Legendary, " +
                    "Checkpoint = excluded.Checkpoint, Retries = excluded.Retries, StartMap = excluded.StartMap, " +
                    "Steps = excluded.Steps, Flags = excluded.Flags, Unchanged = excluded.Unchanged, StartedMs = excluded.StartedMs, " +
                    "Quest = excluded.Quest, Stages = excluded.Stages, StepsPerStage = excluded.StepsPerStage;";
                command.Parameters.AddWithValue("$c", hunt.CharacterId);
                command.Parameters.AddWithValue("$t", hunt.Type);
                command.Parameters.AddWithValue("$l", hunt.Legendary);
                command.Parameters.AddWithValue("$k", hunt.Checkpoint);
                command.Parameters.AddWithValue("$r", hunt.Retries);
                command.Parameters.AddWithValue("$s", hunt.StartMap);
                command.Parameters.AddWithValue("$st", WriteSteps(hunt.Steps));
                command.Parameters.AddWithValue("$f", WriteFlags(hunt.Flags));
                command.Parameters.AddWithValue("$u", hunt.Unchanged ? 1 : 0);
                command.Parameters.AddWithValue("$m", hunt.StartedMs);
                command.Parameters.AddWithValue("$q", hunt.Quest);
                command.Parameters.AddWithValue("$g", hunt.Stages);
                command.Parameters.AddWithValue("$p", hunt.StepsPerStage);
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Hunts] Could not store the hunt of {hunt.CharacterId}: {ex.Message}");
            }
        }

        private static void Delete(long characterId, int type)
        {
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM CharacterTreasureHunts WHERE CharacterId = $c AND QuestType = $t;";
                command.Parameters.AddWithValue("$c", characterId);
                command.Parameters.AddWithValue("$t", type);
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Hunts] Could not remove the hunt of {characterId}: {ex.Message}");
            }
        }

        private static void EnsureTable()
        {
            if (_tableReady) return;
            lock (_tableLock)
            {
                if (_tableReady) return;
                using var connection = Open();
                using var create = connection.CreateCommand();
                create.CommandText = @"
                    CREATE TABLE IF NOT EXISTS CharacterTreasureHunts (
                        CharacterId INTEGER NOT NULL,
                        QuestType   INTEGER NOT NULL,
                        Legendary   INTEGER NOT NULL DEFAULT 0,
                        Checkpoint  INTEGER NOT NULL DEFAULT 0,
                        Retries     INTEGER NOT NULL,
                        StartMap    INTEGER NOT NULL,
                        Steps       TEXT    NOT NULL,
                        Flags       TEXT    NOT NULL DEFAULT '',
                        Unchanged   INTEGER NOT NULL DEFAULT 0,
                        StartedMs   INTEGER NOT NULL,
                        PRIMARY KEY (CharacterId, QuestType));";
                create.ExecuteNonQuery();

                // The columns the classic hunts brought, on a table made before them.
                var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var columns = connection.CreateCommand())
                {
                    columns.CommandText = "PRAGMA table_info(CharacterTreasureHunts);";
                    using var reader = columns.ExecuteReader();
                    while (reader.Read()) have.Add(reader.GetString(1));
                }
                foreach (var (column, fallback) in new[] { ("Quest", 0), ("Stages", Checkpoints), ("StepsPerStage", StepsPerCheckpoint) })
                {
                    if (have.Contains(column)) continue;
                    using var alter = connection.CreateCommand();
                    alter.CommandText = $"ALTER TABLE CharacterTreasureHunts ADD COLUMN {column} INTEGER NOT NULL DEFAULT {fallback};";
                    alter.ExecuteNonQuery();
                }
                _tableReady = true;
            }
        }

        private static SqliteConnection Open()
        {
            var connection = new SqliteConnection(DatabaseManager.WorldConnectionString);
            connection.Open();
            return connection;
        }
    }
}

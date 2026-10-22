using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The ladder window's rankings (LadderUI): experience, achievements, the Koliseo and the infinite
    /// dreams, ranked from what the server keeps of every character.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured in «Combate/koliseo completo con invitacion-koli 2vs2-hasta final de combate»,
    /// where the player goes through every tab: each list is the top <see cref="Shown"/> lines,
    /// ranked 1 to 100 even where scores tie (a hundred characters with 22926 achievement points
    /// are 1, 2, 3...), and the player's own line comes apart, with his rank however far down.
    /// </para>
    /// <para>
    /// What each line shows is read off the client (bo.moq/mor/mos/mot and each tab's
    /// SetCommonInfos): the rank, the class and sex, the name, the level, the server, and the
    /// tab's score -- the experience, the achievement points, the Koliseo's win rate, league and
    /// rating, a dream's intensity, waves and turn and the day it was done.
    /// </para>
    /// <para>
    /// The dreams' order is the client's own rule («ui.ladder.infiniteDreamRules»): the intensity,
    /// then the waves beaten in the final fight, then the turn the last of them fell on, then the
    /// day, give or take an hour; only who fought every fight of the dream, and neither a souvenir
    /// nor a trial. Each character keeps his best dream.
    /// </para>
    /// <para>
    /// INFERRED: the line's character number (ipx f7). The real server's is the character's id
    /// without its server (its id is f7 &lt;&lt; 16 | server); these ids carry no server, so the
    /// whole id goes.
    /// </para>
    /// </remarks>
    public static class Ladders
    {
        /// <summary>The lines a ladder shows: the capture's 100.</summary>
        public const int Shown = 100;

        /// <summary>A character as a ladder line shows him (ipx).</summary>
        public sealed record Person(long Id, int Server, int Breed, int Sex, string Name, int Level);

        public sealed record ExperienceLine(int Rank, Person Who, long Experience);

        public sealed record AchievementLine(int Rank, Person Who, int Points);

        public sealed record KoliseoLine(int Rank, Person Who, int WinRate, int League, int Rating);

        public sealed record DreamLine(int Rank, Person Who, bool Dreamer, int Intensity, int Waves, int Turn, DateTime DoneUtc);

        /// <summary>A dream finished, as one of its people ranks it.</summary>
        public sealed record DreamRecord(long CharacterId, bool Dreamer, int Intensity, int Waves, int Turn, DateTime DoneUtc);

        /// <summary>For tests: the people the ladders rank, in place of the base's characters.</summary>
        internal static Func<IReadOnlyList<(Person Who, long Experience)>>? PeopleOverride;

        private static readonly object Gate = new object();
        private static bool _schema;

        // ─── The rankings ───────────────────────────────────────────────────────────────────

        /// <summary>Every character of a server (0 for all) by experience.</summary>
        public static List<ExperienceLine> Experience(int server)
            => Rank(People(server).OrderByDescending(p => p.Experience).ThenByDescending(p => p.Who.Level).ThenBy(p => p.Who.Id),
                    (rank, p) => new ExperienceLine(rank, p.Who, p.Experience));

        /// <summary>Every character of a server (0 for all) by achievement points.</summary>
        public static List<AchievementLine> Achievement(int server)
        {
            var points = Achievements.AllPoints();
            return Rank(People(server).Select(p => (p.Who, Points: points.TryGetValue(p.Who.Id, out int n) ? n : 0))
                                      .OrderByDescending(p => p.Points).ThenBy(p => p.Who.Id),
                        (rank, p) => new AchievementLine(rank, p.Who, p.Points));
        }

        /// <summary>
        /// A Koliseo mode's placed characters of the season, of every server, by rating; only one class
        /// and one band of levels when asked (the tab's filters: a class, and «20 a 29» ... «200»).
        /// </summary>
        public static List<KoliseoLine> Koliseo(int mode, int breed = 0, int firstLevel = 0)
        {
            var people = People(0).ToDictionary(p => p.Who.Id, p => p.Who);
            var placed = KoliseoLadder.PlacedIn(mode)
                .Where(s => people.ContainsKey(s.CharacterId))
                .Select(s => (Who: people[s.CharacterId], Standing: s))
                .Where(p => breed <= 0 || p.Who.Breed == breed)
                .Where(p => firstLevel <= 0 || InBand(p.Who.Level, firstLevel))
                .OrderByDescending(p => p.Standing.Rating).ThenBy(p => p.Who.Id);
            return Rank(placed, (rank, p) => new KoliseoLine(rank, p.Who, WinRate(p.Standing.SeasonWins, p.Standing.SeasonFights),
                                                             p.Standing.League, p.Standing.Rating));
        }

        /// <summary>The level filter's bands: ten levels from 20, and 200 alone (with what is above it).</summary>
        public static bool InBand(int level, int firstLevel)
            => firstLevel >= 200 ? level >= 200 : level >= firstLevel && level <= firstLevel + 9;

        /// <summary>The share of the season's fights won, in percent.</summary>
        public static int WinRate(int wins, int fights)
            => fights <= 0 ? 0 : (int)Math.Round(100.0 * wins / fights, MidpointRounding.AwayFromZero);

        /// <summary>Every character of a server (0 for all) with a dream to his name, by his best one.</summary>
        public static List<DreamLine> Dreams(int server)
        {
            var people = People(server).ToDictionary(p => p.Who.Id, p => p.Who);
            var records = AllDreamRecords().Where(r => people.ContainsKey(r.CharacterId)).OrderBy(r => r, DreamOrder);
            return Rank(records, (rank, r) => new DreamLine(rank, people[r.CharacterId], r.Dreamer, r.Intensity, r.Waves, r.Turn, r.DoneUtc));
        }

        /// <summary>The first <see cref="Shown"/> lines.</summary>
        public static List<T> Top<T>(IEnumerable<T> lines) => lines.Take(Shown).ToList();

        /// <summary>The lines whose character's name holds the text searched, whatever the case.</summary>
        public static List<T> Named<T>(IEnumerable<T> lines, Func<T, Person> who, string text)
            => string.IsNullOrWhiteSpace(text)
                ? new List<T>()
                : lines.Where(l => who(l).Name.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase)).Take(Shown).ToList();

        private static List<TLine> Rank<TItem, TLine>(IEnumerable<TItem> ordered, Func<int, TItem, TLine> line)
            => ordered.Select((item, i) => line(i + 1, item)).ToList();

        // ─── The dreams' order and records ──────────────────────────────────────────────────

        /// <summary>
        /// The client's rule: intensity, waves, then the earlier turn, then the earlier day «ajustando a
        /// más/menos una hora» -- the same hour ties, and the dreamer goes before his people.
        /// </summary>
        public static readonly IComparer<DreamRecord> DreamOrder = Comparer<DreamRecord>.Create((a, b) =>
        {
            int c = b.Intensity.CompareTo(a.Intensity);
            if (c == 0) c = b.Waves.CompareTo(a.Waves);
            if (c == 0) c = a.Turn.CompareTo(b.Turn);
            if (c == 0) c = Hour(a.DoneUtc).CompareTo(Hour(b.DoneUtc));
            if (c == 0) c = b.Dreamer.CompareTo(a.Dreamer);
            if (c == 0) c = a.CharacterId.CompareTo(b.CharacterId);
            return c;
        });

        private static DateTime Hour(DateTime utc) => new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// A dream finished: each of its people keeps it when it ranks above his best one. Returns the
        /// records that were kept.
        /// </summary>
        public static List<DreamRecord> RecordDream(IEnumerable<DreamRecord> records)
        {
            var kept = new List<DreamRecord>();
            lock (Gate)
            {
                using var connection = Open();
                foreach (var record in records)
                {
                    var best = ReadDream(connection, record.CharacterId);
                    if (best != null && DreamOrder.Compare(best, record) <= 0) continue;
                    using var write = connection.CreateCommand();
                    write.CommandText =
                        "INSERT OR REPLACE INTO DreamRecords (CharacterId, Dreamer, Intensity, Waves, Turn, DoneMs) " +
                        "VALUES ($c, $d, $i, $w, $t, $ms);";
                    write.Parameters.AddWithValue("$c", record.CharacterId);
                    write.Parameters.AddWithValue("$d", record.Dreamer ? 1 : 0);
                    write.Parameters.AddWithValue("$i", record.Intensity);
                    write.Parameters.AddWithValue("$w", record.Waves);
                    write.Parameters.AddWithValue("$t", record.Turn);
                    write.Parameters.AddWithValue("$ms", new DateTimeOffset(DateTime.SpecifyKind(record.DoneUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds());
                    write.ExecuteNonQuery();
                    kept.Add(record);
                }
            }
            return kept;
        }

        /// <summary>A character's best dream, or null.</summary>
        public static DreamRecord? BestDreamOf(long characterId)
        {
            lock (Gate)
            {
                using var connection = Open();
                return ReadDream(connection, characterId);
            }
        }

        /// <summary>For tests: a character's dream forgotten.</summary>
        internal static void EraseDream(long characterId)
        {
            lock (Gate)
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM DreamRecords WHERE CharacterId = $c;";
                command.Parameters.AddWithValue("$c", characterId);
                command.ExecuteNonQuery();
            }
        }

        private static List<DreamRecord> AllDreamRecords()
        {
            var all = new List<DreamRecord>();
            lock (Gate)
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT CharacterId, Dreamer, Intensity, Waves, Turn, DoneMs FROM DreamRecords;";
                using var reader = command.ExecuteReader();
                while (reader.Read()) all.Add(DreamOf(reader));
            }
            return all;
        }

        private static DreamRecord? ReadDream(SqliteConnection connection, long characterId)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT CharacterId, Dreamer, Intensity, Waves, Turn, DoneMs FROM DreamRecords WHERE CharacterId = $c;";
            command.Parameters.AddWithValue("$c", characterId);
            using var reader = command.ExecuteReader();
            return reader.Read() ? DreamOf(reader) : null;
        }

        private static DreamRecord DreamOf(SqliteDataReader reader)
            => new DreamRecord(reader.GetInt64(0), reader.GetInt32(1) != 0, reader.GetInt32(2), reader.GetInt32(3),
                               reader.GetInt32(4), DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(5)).UtcDateTime);

        private static SqliteConnection Open()
        {
            var connection = new SqliteConnection(DatabaseManager.WorldConnectionString);
            connection.Open();
            if (_schema) return connection;
            using var create = connection.CreateCommand();
            create.CommandText = @"
                CREATE TABLE IF NOT EXISTS DreamRecords (
                    CharacterId INTEGER PRIMARY KEY,
                    Dreamer INTEGER NOT NULL,
                    Intensity INTEGER NOT NULL,
                    Waves INTEGER NOT NULL,
                    Turn INTEGER NOT NULL,
                    DoneMs INTEGER NOT NULL
                );";
            create.ExecuteNonQuery();
            _schema = true;
            return connection;
        }

        // ─── The people ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Every character of a server (0 for all), with his experience: what the base keeps, never
        /// below his level's floor (DatabaseManager does the same when he logs in).
        /// </summary>
        private static IReadOnlyList<(Person Who, long Experience)> People(int server)
        {
            var all = PeopleOverride?.Invoke() ?? ReadPeople();
            return server <= 0 ? all : all.Where(p => p.Who.Server == server).ToList();
        }

        private static List<(Person, long)> ReadPeople()
        {
            var people = new List<(Person, long)>();
            try
            {
                using var connection = new SqliteConnection(DatabaseManager.WorldConnectionString);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT Id, COALESCE(ServerId, $server), Breed, Sex, Name, Level, COALESCE(Experience, 0) FROM Characters;";
                command.Parameters.AddWithValue("$server", DatabaseManager.DefaultServerId);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int level = reader.GetInt32(5);
                    people.Add((new Person(reader.GetInt64(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3),
                                           reader.GetString(4), level),
                                Math.Max(reader.GetInt64(6), ExperienceTable.LevelFloor(level))));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Ladders] The characters could not be read: {ex.Message}");
            }
            return people;
        }
    }
}

using System;
using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// A character's alignment: neutral, Bontarian or Brakmarian, his grade and when it last changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On the wire it is the client's alignment block (llp, read by giq.bkjt into its eih): f2 the
    /// side, f4 the grade, f5 the level, and in the sheet f3 { f3: 500 } besides. The sides are 2
    /// neutral, 3 and 4 the two cities: a brand new character's kub carries 2, and the client draws
    /// wings only for a side above 2 with a grade, picking them as «grade + (side - 3) · 10». Over
    /// the captures' players, 107 are side 3, 51 side 2 and 7 side 4, none with a grade: 3 is Bonta,
    /// the city most players swear to, and the order is the old game's (angel, then evil), each one
    /// up by two. The criterion «Ps» counts them the old way: 0 neutral, 1 Bonta, 2 Brakmar.
    /// </para>
    /// <para>
    /// Swearing is Chantal Inea's, in Incarnam: «No puedes dirigirte al Mundo de los Doce sin jurar
    /// lealtad a una ciudad o a otra.». Becoming neutral again is Morry Gann's, in Amakna's militia:
    /// «entre los 500 y los 256 000 kamas, según tu implicación con la milicia» -- ten steps, each
    /// double the last -- and never twice within «dos semanas» (AlignmentHandler). No grade is
    /// earned on this server: there are no honour points to earn it with.
    /// </para>
    /// <para>
    /// Kept in world.db, in a table of its own created at first use:
    /// <c>CharacterAlignments (CharacterId, Side, Grade, ChangedMs)</c>.
    /// </para>
    /// </remarks>
    public static class Alignments
    {
        public const int Neutral = 2, Bonta = 3, Brakmar = 4;

        /// <summary>How long after a change no other is taken: «hace menos de dos semanas».</summary>
        public static readonly TimeSpan ChangeLock = TimeSpan.FromDays(14);

        /// <summary>What becoming neutral costs at the first of its ten steps; each one doubles it.</summary>
        public const long FirstLeaveCost = 500;
        public const int LeaveSteps = 10;

        public sealed record Alignment(int Side, int Grade, long ChangedMs)
        {
            public bool Sworn => Side == Bonta || Side == Brakmar;
        }

        private static readonly Alignment Unsworn = new(Neutral, 0, 0);
        private static readonly ConcurrentDictionary<long, Alignment> _known = new();
        private static readonly object _tableLock = new();
        private static bool _tableReady;

        public static Alignment Of(long characterId)
            => characterId == 0 ? Unsworn : _known.GetOrAdd(characterId, Read);

        /// <summary>The side as the criterion «Ps» counts it: 0 neutral, 1 Bonta, 2 Brakmar.</summary>
        public static int CriterionSide(long characterId) => Of(characterId).Side - Neutral;

        /// <summary>Which of the ten steps becoming neutral costs him: his grade, up to the last.</summary>
        public static int LeaveStep(Alignment alignment) => Math.Clamp(alignment.Grade, 0, LeaveSteps - 1);

        public static long LeaveCost(int step) => FirstLeaveCost << Math.Clamp(step, 0, LeaveSteps - 1);

        /// <summary>Whether his last change is old enough for another.</summary>
        public static bool MayChange(Alignment alignment, long nowMs)
            => alignment.ChangedMs == 0 || nowMs - alignment.ChangedMs >= (long)ChangeLock.TotalMilliseconds;

        /// <summary>Sworn to a city, or back to neutral: kept, with when.</summary>
        public static Alignment Set(long characterId, int side, long nowMs)
        {
            var alignment = new Alignment(side, 0, nowMs);
            _known[characterId] = alignment;
            Store(characterId, alignment);
            return alignment;
        }

        /// <summary>For the tests: forget what was read.</summary>
        internal static void Forget(long characterId) => _known.TryRemove(characterId, out _);

        private static void EnsureTable(SqliteConnection connection)
        {
            if (_tableReady) return;
            lock (_tableLock)
            {
                if (_tableReady) return;
                var command = connection.CreateCommand();
                command.CommandText = @"CREATE TABLE IF NOT EXISTS CharacterAlignments (
                        CharacterId INTEGER PRIMARY KEY,
                        Side        INTEGER NOT NULL,
                        Grade       INTEGER NOT NULL DEFAULT 0,
                        ChangedMs   INTEGER NOT NULL DEFAULT 0);";
                command.ExecuteNonQuery();
                _tableReady = true;
            }
        }

        private static Alignment Read(long characterId)
        {
            try
            {
                using var connection = new SqliteConnection(DatabaseManager.WorldConnectionString);
                connection.Open();
                EnsureTable(connection);
                var command = connection.CreateCommand();
                command.CommandText = "SELECT Side, Grade, ChangedMs FROM CharacterAlignments WHERE CharacterId = $id;";
                command.Parameters.AddWithValue("$id", characterId);
                using var reader = command.ExecuteReader();
                return reader.Read() ? new Alignment(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt64(2)) : Unsworn;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Alignment] Could not read {characterId}'s alignment: {ex.Message}");
                return Unsworn;
            }
        }

        private static void Store(long characterId, Alignment alignment)
        {
            try
            {
                using var connection = new SqliteConnection(DatabaseManager.WorldConnectionString);
                connection.Open();
                EnsureTable(connection);
                var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO CharacterAlignments (CharacterId, Side, Grade, ChangedMs) " +
                                      "VALUES ($id, $side, $grade, $when) ON CONFLICT(CharacterId) DO UPDATE SET " +
                                      "Side = excluded.Side, Grade = excluded.Grade, ChangedMs = excluded.ChangedMs;";
                command.Parameters.AddWithValue("$id", characterId);
                command.Parameters.AddWithValue("$side", alignment.Side);
                command.Parameters.AddWithValue("$grade", alignment.Grade);
                command.Parameters.AddWithValue("$when", alignment.ChangedMs);
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Alignment] Could not keep {characterId}'s alignment: {ex.Message}");
            }
        }
    }
}

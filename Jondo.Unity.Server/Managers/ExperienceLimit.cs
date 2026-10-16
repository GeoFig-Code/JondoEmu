using System;
using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The limit a player sets on the experience he earns: «Limitar la experiencia recibida a», in
    /// the characteristics panel and the experience bar's menu. A percent, 100 by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client sends it in a kts and is answered with the hox that carries it (see
    /// <see cref="Handlers.ExperienceLimitHandler"/>). It applies it itself to the experience its
    /// monster-group tooltip shows: xp × the limit / 100, truncated, the limit kept between 0 and
    /// 100 (lg.nzb). And it puts its warning, «Has limitado tu ganancia de experiencia al {0}%»,
    /// beside the experience of the end of a fight, of a quest's rewards and of the Koliseo's.
    /// Those are the gains it cuts here; achievements, guild raid rewards and experience scrolls
    /// carry no warning in the client and are paid whole.
    /// </para>
    /// <para>
    /// Kept in world.db, in a table of its own created at first use -- datos/world.zip, the base CI
    /// tests against, has never heard of it --, one row per character that has ever changed it:
    /// </para>
    /// <code>
    ///   CharacterExperienceLimit (CharacterId, Percent)
    /// </code>
    /// </remarks>
    public static class ExperienceLimit
    {
        /// <summary>No limit: every point of experience earned.</summary>
        public const int Full = 100;

        private static readonly ConcurrentDictionary<long, int> _known = new();
        private static readonly object _tableLock = new object();
        private static volatile bool _tableReady;

        /// <summary>The character's limit, in percent; <see cref="Full"/> when he never set one.</summary>
        public static int Of(long characterId)
        {
            if (characterId == 0) return Full;
            return _known.GetOrAdd(characterId, Read);
        }

        /// <summary>Stores the character's limit, between 0 and 100, and returns what was stored.</summary>
        public static int Set(long characterId, int percent)
        {
            int value = Math.Clamp(percent, 0, Full);
            if (characterId == 0) return value;
            _known[characterId] = value;
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO CharacterExperienceLimit (CharacterId, Percent) VALUES ($c, $p) " +
                                      "ON CONFLICT(CharacterId) DO UPDATE SET Percent = excluded.Percent;";
                command.Parameters.AddWithValue("$c", characterId);
                command.Parameters.AddWithValue("$p", value);
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Experience] Could not store the experience limit of {characterId}: {ex.Message}");
            }
            return value;
        }

        /// <summary>Some experience under a limit: xp × limit / 100, truncated, as the client cuts its tooltip.</summary>
        public static long Apply(long experience, int percent)
        {
            if (experience <= 0) return 0;
            return experience * Math.Clamp(percent, 0, Full) / Full;
        }

        /// <summary>Some experience under the character's own limit.</summary>
        public static long ApplyFor(long characterId, long experience) => Apply(experience, Of(characterId));

        /// <summary>For the tests: forget what was read, so the next read goes to the base.</summary>
        internal static void Forget(long characterId) => _known.TryRemove(characterId, out _);

        private static int Read(long characterId)
        {
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT Percent FROM CharacterExperienceLimit WHERE CharacterId = $c;";
                command.Parameters.AddWithValue("$c", characterId);
                return command.ExecuteScalar() is long percent ? (int)Math.Clamp(percent, 0, Full) : Full;
            }
            catch (Exception)
            {
                return Full;
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
                    CREATE TABLE IF NOT EXISTS CharacterExperienceLimit (
                        CharacterId INTEGER PRIMARY KEY,
                        Percent     INTEGER NOT NULL);";
                create.ExecuteNonQuery();
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

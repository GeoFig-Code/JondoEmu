using System;
using System.Collections.Concurrent;
using Jondo.Unity.World.Fights;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// A character's energy: characteristic 29, <c>energyPoints</c>, out of the 10,000 of 47,
    /// <c>maxEnergyPoints</c>. What a lost fight spends -- see <see cref="DefeatPenalty"/> --, and
    /// what comes back with the consumables (<see cref="Handlers.RecoveryItemHandler"/>) and with
    /// time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kept in world.db, in a table of its own created at first use -- datos/world.zip, the base CI
    /// tests against, has never heard of it --, one row per character that has lost any: a
    /// character without a row has the full gauge, which is what every captured character
    /// fresh from rest carries (10000 in the kub of the Pandala capture).
    /// </para>
    /// <code>
    ///   CharacterEnergy (CharacterId, Energy, UpdatedUtc)
    /// </code>
    /// <para>
    /// It comes back at <see cref="RegainPerSecond"/> a second, connected or not, from the moment it
    /// was last written: the stored figure is what it was then, and every read adds the seconds since.
    /// The rate is the server owner's choice: the help text says it returns with rest -- "durante tu
    /// tiempo de desconexión", twice as fast in a house, a tavern or the class temple -- and the
    /// captures show it back at 10,000 a week later, but none measures how fast.
    /// </para>
    /// </remarks>
    public static class Energy
    {
        /// <summary>The energy that comes back each second, by the server owner's choice.</summary>
        public const int RegainPerSecond = 1;

        /// <summary>The energy as last written, and when.</summary>
        internal readonly record struct Stored(int Energy, DateTime SinceUtc);

        private static readonly ConcurrentDictionary<long, Stored> _known = new();
        private static readonly object _tableLock = new object();
        private static volatile bool _tableReady;

        /// <summary>The character's energy now: the stored figure and what time has given back, or the full gauge.</summary>
        public static int Of(long characterId)
        {
            if (characterId == 0) return DefeatPenalty.MaxEnergy;
            return At(_known.GetOrAdd(characterId, Read), DateTime.UtcNow);
        }

        /// <summary>A stored energy at a moment: what it was, plus a point a second since, within the gauge.</summary>
        internal static int At(Stored stored, DateTime nowUtc)
        {
            long seconds = Math.Max(0, (long)(nowUtc - stored.SinceUtc).TotalSeconds);
            return (int)Math.Min(DefeatPenalty.MaxEnergy, stored.Energy + RegainPerSecond * seconds);
        }

        /// <summary>Stores the character's energy, within the gauge, as of now.</summary>
        public static void Set(long characterId, int energy)
        {
            if (characterId == 0) return;
            int value = Math.Clamp(energy, 0, DefeatPenalty.MaxEnergy);
            var now = DateTime.UtcNow;
            _known[characterId] = new Stored(value, now);
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO CharacterEnergy (CharacterId, Energy, UpdatedUtc) VALUES ($c, $e, $t) " +
                                      "ON CONFLICT(CharacterId) DO UPDATE SET Energy = excluded.Energy, UpdatedUtc = excluded.UpdatedUtc;";
                command.Parameters.AddWithValue("$c", characterId);
                command.Parameters.AddWithValue("$e", value);
                command.Parameters.AddWithValue("$t", new DateTimeOffset(now).ToUnixTimeMilliseconds());
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Energy] Could not store the energy of {characterId}: {ex.Message}");
            }
        }

        /// <summary>For the tests: forget what was read, so the next read goes to the base.</summary>
        internal static void Forget(long characterId) => _known.TryRemove(characterId, out _);

        private static Stored Read(long characterId)
        {
            var now = DateTime.UtcNow;
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT Energy, UpdatedUtc FROM CharacterEnergy WHERE CharacterId = $c;";
                command.Parameters.AddWithValue("$c", characterId);
                using var reader = command.ExecuteReader();
                if (!reader.Read()) return new Stored(DefeatPenalty.MaxEnergy, now);

                int energy = (int)Math.Clamp(reader.GetInt64(0), 0, DefeatPenalty.MaxEnergy);
                // A row from before the time was kept starts counting now, not from the epoch.
                var since = reader.IsDBNull(1) ? now : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)).UtcDateTime;
                return new Stored(energy, since);
            }
            catch (Exception)
            {
                return new Stored(DefeatPenalty.MaxEnergy, now);
            }
        }

        private static void EnsureTable()
        {
            if (_tableReady) return;
            lock (_tableLock)
            {
                if (_tableReady) return;
                using var connection = Open();
                using (var create = connection.CreateCommand())
                {
                    create.CommandText = @"
                        CREATE TABLE IF NOT EXISTS CharacterEnergy (
                            CharacterId INTEGER PRIMARY KEY,
                            Energy      INTEGER NOT NULL,
                            UpdatedUtc  INTEGER);";
                    create.ExecuteNonQuery();
                }

                // A table made before the time was kept: the column comes in empty.
                bool hasTime = false;
                using (var columns = connection.CreateCommand())
                {
                    columns.CommandText = "PRAGMA table_info(CharacterEnergy);";
                    using var reader = columns.ExecuteReader();
                    while (reader.Read())
                        if (string.Equals(reader.GetString(1), "UpdatedUtc", StringComparison.OrdinalIgnoreCase)) hasTime = true;
                }
                if (!hasTime)
                {
                    using var add = connection.CreateCommand();
                    add.CommandText = "ALTER TABLE CharacterEnergy ADD COLUMN UpdatedUtc INTEGER;";
                    add.ExecuteNonQuery();
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

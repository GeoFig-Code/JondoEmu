using System;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// An alliance's nugget pot: what its members' recycling at their tax collectors adds to it,
    /// until someone with the right hands it out (see <see cref="Handlers.RecycleHandler"/>).
    /// </summary>
    /// <remarks>
    /// Kept in world.db, in a table created at first use:
    /// <code>
    ///   AllianceNuggetPots (AllianceId, Pot)
    /// </code>
    /// </remarks>
    public static class AllianceNuggets
    {
        private static readonly object _gate = new object();
        private static volatile bool _tableReady;

        /// <summary>The nuggets in the alliance's pot.</summary>
        public static long PotOf(long allianceId)
        {
            if (allianceId <= 0) return 0;
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT Pot FROM AllianceNuggetPots WHERE AllianceId = $a;";
                command.Parameters.AddWithValue("$a", allianceId);
                return command.ExecuteScalar() is long pot ? pot : 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Nuggets] Could not read the pot of alliance {allianceId}: {ex.Message}");
                return 0;
            }
        }

        /// <summary>Puts nuggets in the pot and returns what it holds now.</summary>
        public static long Add(long allianceId, long amount)
        {
            if (allianceId <= 0 || amount <= 0) return PotOf(allianceId);
            lock (_gate)
            {
                Write("INSERT INTO AllianceNuggetPots (AllianceId, Pot) VALUES ($a, $n) " +
                      "ON CONFLICT(AllianceId) DO UPDATE SET Pot = Pot + excluded.Pot;", allianceId, amount);
                return PotOf(allianceId);
            }
        }

        /// <summary>Takes nuggets out of the pot; false, and nothing taken, when it holds fewer.</summary>
        public static bool TryTake(long allianceId, long amount)
        {
            if (allianceId <= 0 || amount <= 0) return false;
            lock (_gate)
            {
                if (PotOf(allianceId) < amount) return false;
                Write("UPDATE AllianceNuggetPots SET Pot = Pot - $n WHERE AllianceId = $a;", allianceId, amount);
                return true;
            }
        }

        private static void Write(string sql, long allianceId, long amount)
        {
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.Parameters.AddWithValue("$a", allianceId);
                command.Parameters.AddWithValue("$n", amount);
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Nuggets] Could not change the pot of alliance {allianceId}: {ex.Message}");
            }
        }

        private static void EnsureTable()
        {
            if (_tableReady) return;
            lock (_gate)
            {
                if (_tableReady) return;
                using var connection = Open();
                using var create = connection.CreateCommand();
                create.CommandText = @"
                    CREATE TABLE IF NOT EXISTS AllianceNuggetPots (
                        AllianceId INTEGER PRIMARY KEY,
                        Pot        INTEGER NOT NULL);";
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

using System;
using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The zaap each character saved his position at: where a lost fight and the recall potion take
    /// him, and what the zaap window's list says (hjj f2).
    /// </summary>
    /// <remarks>
    /// The zaap window's «Guardar» button sends hip, an empty message (the client's
    /// BaseZaapSelectionUI.OnSaveCurrentZaap, through eoa); the info message 6 is «Posición
    /// guardada.». No capture presses it, which is why every character's position used to be the
    /// zaap it starts beside. Kept in world.db:
    /// <code>
    ///   CharacterSavedPositions (CharacterId, MapId)
    /// </code>
    /// </remarks>
    public static class SavedPositions
    {
        /// <summary>«Posición guardada.», info message 6.</summary>
        public const int SavedMessage = 6;

        private static readonly ConcurrentDictionary<long, long> _cache = new();
        private static readonly object _gate = new object();
        private static volatile bool _tableReady;

        /// <summary>The map the character saved his position at, or 0 for none.</summary>
        public static long Of(long characterId)
        {
            if (characterId <= 0) return 0;
            if (_cache.TryGetValue(characterId, out long cached)) return cached;
            long map = 0;
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT MapId FROM CharacterSavedPositions WHERE CharacterId = $c;";
                command.Parameters.AddWithValue("$c", characterId);
                map = command.ExecuteScalar() is long found ? found : 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Zaap] Could not read {characterId}'s saved position: {ex.Message}");
            }
            _cache[characterId] = map;
            return map;
        }

        /// <summary>Saves the character's position at that zaap's map.</summary>
        public static void Save(long characterId, long mapId)
        {
            if (characterId <= 0 || mapId <= 0) return;
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT OR REPLACE INTO CharacterSavedPositions (CharacterId, MapId) VALUES ($c, $m);";
                command.Parameters.AddWithValue("$c", characterId);
                command.Parameters.AddWithValue("$m", mapId);
                command.ExecuteNonQuery();
                _cache[characterId] = mapId;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Zaap] Could not save {characterId}'s position at {mapId}: {ex.Message}");
            }
        }

        /// <summary>Forgets a character's, for the tests.</summary>
        internal static void Forget(long characterId)
        {
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM CharacterSavedPositions WHERE CharacterId = $c;";
                command.Parameters.AddWithValue("$c", characterId);
                command.ExecuteNonQuery();
            }
            catch (Exception)
            {
                // nothing saved, nothing to forget
            }
            _cache.TryRemove(characterId, out _);
        }

        private static void EnsureTable()
        {
            if (_tableReady) return;
            lock (_gate)
            {
                if (_tableReady) return;
                using var connection = Open();
                using var create = connection.CreateCommand();
                create.CommandText = "CREATE TABLE IF NOT EXISTS CharacterSavedPositions (" +
                                     "CharacterId INTEGER PRIMARY KEY, MapId INTEGER NOT NULL);";
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

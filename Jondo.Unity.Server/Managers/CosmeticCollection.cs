using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The account's cosmetics collection: the appearance items taken out of the bag and kept for
    /// the appearance window, one of each template, with the effects they had (a living object
    /// keeps its level and its looks).
    /// </summary>
    /// <remarks>
    /// MEASURED in «Apariencias/añadir capa objeviva y anillo objevivo a coleccion cosmeticos» and
    /// «Apariencias/retirar varias cosas de coleccion de cosmeticos…»: an item goes in by its uid
    /// and comes back out by its template, each time with a new uid; the whole collection arrives
    /// on entering the world (lwt). Until now that lwt was the captured account's, 153 items with
    /// its uids, replayed to everybody. Kept in world.db:
    /// <code>
    ///   AccountCosmetics (AccountId, Uid, Gid, Quantity, Effects)
    /// </code>
    /// </remarks>
    public static class CosmeticCollection
    {
        public sealed class Entry
        {
            public long Uid { get; init; }
            public int Gid { get; init; }
            public int Quantity { get; init; } = 1;
            public string Effects { get; init; } = "";
        }

        private static readonly object _gate = new object();
        private static volatile bool _tableReady;

        /// <summary>
        /// The table, and its uids kept out of the dispenser's way: they come from the same one as
        /// every item's, which starts above CharacterItems' highest only (as the bank's do).
        /// </summary>
        public static void Initialize() => EnsureTable();

        /// <summary>Whether an item may go into the collection: an appearance item of the client's catalogue.</summary>
        public static bool Takes(int gid) => Cosmetics.Of(gid) != null;

        /// <summary>The account's collection, in the order it was filled.</summary>
        public static List<Entry> Of(long accountId)
        {
            var entries = new List<Entry>();
            if (accountId <= 0) return entries;
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT Uid, Gid, Quantity, Effects FROM AccountCosmetics WHERE AccountId = $a ORDER BY rowid;";
                command.Parameters.AddWithValue("$a", accountId);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    entries.Add(new Entry
                    {
                        Uid = reader.GetInt64(0), Gid = reader.GetInt32(1), Quantity = reader.GetInt32(2),
                        Effects = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Cosmetics] Could not read account {accountId}'s collection: {ex.Message}");
            }
            return entries;
        }

        /// <summary>Whether the account has that template in its collection.</summary>
        public static bool Has(long accountId, int gid) => Of(accountId).Any(e => e.Gid == gid);

        /// <summary>Keeps an item, under a new uid. Null when the account has that template already.</summary>
        public static Entry? Add(long accountId, int gid, string effects)
        {
            if (accountId <= 0 || Has(accountId, gid)) return null;
            var entry = new Entry { Uid = DatabaseManager.NextItemUid(), Gid = gid, Quantity = 1, Effects = effects ?? "" };
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO AccountCosmetics (AccountId, Uid, Gid, Quantity, Effects) VALUES ($a, $u, $g, $q, $e);";
                command.Parameters.AddWithValue("$a", accountId);
                command.Parameters.AddWithValue("$u", entry.Uid);
                command.Parameters.AddWithValue("$g", entry.Gid);
                command.Parameters.AddWithValue("$q", entry.Quantity);
                command.Parameters.AddWithValue("$e", entry.Effects);
                command.ExecuteNonQuery();
                return entry;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Cosmetics] Could not keep {gid} for account {accountId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Takes a template out of the collection: what it was, or null when it was not there.</summary>
        public static Entry? Take(long accountId, int gid)
        {
            var entry = Of(accountId).FirstOrDefault(e => e.Gid == gid);
            if (entry == null) return null;
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM AccountCosmetics WHERE AccountId = $a AND Uid = $u;";
                command.Parameters.AddWithValue("$a", accountId);
                command.Parameters.AddWithValue("$u", entry.Uid);
                return command.ExecuteNonQuery() > 0 ? entry : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Cosmetics] Could not take {gid} out of account {accountId}'s collection: {ex.Message}");
                return null;
            }
        }

        /// <summary>Forgets an account's collection, for the tests.</summary>
        internal static void Forget(long accountId)
        {
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM AccountCosmetics WHERE AccountId = $a;";
                command.Parameters.AddWithValue("$a", accountId);
                command.ExecuteNonQuery();
            }
            catch (Exception)
            {
                // nothing kept, nothing to forget
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
                create.CommandText = "CREATE TABLE IF NOT EXISTS AccountCosmetics (" +
                                     "AccountId INTEGER NOT NULL, Uid INTEGER NOT NULL PRIMARY KEY, Gid INTEGER NOT NULL, " +
                                     "Quantity INTEGER NOT NULL, Effects TEXT); " +
                                     "CREATE INDEX IF NOT EXISTS IX_AccountCosmetics_Account ON AccountCosmetics (AccountId);";
                create.ExecuteNonQuery();
                using var highest = connection.CreateCommand();
                highest.CommandText = "SELECT MAX(Uid) FROM AccountCosmetics;";
                if (highest.ExecuteScalar() is long max) DatabaseManager.KeepUidsAbove(max);
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

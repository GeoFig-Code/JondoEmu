using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The gifts waiting on an account: what the shop's gifts window lists and hands to one of its
    /// characters (see <see cref="Handlers.GiftHandler"/>).
    /// </summary>
    /// <remarks>
    /// On Ankama's servers they are what the account bought or was given -- the shop, the codes,
    /// the events. Here an administrator grants them from the F10 window. Kept in world.db, in a
    /// table created at first use, one row per gift, gone once it is handed out:
    /// <code>
    ///   AccountGifts (Id, AccountId, Gid, Quantity, Title, Description, Created)
    /// </code>
    /// </remarks>
    public static class Gifts
    {
        public sealed class Gift
        {
            public int Id { get; init; }
            public long AccountId { get; init; }
            public int Gid { get; init; }
            public int Quantity { get; init; }
            public string Title { get; init; } = "";
            public string Description { get; init; } = "";
        }

        private static readonly object _gate = new object();
        private static volatile bool _tableReady;

        /// <summary>Puts a gift on an account and returns its id, or 0 if it could not be stored.</summary>
        public static int Grant(long accountId, int gid, int quantity, string title, string description)
        {
            if (accountId <= 0 || gid <= 0 || quantity <= 0) return 0;
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO AccountGifts (AccountId, Gid, Quantity, Title, Description, Created) " +
                                      "VALUES ($a, $g, $q, $t, $d, $c); SELECT last_insert_rowid();";
                command.Parameters.AddWithValue("$a", accountId);
                command.Parameters.AddWithValue("$g", gid);
                command.Parameters.AddWithValue("$q", quantity);
                command.Parameters.AddWithValue("$t", title ?? "");
                command.Parameters.AddWithValue("$d", description ?? "");
                command.Parameters.AddWithValue("$c", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                return command.ExecuteScalar() is long id ? (int)id : 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gifts] Could not give {gid} to account {accountId}: {ex.Message}");
                return 0;
            }
        }

        /// <summary>The account's gifts, oldest first.</summary>
        public static IReadOnlyList<Gift> Of(long accountId)
        {
            var found = new List<Gift>();
            if (accountId <= 0) return found;
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT Id, Gid, Quantity, Title, Description FROM AccountGifts WHERE AccountId = $a ORDER BY Id;";
                command.Parameters.AddWithValue("$a", accountId);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    found.Add(new Gift
                    {
                        Id = reader.GetInt32(0), AccountId = accountId, Gid = reader.GetInt32(1),
                        Quantity = reader.GetInt32(2), Title = reader.GetString(3), Description = reader.GetString(4),
                    });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gifts] Could not read the gifts of account {accountId}: {ex.Message}");
            }
            return found;
        }

        /// <summary>Takes one of the account's gifts away to hand it out; null if it has no such gift.</summary>
        public static Gift? Take(long accountId, int giftId)
        {
            lock (_gate)
            {
                Gift? gift = null;
                foreach (var g in Of(accountId))
                    if (g.Id == giftId) gift = g;
                if (gift == null) return null;
                try
                {
                    using var connection = Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = "DELETE FROM AccountGifts WHERE Id = $i AND AccountId = $a;";
                    command.Parameters.AddWithValue("$i", giftId);
                    command.Parameters.AddWithValue("$a", accountId);
                    return command.ExecuteNonQuery() > 0 ? gift : null;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Gifts] Could not take gift {giftId} of account {accountId}: {ex.Message}");
                    return null;
                }
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
                    CREATE TABLE IF NOT EXISTS AccountGifts (
                        Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                        AccountId   INTEGER NOT NULL,
                        Gid         INTEGER NOT NULL,
                        Quantity    INTEGER NOT NULL,
                        Title       TEXT NOT NULL,
                        Description TEXT NOT NULL,
                        Created     INTEGER NOT NULL);";
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

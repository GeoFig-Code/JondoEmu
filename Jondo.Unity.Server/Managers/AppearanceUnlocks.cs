using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The faces, bodies and poses an account has unlocked for the appearance window, beyond the
    /// ones every character may take with nothing unlocked.
    /// </summary>
    /// <remarks>
    /// They are unlocked by using an item: a «Cara» (type 306, effect 3796 «Cara adicional»), a
    /// «Cuerpo» (329, effect 4060 «Desbloquea un cuerpo») or a «Poses» (330, effect 4061
    /// «Desbloquea una pose»), each effect's value a head, body or idle id -- the Ocra's «Cara
    /// vanguardista» carries heads 616 and 617, one per sex. The client reads them as the
    /// collection's lists: KnownHead asks its inventory service (slot 19) for the lwt's f3, KnownBody
    /// (slot 21) for its f5 and KnownIdle (slot 23) for its f4; lyb { f1 ids, f3 item, f4 kind }
    /// adds to the one its kind names (eqq::bbhc). They are the account's, as the collection is.
    /// </remarks>
    public static class AppearanceUnlocks
    {
        /// <summary>The lyb's f4 (the client's lwi): what its f1 ids are.</summary>
        public enum Kind { Cosmetic = 0, Head = 1, Body = 2, Idle = 3 }

        public const int HeadEffect = 3796, BodyEffect = 4060, IdleEffect = 4061;

        private static readonly object _gate = new object();
        private static volatile bool _tableReady;

        /// <summary>What an item unlocks, read off its effects; null when it unlocks nothing.</summary>
        public static (Kind Kind, List<int> Ids)? Of(IReadOnlyList<DatabaseManager.ItemEffectData> effects)
        {
            foreach (var (effect, kind) in new[] { (HeadEffect, Kind.Head), (BodyEffect, Kind.Body), (IdleEffect, Kind.Idle) })
            {
                var ids = effects.Where(e => e.EffectId == effect && e.Value > 0).Select(e => e.Value).Distinct().ToList();
                if (ids.Count > 0) return (kind, ids);
            }
            return null;
        }

        /// <summary>Whether the account has unlocked that face, body or pose.</summary>
        public static bool Has(long accountId, Kind kind, int id) => All(accountId, kind).Contains(id);

        /// <summary>The account's unlocked ids of that kind, in the order they were unlocked.</summary>
        public static List<int> All(long accountId, Kind kind)
        {
            var ids = new List<int>();
            if (accountId <= 0) return ids;
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT Id FROM AccountAppearanceUnlocks WHERE AccountId = $a AND Kind = $k ORDER BY rowid;";
                command.Parameters.AddWithValue("$a", accountId);
                command.Parameters.AddWithValue("$k", (int)kind);
                using var reader = command.ExecuteReader();
                while (reader.Read()) ids.Add(reader.GetInt32(0));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Appearances] Could not read the unlocks of {accountId}: {ex.Message}");
            }
            return ids;
        }

        /// <summary>Unlocks those ids for the account; the ones it did not have yet.</summary>
        public static List<int> Add(long accountId, Kind kind, IEnumerable<int> ids)
        {
            var added = new List<int>();
            try
            {
                EnsureTable();
                using var connection = Open();
                foreach (int id in ids)
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = "INSERT OR IGNORE INTO AccountAppearanceUnlocks (AccountId, Kind, Id) VALUES ($a, $k, $i);";
                    command.Parameters.AddWithValue("$a", accountId);
                    command.Parameters.AddWithValue("$k", (int)kind);
                    command.Parameters.AddWithValue("$i", id);
                    if (command.ExecuteNonQuery() > 0) added.Add(id);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Appearances] Could not unlock for {accountId}: {ex.Message}");
            }
            return added;
        }

        /// <summary>Forgets an account's unlocks, for the tests.</summary>
        internal static void Forget(long accountId)
        {
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM AccountAppearanceUnlocks WHERE AccountId = $a;";
                command.Parameters.AddWithValue("$a", accountId);
                command.ExecuteNonQuery();
            }
            catch (Exception)
            {
                // nothing unlocked, nothing to forget
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
                create.CommandText = "CREATE TABLE IF NOT EXISTS AccountAppearanceUnlocks (" +
                                     "AccountId INTEGER NOT NULL, Kind INTEGER NOT NULL, Id INTEGER NOT NULL, " +
                                     "PRIMARY KEY (AccountId, Kind, Id));";
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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jondo.Unity.Launcher;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The veteran rewards: what an account earns by its days of subscription, handed to it as gifts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client's 76 (datos/veteran_rewards_*.json, tools/extract_veteran_rewards.py): 5 «Silbar»
    /// scrolls at 7 days, the Miaudaga at 30, the Miaucapa at 90… up to the as'hesin set at 5,400.
    /// Its gifts window calls them «Recompensas a la veteranía» (text 1094738); the account's
    /// subscription days its veteran panel shows come from Ankama's web services, which this server
    /// does not answer, so the panel stays empty and the gifts arrive all the same.
    /// </para>
    /// <para>
    /// Every account is subscribed here from the day it was made, a year at a time (Subscription):
    /// its days are those since its subscription began, the end it was given less the year. Each
    /// reward goes once, written down in world.db:
    /// <code>
    ///   AccountVeteranRewards (AccountId, RewardId)
    /// </code>
    /// </para>
    /// </remarks>
    public static class VeteranRewards
    {
        public sealed record Reward(int Id, int Days, int Item, int Quantity);

        /// <summary>«Recompensas a la veteranía», the gifts' title.</summary>
        public const long TitleKey = 1094738;

        private static IReadOnlyList<Reward>? _rewards;
        private static readonly object _gate = new object();
        private static volatile bool _tableReady;

        public static IReadOnlyList<Reward> All
        {
            get
            {
                if (_rewards != null) return _rewards;
                lock (_gate) return _rewards ??= Read();
            }
        }

        private static IReadOnlyList<Reward> Read()
        {
            var read = new List<Reward>();
            string path = Paths.VeteranRewardsJson;
            if (!File.Exists(path)) return read;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var row in doc.RootElement.GetProperty("rewards").EnumerateArray())
                    read.Add(new Reward(row.GetProperty("id").GetInt32(), row.GetProperty("days").GetInt32(),
                                        row.GetProperty("item").GetInt32(), row.GetProperty("quantity").GetInt32()));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Veteran] {Path.GetFileName(path)} could not be read: {ex.Message}");
            }
            return read;
        }

        /// <summary>The rewards earned by so many days and not yet given.</summary>
        public static IEnumerable<Reward> Due(double days, IReadOnlySet<int> given)
            => All.Where(r => r.Days <= days && !given.Contains(r.Id));

        /// <summary>The days an account has been subscribed: since its subscription began.</summary>
        public static double SubscribedDays(string subscriptionEnd, DateTimeOffset now)
        {
            if (!DateTimeOffset.TryParse(subscriptionEnd, CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)) return 0;
            return Math.Max(0, (now - (end - Network.Subscription.Length)).TotalDays);
        }

        /// <summary>
        /// Puts on the account, as gifts, the rewards its days have earned and it has not had yet.
        /// Returns how many.
        /// </summary>
        public static int GrantDue(long accountId)
        {
            if (accountId <= 0 || All.Count == 0) return 0;
            try
            {
                EnsureTable();
                double days = SubscribedDays(DatabaseManager.GetSubscriptionEnd(accountId), DateTimeOffset.UtcNow);
                var due = Due(days, Given(accountId)).ToList();
                if (due.Count == 0) return 0;

                string title = Title();
                using var connection = Open();
                foreach (var reward in due)
                {
                    if (Gifts.Grant(accountId, reward.Item, reward.Quantity, title, "") == 0) continue;
                    using var mark = connection.CreateCommand();
                    mark.CommandText = "INSERT OR IGNORE INTO AccountVeteranRewards (AccountId, RewardId) VALUES ($a, $r);";
                    mark.Parameters.AddWithValue("$a", accountId);
                    mark.Parameters.AddWithValue("$r", reward.Id);
                    mark.ExecuteNonQuery();
                }
                Console.WriteLine($"[Veteran] Account {accountId}, {days:0} days subscribed: {due.Count} reward(s) given.");
                return due.Count;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Veteran] Could not give account {accountId} its rewards: {ex.Message}");
                return 0;
            }
        }

        private static HashSet<int> Given(long accountId)
        {
            var given = new HashSet<int>();
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT RewardId FROM AccountVeteranRewards WHERE AccountId = $a;";
            command.Parameters.AddWithValue("$a", accountId);
            using var reader = command.ExecuteReader();
            while (reader.Read()) given.Add(reader.GetInt32(0));
            return given;
        }

        private static string Title()
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Text FROM Translations WHERE Key = $key;";
            command.Parameters.AddWithValue("$key", TitleKey.ToString());
            return command.ExecuteScalar() as string ?? "";
        }

        private static void EnsureTable()
        {
            if (_tableReady) return;
            lock (_gate)
            {
                if (_tableReady) return;
                using var connection = Open();
                using var create = connection.CreateCommand();
                create.CommandText = "CREATE TABLE IF NOT EXISTS AccountVeteranRewards (" +
                                     "AccountId INTEGER NOT NULL, RewardId INTEGER NOT NULL, PRIMARY KEY (AccountId, RewardId));";
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

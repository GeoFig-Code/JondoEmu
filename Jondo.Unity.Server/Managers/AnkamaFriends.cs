using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The Ankama friends: the friendships between accounts that the chat service keeps, and the
    /// invitations still waiting for an answer (see <see cref="Network.ChatServer"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike the in-game contacts (<see cref="Contacts"/>), an Ankama friendship is mutual from
    /// the start: one account invites, the other accepts, and both have the friend. Kept in world.db,
    /// in tables created at first use:
    /// </para>
    /// <code>
    ///   AnkamaFriendships   (AccountId, FriendId)        one row each way
    ///   AnkamaFriendInvites (InviterId, RecipientId)
    /// </code>
    /// </remarks>
    public static class AnkamaFriends
    {
        private static readonly object _gate = new object();
        private static volatile bool _tablesReady;

        /// <summary>The account's friends, oldest first.</summary>
        public static IReadOnlyList<long> FriendsOf(long accountId)
            => Ids("SELECT FriendId FROM AnkamaFriendships WHERE AccountId = $a ORDER BY rowid;", accountId);

        public static bool AreFriends(long a, long b) => FriendsOf(a).Contains(b);

        /// <summary>Makes two accounts friends; false if they were already.</summary>
        public static bool Befriend(long a, long b)
        {
            if (a <= 0 || b <= 0 || a == b) return false;
            int added = Execute("INSERT OR IGNORE INTO AnkamaFriendships (AccountId, FriendId) VALUES ($a, $b), ($b, $a);", a, b);
            return added > 0;
        }

        /// <summary>Ends a friendship, on both sides; false if there was none.</summary>
        public static bool Unfriend(long a, long b)
            => Execute("DELETE FROM AnkamaFriendships WHERE (AccountId = $a AND FriendId = $b) OR (AccountId = $b AND FriendId = $a);", a, b) > 0;

        /// <summary>The invitations this account sent or received that wait for an answer: (inviter, recipient).</summary>
        public static IReadOnlyList<(long Inviter, long Recipient)> InvitesOf(long accountId)
        {
            var found = new List<(long, long)>();
            if (accountId <= 0) return found;
            try
            {
                EnsureTables();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT InviterId, RecipientId FROM AnkamaFriendInvites " +
                                      "WHERE InviterId = $a OR RecipientId = $a ORDER BY rowid;";
                command.Parameters.AddWithValue("$a", accountId);
                using var reader = command.ExecuteReader();
                while (reader.Read()) found.Add((reader.GetInt64(0), reader.GetInt64(1)));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Ankama friends] Could not read the invitations of {accountId}: {ex.Message}");
            }
            return found;
        }

        public static bool IsInvited(long inviter, long recipient)
            => InvitesOf(inviter).Contains((inviter, recipient));

        /// <summary>Stores an invitation; false if it was there already.</summary>
        public static bool Invite(long inviter, long recipient)
        {
            if (inviter <= 0 || recipient <= 0 || inviter == recipient) return false;
            return Execute("INSERT OR IGNORE INTO AnkamaFriendInvites (InviterId, RecipientId) VALUES ($a, $b);", inviter, recipient) > 0;
        }

        /// <summary>Takes an invitation away, answered or withdrawn; false if there was none.</summary>
        public static bool Withdraw(long inviter, long recipient)
            => Execute("DELETE FROM AnkamaFriendInvites WHERE InviterId = $a AND RecipientId = $b;", inviter, recipient) > 0;

        private static IReadOnlyList<long> Ids(string sql, long accountId)
        {
            var found = new List<long>();
            if (accountId <= 0) return found;
            try
            {
                EnsureTables();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.Parameters.AddWithValue("$a", accountId);
                using var reader = command.ExecuteReader();
                while (reader.Read()) found.Add(reader.GetInt64(0));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Ankama friends] Could not read the friends of {accountId}: {ex.Message}");
            }
            return found;
        }

        private static int Execute(string sql, long a, long b)
        {
            try
            {
                EnsureTables();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.Parameters.AddWithValue("$a", a);
                command.Parameters.AddWithValue("$b", b);
                return command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Ankama friends] Could not store {a} and {b}: {ex.Message}");
                return 0;
            }
        }

        private static void EnsureTables()
        {
            if (_tablesReady) return;
            lock (_gate)
            {
                if (_tablesReady) return;
                using var connection = Open();
                using var create = connection.CreateCommand();
                create.CommandText = @"
                    CREATE TABLE IF NOT EXISTS AnkamaFriendships (
                        AccountId INTEGER NOT NULL,
                        FriendId  INTEGER NOT NULL,
                        PRIMARY KEY (AccountId, FriendId));
                    CREATE TABLE IF NOT EXISTS AnkamaFriendInvites (
                        InviterId   INTEGER NOT NULL,
                        RecipientId INTEGER NOT NULL,
                        PRIMARY KEY (InviterId, RecipientId));";
                create.ExecuteNonQuery();
                _tablesReady = true;
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

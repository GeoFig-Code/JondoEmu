using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The quests a character follows: the small box beside the screen that tracks their
    /// objectives.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client's quest tracker (epw) says so itself: following a quest, or one of its
    /// objectives, sends iec { quest, objective or -1 }; no longer following it, idv { quest,
    /// objective or -1 } (epm.basn, on the action's flag). The real server answers neither. What it
    /// does is give the box back on entering the world, in an iel { f1 [ the journal's entry ] }
    /// -- one frame in the world-entry capture, with two quests --, which replaces the box.
    /// </para>
    /// <para>
    /// Kept by quest, in world.db, in a table created at first use:
    /// </para>
    /// <code>
    ///   CharacterFollowedQuests (CharacterId, QuestId)
    /// </code>
    /// </remarks>
    public static class FollowedQuests
    {
        private static readonly object _gate = new object();
        private static volatile bool _tableReady;

        /// <summary>The quests the character follows, in the order he took them up.</summary>
        public static IReadOnlyList<int> Of(long characterId)
        {
            var found = new List<int>();
            if (characterId <= 0) return found;
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT QuestId FROM CharacterFollowedQuests WHERE CharacterId = $c ORDER BY rowid;";
                command.Parameters.AddWithValue("$c", characterId);
                using var reader = command.ExecuteReader();
                while (reader.Read()) found.Add(reader.GetInt32(0));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Quests] Could not read the followed quests of {characterId}: {ex.Message}");
            }
            return found;
        }

        public static void Follow(long characterId, int questId)
            => Execute("INSERT OR IGNORE INTO CharacterFollowedQuests (CharacterId, QuestId) VALUES ($c, $q);", characterId, questId);

        public static void Unfollow(long characterId, int questId)
            => Execute("DELETE FROM CharacterFollowedQuests WHERE CharacterId = $c AND QuestId = $q;", characterId, questId);

        /// <summary>
        /// The box put in a new order (iek { 1 [quests] }): those named first, as named, then any
        /// other followed one as it was. MEASURED in «barco hacia vulkania…»: after following 724,
        /// 725, 730 and 726, frame 365 sends [730, 1869, 725, 726].
        /// </summary>
        public static void Order(long characterId, IReadOnlyList<int> quests)
        {
            if (characterId <= 0 || quests.Count == 0) return;
            var ordered = new List<int>();
            foreach (int quest in quests) if (quest > 0 && !ordered.Contains(quest)) ordered.Add(quest);
            foreach (int quest in Of(characterId)) if (!ordered.Contains(quest)) ordered.Add(quest);
            try
            {
                EnsureTable();
                using var connection = Open();
                using var transaction = connection.BeginTransaction();
                using (var clear = connection.CreateCommand())
                {
                    clear.Transaction = transaction;
                    clear.CommandText = "DELETE FROM CharacterFollowedQuests WHERE CharacterId = $c;";
                    clear.Parameters.AddWithValue("$c", characterId);
                    clear.ExecuteNonQuery();
                }
                foreach (int quest in ordered)
                {
                    using var insert = connection.CreateCommand();
                    insert.Transaction = transaction;
                    insert.CommandText = "INSERT OR IGNORE INTO CharacterFollowedQuests (CharacterId, QuestId) VALUES ($c, $q);";
                    insert.Parameters.AddWithValue("$c", characterId);
                    insert.Parameters.AddWithValue("$q", quest);
                    insert.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Quests] Could not order the followed quests of {characterId}: {ex.Message}");
            }
        }

        private static void Execute(string sql, long characterId, int questId)
        {
            if (characterId <= 0 || questId <= 0) return;
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.Parameters.AddWithValue("$c", characterId);
                command.Parameters.AddWithValue("$q", questId);
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Quests] Could not store the followed quest {questId} of {characterId}: {ex.Message}");
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
                    CREATE TABLE IF NOT EXISTS CharacterFollowedQuests (
                        CharacterId INTEGER NOT NULL,
                        QuestId     INTEGER NOT NULL,
                        PRIMARY KEY (CharacterId, QuestId));";
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

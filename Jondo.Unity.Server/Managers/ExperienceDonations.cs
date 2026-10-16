using System;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// Persistent combat donation settings and mount XP. Mounts currently have no breeding/level
    /// progression subsystem: their known level is registered here, and XP follows the item UID.
    /// Rates default to zero until an administrative tool or a future client handler sets them.
    /// </summary>
    public static class ExperienceDonations
    {
        private static SqliteConnection Open()
        {
            var connection = GuildStore.Open();
            using var schema = connection.CreateCommand();
            schema.CommandText = @"
                CREATE TABLE IF NOT EXISTS CombatDonationSettings (
                    CharacterId INTEGER PRIMARY KEY, MountPercent INTEGER NOT NULL DEFAULT 0,
                    GuildId INTEGER NOT NULL DEFAULT 0, GuildPercent INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE IF NOT EXISTS MountExperience (
                    ItemUid INTEGER PRIMARY KEY, Level INTEGER NOT NULL DEFAULT 1,
                    Experience INTEGER NOT NULL DEFAULT 0);";
            schema.ExecuteNonQuery();
            return connection;
        }

        /// <summary>Guild settings are bound to a membership, so leaving cannot donate to the next guild.</summary>
        public static void SetRates(long characterId, int mountPercent, long guildId, int guildPercent)
        {
            if (characterId <= 0 || guildId < 0 || mountPercent < 0 || mountPercent > 90
                || guildPercent < 0 || guildPercent > 90) throw new ArgumentOutOfRangeException();
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO CombatDonationSettings (CharacterId, MountPercent, GuildId, GuildPercent)
                VALUES ($c, $m, $g, $p) ON CONFLICT(CharacterId) DO UPDATE SET
                    MountPercent = $m, GuildId = $g, GuildPercent = $p;";
            command.Parameters.AddWithValue("$c", characterId);
            command.Parameters.AddWithValue("$m", mountPercent);
            command.Parameters.AddWithValue("$g", guildId);
            command.Parameters.AddWithValue("$p", guildPercent);
            command.ExecuteNonQuery();
        }

        public static (int Level, long Experience) MountOf(long itemUid)
        {
            using var connection = Open();
            return ReadMount(connection, null, itemUid);
        }

        /// <summary>Registers a mount's actual level without discarding XP already credited.</summary>
        public static void SetMountLevel(long itemUid, int level)
        {
            if (itemUid <= 0 || level < 1 || level > 100) throw new ArgumentOutOfRangeException();
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"INSERT INTO MountExperience (ItemUid, Level) VALUES ($u, $l)
                ON CONFLICT(ItemUid) DO UPDATE SET Level = $l;";
            command.Parameters.AddWithValue("$u", itemUid);
            command.Parameters.AddWithValue("$l", level);
            command.ExecuteNonQuery();
        }

        private static (int Level, long Experience) ReadMount(SqliteConnection connection,
                                                             SqliteTransaction transaction, long itemUid)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT Level, Experience FROM MountExperience WHERE ItemUid = $u;";
            command.Parameters.AddWithValue("$u", itemUid);
            using var reader = command.ExecuteReader();
            return reader.Read() ? (reader.GetInt32(0), reader.GetInt64(1)) : (1, 0);
        }

        /// <summary>
        /// Called once when planning each player's combat reward. The caller supplies only the UID
        /// of a real, equipped mount; zero means no mount (pets and petsmounts never receive XP).
        /// Recipient credits and the member's contribution are committed together before returning
        /// the remaining character XP. A missing guild never takes anything from the character.
        /// </summary>
        internal static CombatExperience.Allocation Credit(long characterId, long experience, int level, long mountUid)
        {
            if (experience <= 0) return default;
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            int mountPercent = 0, guildPercent = 0, guildLevel = 1;
            long guildId = 0;
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
                    SELECT s.MountPercent, s.GuildPercent, g.Id, g.Level
                    FROM CombatDonationSettings s
                    LEFT JOIN GuildMembers m ON m.CharacterId = s.CharacterId AND m.GuildId = s.GuildId
                    LEFT JOIN Guilds g ON g.Id = m.GuildId WHERE s.CharacterId = $c;";
                command.Parameters.AddWithValue("$c", characterId);
                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    mountPercent = mountUid > 0 ? reader.GetInt32(0) : 0;
                    if (!reader.IsDBNull(2))
                    {
                        guildPercent = reader.GetInt32(1);
                        guildId = reader.GetInt64(2);
                        guildLevel = reader.GetInt32(3);
                    }
                }
            }
            var mount = mountPercent > 0 ? ReadMount(connection, transaction, mountUid) : (Level: 1, Experience: 0L);
            var allocation = CombatExperience.Allocate(experience, level, mountPercent, mount.Level, guildPercent, guildLevel);
            if (allocation.Mount > 0)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO MountExperience (ItemUid, Experience) VALUES ($u, $x)
                    ON CONFLICT(ItemUid) DO UPDATE SET Experience = Experience + $x;";
                command.Parameters.AddWithValue("$u", mountUid);
                command.Parameters.AddWithValue("$x", allocation.Mount);
                command.ExecuteNonQuery();
            }
            if (allocation.Guild > 0)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
                    UPDATE GuildMembers SET Experience = Experience + $x WHERE CharacterId = $c AND GuildId = $g;
                    UPDATE Guilds SET Experience = Experience + $x WHERE Id = $g;";
                command.Parameters.AddWithValue("$c", characterId);
                command.Parameters.AddWithValue("$g", guildId);
                command.Parameters.AddWithValue("$x", allocation.Guild);
                command.ExecuteNonQuery();
                command.CommandText = "SELECT Experience FROM Guilds WHERE Id = $g;";
                long total = Convert.ToInt64(command.ExecuteScalar());
                command.CommandText = "UPDATE Guilds SET Level = $l WHERE Id = $g;";
                command.Parameters.AddWithValue("$l", GuildLevels.LevelFor(total));
                command.ExecuteNonQuery();
            }
            transaction.Commit();
            return allocation;
        }
    }
}

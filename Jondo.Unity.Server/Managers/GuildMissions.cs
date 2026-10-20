using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jondo.Unity.Launcher;
using Jondo.Unity.Server.Network;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The guild's weekly missions («Encargos»): twelve drawn each week from the client's catalogue
    /// (datos/guild_missions_*.json, tools/extract_guild_missions.py), in the ranks and categories
    /// the guild chose with its tier.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The capture «muchas acciones en mi gremio como líder» chooses tier 1 with no preferences and
    /// gets twelve: five regulations, three dungeons, two anomalies, a dream and a guild contract,
    /// of ranks 1 to 5 -- super categories 1 to 5. Its eight «Eventos» (super category 6) travel in
    /// another field and follow the game's live events, which this server does not run.
    /// </para>
    /// <para>
    /// The preferences are the jgs: f1 the super categories, f2 the ranks. Read in Ghidra:
    /// GuildMissionRanksAndCategoriesOption.SelectCategory writes the list at iy+24 and SelectRank
    /// the one at iy+16, and iy.nng fills +24 from jgs f1 and +16 from f2. None chosen is all.
    /// INFERRED: the draw is even among the missions those allow; the capture's spread fits it.
    /// </para>
    /// </remarks>
    public static class GuildMissions
    {
        /// <summary>How many missions a week has: «12 encargos por semana».</summary>
        public const int PerWeek = 12;

        /// <summary>The super category of the event missions, drawn by live events: left out.</summary>
        public const int EventsSuperCategory = 6;

        public sealed record Mission(int Id, string Name, int CategoryId, int SuperCategoryId, int RankId,
                                     int RecommendedLevel, int ActivityPoints, int Tokens, List<int> Objectives);

        public sealed record Objective(int Id, int MissionId, int Order);

        /// <summary>A mission of a guild's week: where it is, which, its uuid and its first objective.</summary>
        public sealed record Drawn(int Slot, int MissionId, string Uuid, int ObjectiveId);

        private sealed record Document(List<Mission> Missions, List<Objective> Objectives);

        private static Document _data;
        private static readonly object _lock = new();

        private static Document Data
        {
            get
            {
                if (_data != null) return _data;
                lock (_lock)
                {
                    _data ??= Load(Paths.GuildMissionsJson);
                    return _data;
                }
            }
        }

        private static Document Load(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Guild] {path} not found: no mission can be drawn.");
                return new Document(new(), new());
            }
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path), options);
            return new Document(document?.Missions ?? new(), document?.Objectives ?? new());
        }

        public static IReadOnlyList<Mission> All => Data.Missions;

        public static Mission MissionOf(int id) => Data.Missions.FirstOrDefault(m => m.Id == id);

        /// <summary>A mission's first objective, by its order: the one its line shows first.</summary>
        public static int FirstObjectiveOf(Mission mission)
        {
            if (mission == null) return 0;
            var objectives = Data.Objectives.Where(o => o.MissionId == mission.Id).OrderBy(o => o.Order).ToList();
            return objectives.Count > 0 ? objectives[0].Id : mission.Objectives?.FirstOrDefault() ?? 0;
        }

        /// <summary>
        /// The preferences' two lists (jgs): f1 the super categories, f2 the ranks, packed or not.
        /// </summary>
        public static (List<int> Categories, List<int> Ranks) PreferencesOf(byte[] jgs)
        {
            var categories = new List<int>();
            var ranks = new List<int>();
            if (jgs == null || jgs.Length == 0) return (categories, ranks);
            foreach (var field in ProtoMessage.Parse(jgs).Fields)
            {
                var into = field.FieldNumber == 1 ? categories : field.FieldNumber == 2 ? ranks : null;
                if (into == null) continue;
                if (field.WireType == 0) into.Add((int)field.VarIntValue);
                else if (field.WireType == 2) into.AddRange(Packed(field.BytesValue));
            }
            return (categories, ranks);
        }

        private static IEnumerable<int> Packed(byte[] bytes)
        {
            int i = 0;
            while (bytes != null && i < bytes.Length)
            {
                ulong value = 0;
                int shift = 0;
                while (i < bytes.Length)
                {
                    byte b = bytes[i++];
                    value |= (ulong)(b & 0x7F) << shift;
                    if ((b & 0x80) == 0) break;
                    shift += 7;
                }
                yield return (int)value;
            }
        }

        /// <summary>
        /// Twelve missions of the ranks and super categories chosen (all when none), none of the
        /// events', none twice, each with an objective.
        /// </summary>
        public static List<Mission> Draw(byte[] preferences, Random dice)
        {
            var (categories, ranks) = PreferencesOf(preferences);
            var pool = Data.Missions
                .Where(m => m.SuperCategoryId > 0 && m.SuperCategoryId != EventsSuperCategory)
                .Where(m => categories.Count == 0 || categories.Contains(m.SuperCategoryId))
                .Where(m => ranks.Count == 0 || ranks.Contains(m.RankId))
                .Where(m => FirstObjectiveOf(m) > 0)
                .ToList();
            var drawn = new List<Mission>();
            while (drawn.Count < PerWeek && pool.Count > 0)
            {
                int at = dice.Next(pool.Count);
                drawn.Add(pool[at]);
                pool.RemoveAt(at);
            }
            return drawn;
        }

        /// <summary>
        /// A guild's missions of the week: the stored ones, or, the first time they are asked while
        /// the guild has a tier, twelve drawn with its preferences. None without a tier.
        /// </summary>
        public static IReadOnlyList<Drawn> Of(long guildId, DateTimeOffset when)
        {
            var week = GuildActivity.Of(guildId, when);
            if (!week.HasActivity) return Array.Empty<Drawn>();
            lock (_lock)
            {
                var stored = Stored(guildId, week.Name);
                if (stored.Count > 0) return stored;

                var missions = Draw(week.Preferences, Random.Shared);
                using var connection = GuildStore.Open();
                using var transaction = connection.BeginTransaction();
                var result = new List<Drawn>();
                for (int slot = 0; slot < missions.Count; slot++)
                {
                    var drawn = new Drawn(slot, missions[slot].Id, Guid.NewGuid().ToString(), FirstObjectiveOf(missions[slot]));
                    var insert = connection.CreateCommand();
                    insert.Transaction = transaction;
                    insert.CommandText = @"
                        INSERT OR IGNORE INTO GuildMissionDraws (GuildId, Week, Slot, MissionId, Uuid, ObjectiveId)
                        VALUES ($g, $w, $s, $m, $u, $o);";
                    insert.Parameters.AddWithValue("$g", guildId);
                    insert.Parameters.AddWithValue("$w", week.Name);
                    insert.Parameters.AddWithValue("$s", slot);
                    insert.Parameters.AddWithValue("$m", drawn.MissionId);
                    insert.Parameters.AddWithValue("$u", drawn.Uuid);
                    insert.Parameters.AddWithValue("$o", drawn.ObjectiveId);
                    insert.ExecuteNonQuery();
                    result.Add(drawn);
                }
                transaction.Commit();
                Program.LogDebug($"[Guild] Guild {guildId} draws {result.Count} mission(s) for week {week.Name}.");
                return result;
            }
        }

        private static List<Drawn> Stored(long guildId, string week)
        {
            var found = new List<Drawn>();
            using var connection = GuildStore.Open();
            var query = connection.CreateCommand();
            query.CommandText = @"
                SELECT Slot, MissionId, Uuid, ObjectiveId FROM GuildMissionDraws
                WHERE GuildId = $g AND Week = $w ORDER BY Slot;";
            query.Parameters.AddWithValue("$g", guildId);
            query.Parameters.AddWithValue("$w", week);
            using var reader = query.ExecuteReader();
            while (reader.Read())
                found.Add(new Drawn(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetInt32(3)));
            return found;
        }
    }
}

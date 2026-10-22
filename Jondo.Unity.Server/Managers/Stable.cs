using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// Where a character's rides are when they are not in the bag: the stable, which holds 250, and
    /// the six paddocks, ten each; and each paddock's six gauges, the two of them that are on, and
    /// what they do to the rides in it every ten seconds.
    /// </summary>
    /// <remarks>
    /// A ride keeps its item's uid and effects wherever it goes: it leaves CharacterItems for
    /// CharacterRides and comes back the same. The place is 0 for the stable, or the paddock's id.
    ///
    /// THE GAUGES (the client's guide, text 1178404, and PaddockGaugesData): each holds up to
    /// 100,000 in four tiers -- to 40,000, 70,000, 90,000 and 100,000 -- and every ten seconds an
    /// active one spends the consumption of the tier it is in, 10 to 40 ("El indicador pierde {0}
    /// cada {1} s"), and gives that much to each ride in the paddock: the slapper takes it off its
    /// serenity, the patter adds it; the blaster gives stamina, the drinking trough maturity and the
    /// dragobutt love, each only while the serenity is in the gauge's range (RideGaugesData: stamina
    /// -5000 to -1, maturity -2000 to 2000, love 0 to 5000); the feeder gives experience. A guide
    /// measured the same: the feeder gives "10 XP toutes les 10 secondes" in the first tier and 40
    /// in the last, and a full gauge lasts 17 h 48. The wise capacity doubles the experience (that
    /// guide: "Sage double les gains d'XP"); the tough, grower and lover ones double their gauge
    /// the same way, which is INFERRED from their names.
    /// </remarks>
    public static class Stable
    {
        /// <summary>The stable's place; a paddock's is its id, 1 to 6.</summary>
        public const int StablePlace = 0;

        /// <summary>The paddock gauges, by the client's PaddockGauge: 1 slapper to 6 feeder.</summary>
        public const int Slapper = 1;
        public const int Patter = 2;
        public const int Blaster = 3;
        public const int DrinkingTrough = 4;
        public const int DragoButt = 5;
        public const int Feeder = 6;

        /// <summary>What each paddock gauge fills on the rides, by the client's RideGaugesData id: stamina, maturity, love.</summary>
        private static readonly IReadOnlyDictionary<int, (int RideGauge, int Capacity)> Fills = new Dictionary<int, (int, int)>
        {
            [Blaster] = (3, Rides.Tough),
            [DrinkingTrough] = (2, Rides.Grower),
            [DragoButt] = (1, Rides.Lover),
        };

        public sealed class StoredRide
        {
            public long Uid { get; init; }
            public long CharacterId { get; init; }
            public int Place { get; set; }
            public int Gid { get; init; }
            public List<Equipment.ItemEffect> Effects { get; set; } = new();
        }

        /// <summary>A paddock's gauges: their values by gauge id, the ones on, and when they last worked.</summary>
        public sealed class Gauges
        {
            public Dictionary<int, int> Values { get; } = new();
            public List<int> Active { get; } = new();
            public long TickedMs { get; set; }

            public int ValueOf(int gauge) => Values.TryGetValue(gauge, out int v) ? v : 0;
        }

        private static bool _ready;
        private static readonly object _lock = new();

        // ─── The rides ───────────────────────────────────────────────────────────────────

        /// <summary>Every ride of a character's out of the bag.</summary>
        public static List<StoredRide> Of(long characterId)
        {
            var list = new List<StoredRide>();
            try
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT Uid, Place, Gid, Effects FROM CharacterRides WHERE CharacterId = $c ORDER BY Uid;";
                command.Parameters.AddWithValue("$c", characterId);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new StoredRide
                    {
                        Uid = reader.GetInt64(0),
                        CharacterId = characterId,
                        Place = reader.GetInt32(1),
                        Gid = reader.GetInt32(2),
                        Effects = Equipment.ParseEffects(reader.IsDBNull(3) ? null : reader.GetString(3)).ToList(),
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Breeding] Could not read the rides of {characterId}: {ex.Message}");
            }
            return list;
        }

        /// <summary>The rides in one place: the stable, or a paddock.</summary>
        public static List<StoredRide> In(long characterId, int place) => Of(characterId).Where(r => r.Place == place).ToList();

        public static StoredRide? Get(long characterId, long uid) => Of(characterId).FirstOrDefault(r => r.Uid == uid);

        /// <summary>A ride written as it is now, wherever it is.</summary>
        public static bool Save(StoredRide ride)
        {
            try
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO CharacterRides (Uid, CharacterId, Place, Gid, Effects) VALUES ($u, $c, $p, $g, $e) " +
                                      "ON CONFLICT(Uid) DO UPDATE SET CharacterId = excluded.CharacterId, Place = excluded.Place, " +
                                      "Gid = excluded.Gid, Effects = excluded.Effects;";
                command.Parameters.AddWithValue("$u", ride.Uid);
                command.Parameters.AddWithValue("$c", ride.CharacterId);
                command.Parameters.AddWithValue("$p", ride.Place);
                command.Parameters.AddWithValue("$g", ride.Gid);
                command.Parameters.AddWithValue("$e", Forgemagic.Serialize(ride.Effects));
                command.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Breeding] Could not store ride {ride.Uid}: {ex.Message}");
                return false;
            }
        }

        public static bool Remove(long characterId, long uid)
        {
            try
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM CharacterRides WHERE CharacterId = $c AND Uid = $u;";
                command.Parameters.AddWithValue("$c", characterId);
                command.Parameters.AddWithValue("$u", uid);
                return command.ExecuteNonQuery() > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Breeding] Could not remove ride {uid}: {ex.Message}");
                return false;
            }
        }

        // ─── The gauges ──────────────────────────────────────────────────────────────────

        public static Gauges GaugesOf(long characterId, int paddock)
        {
            var gauges = new Gauges();
            try
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT Gauges, Active, TickedMs FROM CharacterPaddocks WHERE CharacterId = $c AND Paddock = $p;";
                command.Parameters.AddWithValue("$c", characterId);
                command.Parameters.AddWithValue("$p", paddock);
                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    var values = JsonSerializer.Deserialize<int[]>(reader.GetString(0)) ?? Array.Empty<int>();
                    for (int i = 0; i < values.Length; i++) if (values[i] != 0) gauges.Values[i + 1] = values[i];
                    gauges.Active.AddRange(JsonSerializer.Deserialize<int[]>(reader.GetString(1)) ?? Array.Empty<int>());
                    gauges.TickedMs = reader.GetInt64(2);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Breeding] Could not read paddock {paddock} of {characterId}: {ex.Message}");
            }
            return gauges;
        }

        public static void SaveGauges(long characterId, int paddock, Gauges gauges)
        {
            try
            {
                var values = Enumerable.Range(1, Breeding.Gauges.Count).Select(gauges.ValueOf).ToArray();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO CharacterPaddocks (CharacterId, Paddock, Gauges, Active, TickedMs) VALUES ($c, $p, $g, $a, $t) " +
                                      "ON CONFLICT(CharacterId, Paddock) DO UPDATE SET Gauges = excluded.Gauges, Active = excluded.Active, " +
                                      "TickedMs = excluded.TickedMs;";
                command.Parameters.AddWithValue("$c", characterId);
                command.Parameters.AddWithValue("$p", paddock);
                command.Parameters.AddWithValue("$g", JsonSerializer.Serialize(values));
                command.Parameters.AddWithValue("$a", JsonSerializer.Serialize(gauges.Active.Distinct().ToArray()));
                command.Parameters.AddWithValue("$t", gauges.TickedMs);
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Breeding] Could not store paddock {paddock} of {characterId}: {ex.Message}");
            }
        }

        /// <summary>The tier a gauge's value is in, and what it spends there every ten seconds.</summary>
        public static Breeding.GaugeTier? TierOf(int gauge, int value)
        {
            var data = Breeding.GaugeOf(gauge);
            if (data == null || value <= 0) return null;
            return data.Tiers.FirstOrDefault(t => value <= t.Max) ?? data.Tiers.LastOrDefault();
        }

        /// <summary>The most a gauge holds: its last tier's top.</summary>
        public static int GaugeMax(int gauge) => Breeding.GaugeOf(gauge)?.Tiers.LastOrDefault()?.Max ?? 0;

        /// <summary>
        /// A paddock brought up to now: every ten seconds gone, its active gauges spend their tier's
        /// consumption and give it to its rides. Returns whether anything changed.
        /// </summary>
        public static bool Advance(long characterId, int paddock, long nowMs)
        {
            var gauges = GaugesOf(characterId, paddock);
            if (gauges.TickedMs <= 0 || gauges.TickedMs > nowMs)
            {
                gauges.TickedMs = nowMs;
                SaveGauges(characterId, paddock, gauges);
                return false;
            }
            long ticks = (nowMs - gauges.TickedMs) / (Breeding.TickSeconds * 1000L);
            if (ticks <= 0) return false;
            gauges.TickedMs += ticks * Breeding.TickSeconds * 1000L;

            var rides = In(characterId, paddock);
            bool changed = false;
            for (long t = 0; t < ticks; t++)
            {
                bool working = false;
                foreach (int gauge in gauges.Active.ToList())
                {
                    int value = gauges.ValueOf(gauge);
                    var tier = TierOf(gauge, value);
                    if (tier == null) continue;
                    int amount = Math.Min(value, tier.ConsumptionTick);
                    gauges.Values[gauge] = value - amount;
                    working = true;
                    changed = true;
                    // Today's Almanax may double what it does: «El efecto de los acariciadores se
                    // duplica en los cercados» (type 36, by gauge); what it spends stays the same.
                    int given = (int)Almanax.WithBonus(Almanax.BonusType.PaddockItems, amount,
                        Almanax.Where.Anywhere with { PaddockGauge = gauge });
                    foreach (var ride in rides) ride.Effects = Worked(ride, gauge, given);
                }
                if (!working) break;
            }
            SaveGauges(characterId, paddock, gauges);
            if (changed) foreach (var ride in rides) Save(ride);
            return changed;
        }

        /// <summary>What one tick of a gauge does to a ride.</summary>
        public static List<Equipment.ItemEffect> Worked(StoredRide ride, int gauge, int amount)
        {
            var effects = ride.Effects;
            int mood = Rides.MoodOf(effects);
            switch (gauge)
            {
                case Slapper:
                    return Rides.With(effects, Rides.Mood, Math.Max(Breeding.MinMood, mood - amount));
                case Patter:
                    return Rides.With(effects, Rides.Mood, Math.Min(Breeding.MaxMood, mood + amount));
                case Feeder:
                    return Rides.WithExperience(ride.Gid, effects, amount * (Rides.Has(effects, Rides.Wise) ? 2 : 1));
            }
            if (!Fills.TryGetValue(gauge, out var fill)) return effects;
            var data = Breeding.RideGaugeOf(fill.RideGauge);
            if (data == null || mood < data.MinMood || mood > data.MaxMood) return effects;
            int effect = Rides.GaugeEffect[fill.RideGauge];
            int gained = amount * (Rides.Has(effects, fill.Capacity) ? 2 : 1);
            return Rides.With(effects, effect, Math.Min(data.Max, Rides.GaugeOf(effects, effect) + gained));
        }

        // ─── The base ────────────────────────────────────────────────────────────────────

        private static SqliteConnection Open()
        {
            var connection = new SqliteConnection(DatabaseManager.WorldConnectionString);
            connection.Open();
            if (!_ready)
            {
                lock (_lock)
                {
                    if (!_ready)
                    {
                        using var create = connection.CreateCommand();
                        create.CommandText = @"
                            CREATE TABLE IF NOT EXISTS CharacterRides (
                                Uid         INTEGER NOT NULL PRIMARY KEY,
                                CharacterId INTEGER NOT NULL,
                                Place       INTEGER NOT NULL,
                                Gid         INTEGER NOT NULL,
                                Effects     TEXT);
                            CREATE INDEX IF NOT EXISTS CharacterRidesByCharacter ON CharacterRides (CharacterId);
                            CREATE TABLE IF NOT EXISTS CharacterPaddocks (
                                CharacterId INTEGER NOT NULL,
                                Paddock     INTEGER NOT NULL,
                                Gauges      TEXT NOT NULL,
                                Active      TEXT NOT NULL,
                                TickedMs    INTEGER NOT NULL DEFAULT 0,
                                PRIMARY KEY (CharacterId, Paddock));";
                        create.ExecuteNonQuery();
                        _ready = true;
                    }
                }
            }
            return connection;
        }
    }
}

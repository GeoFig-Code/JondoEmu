using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The bookkeeping of a kind of rare monster that stands one at a time in the world: where each
    /// one is, which maps already have one, and how long a killed one stays away. It knows nothing
    /// of what makes a monster rare; <see cref="Archimonsters"/> and <see cref="WantedMonsters"/>
    /// each keep one of these, with their own respawn time.
    ///
    /// Not thread-safe on its own: its owner calls it under its own lock.
    /// </summary>
    public sealed class RareMonsterPool
    {
        /// <summary>One group in ten gets to keep one, when the world is loaded.</summary>
        private const int OneInHowMany = 10;

        private const int RespawnSpreadMinutes = 15;

        /// <summary>The time, which the tests move forward instead of waiting hours.</summary>
        public static Func<DateTime> Clock = () => DateTime.UtcNow;

        private readonly TimeSpan _respawn;

        /// <summary>
        /// How fast one comes out today, in percent (100 = as ever): the Almanax's «la velocidad de
        /// aparición ... aumenta un 200%» is 300. It multiplies the draw's odds and divides the
        /// respawn time.
        /// </summary>
        private readonly Func<int, int>? _speedPercent;

        /// <summary>id -> the subareas it belongs to.</summary>
        private readonly Dictionary<int, List<int>> _zones = new Dictionary<int, List<int>>();

        /// <summary>Where each one is standing right now: id -> map.</summary>
        private readonly Dictionary<int, long> _placed = new Dictionary<int, long>();

        /// <summary>Maps that already have one, so no map gets a second.</summary>
        private readonly HashSet<long> _busyMaps = new HashSet<long>();

        /// <summary>Killed ones, and when they may stand again.</summary>
        private readonly Dictionary<int, DateTime> _respawnAt = new Dictionary<int, DateTime>();

        public RareMonsterPool(TimeSpan respawn, Func<int, int>? speedPercent = null)
        {
            _respawn = respawn;
            _speedPercent = speedPercent;
        }

        private int Speed(int id) => Math.Max(100, _speedPercent?.Invoke(id) ?? 100);

        public int ZoneCount => _zones.Count;

        public void Clear()
        {
            _zones.Clear();
            _placed.Clear();
            _busyMaps.Clear();
            _respawnAt.Clear();
        }

        /// <summary>Reads the subareas a template declares, if it declares any.</summary>
        public void AddZones(int id, JsonElement template)
        {
            if (!template.TryGetProperty("subareas", out var subareas)) return;
            if (!subareas.TryGetProperty("Array", out var array)) return;

            var zones = new List<int>();
            foreach (var z in array.EnumerateArray())
            {
                if (z.TryGetInt32(out int zone)) zones.Add(zone);
            }
            if (zones.Count > 0) _zones[id] = zones;
        }

        public bool BelongsTo(int id, int subAreaId)
            => _zones.TryGetValue(id, out var zones) && zones.Contains(subAreaId);

        /// <summary>Standing nowhere, and not waiting out its respawn time.</summary>
        public bool IsUp(int id)
            => !_placed.ContainsKey(id) &&
               (!_respawnAt.TryGetValue(id, out var at) || at <= Clock());

        /// <summary>
        /// One per map, one of each, not while dead, and, with <paramref name="draw"/>, one group
        /// in ten. The draw is worked out from the group's id, so it always comes out the same for
        /// the same group, restart or no restart.
        /// </summary>
        public bool MayStand(long mapId, long groupId, int id, bool draw)
        {
            if (_busyMaps.Contains(mapId)) return false;
            if (!IsUp(id)) return false;

            // Mixing the group and the monster keeps two groups on the same map from drawing alike.
            // One in ten, three in ten on a day the Almanax triples the speed.
            return !draw || Draw(groupId, id) % (OneInHowMany * 100) < Speed(id);
        }

        public void Reserve(long mapId, int id)
        {
            _placed[id] = mapId;
            _busyMaps.Add(mapId);
        }

        /// <summary>
        /// Lets go of the one a beaten group was holding on that map: it may stand again, somewhere
        /// in its zone, once its respawn time ± 15 minutes is over.
        /// </summary>
        public void Release(long mapId, IEnumerable<int> members)
        {
            foreach (int id in members)
            {
                if (!_placed.TryGetValue(id, out long where) || where != mapId) continue;
                _placed.Remove(id);
                _busyMaps.Remove(mapId);

                int spread = Random.Shared.Next(-RespawnSpreadMinutes, RespawnSpreadMinutes + 1);
                var away = _respawn + TimeSpan.FromMinutes(spread);
                _respawnAt[id] = Clock() + TimeSpan.FromTicks(away.Ticks * 100 / Speed(id));
            }
        }

        /// <summary>Every monster template, id and parsed data, for the owners to pick theirs.</summary>
        public static Dictionary<int, JsonElement> ReadTemplates(SqliteConnection connection)
        {
            var templates = new Dictionary<int, JsonElement>();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Data FROM MonsterTemplates;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (reader.IsDBNull(1)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(reader.GetString(1));
                    templates[reader.GetInt32(0)] = doc.RootElement.Clone();
                }
                catch { }
            }
            return templates;
        }

        /// <summary>
        /// A number that depends only on the group and the monster. Deliberately not Random: the
        /// same world has to come back after a restart, and nothing here is worth persisting.
        /// </summary>
        private static uint Draw(long groupId, int id)
        {
            unchecked
            {
                ulong x = ((ulong)groupId * 0x9E3779B97F4A7C15UL) ^ ((ulong)id * 0xBF58476D1CE4E5B9UL);
                x ^= x >> 33;
                x *= 0xFF51AFD7ED558CCDUL;
                x ^= x >> 33;
                return (uint)(x & 0x7FFFFFFF);
            }
        }
    }
}

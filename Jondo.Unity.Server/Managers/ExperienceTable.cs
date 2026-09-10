using Jondo.Unity.Launcher;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Jondo.Unity.Server
{
    /// <summary>
    /// Character experience table: how much accumulated experience is needed to reach each level.
    /// It comes from the client bundles
    /// (data_assets_characterxpmappingsdataroot.asset.bundle) through extract_character_xp.py.
    ///
    /// The first values line up with what was already hardcoded in the kri -- level 2 = 110 and
    /// level 3 = 650 -- and with the official end-of-fight capture, which for a level 3 character
    /// sends 650 as the floor of the level and 1500 as the threshold for the next one.
    /// </summary>
    public static class ExperienceTable
    {
        private static readonly SortedDictionary<int, long> _floors = new SortedDictionary<int, long>();
        private static int _maxLevel = 1;

        /// <summary>
        /// Whether the table is filled in and safe to read. Volatile because the fast path in
        /// <see cref="Ensure"/> reads it outside the lock, and raised LAST so that it publishes
        /// _floors AND _maxLevel together -- they are read as a pair in <see cref="LevelFloor"/>
        /// and a reader must never see one without the other.
        /// </summary>
        private static volatile bool _loaded;
        private static readonly object _lock = new object();

        /// <summary>
        /// Whether there is a table to work with. Loads it first if nobody has yet, so this
        /// answers "is there experience data", never "has somebody remembered to call Initialize".
        /// </summary>
        /// <remarks>
        /// This one carries weight. CommandHandler uses it to decide whether to write
        /// GameState.Experience at all, because with no table LevelFloor returns zero for every
        /// level, and writing that does not floor the character's experience -- it erases it.
        /// </remarks>
        public static bool IsLoaded { get { Ensure(); return _floors.Count > 0; } }

        public static int MaxLevel { get { Ensure(); return _maxLevel; } }

        /// <summary>
        /// Reads the table, once per run. Kept as a separate call so the server pays for it at
        /// boot, with its log line, instead of on whoever first asks what a level is worth.
        /// </summary>
        /// <remarks>
        /// CALLING IT AGAIN DOES NOTHING, ON PURPOSE. It used to clear the table and re-read the
        /// file with no lock of any kind, and anybody reading it during that window got a wrong
        /// answer -- measured, every single read: LevelFloor(200) came back 0 instead of
        /// 5,555,424,000. Not an exception, a number; and a zero here travels. It is what
        /// CommandHandler guards against before overwriting a character's experience, and in the
        /// kub it goes out through VarIfNotZero, which drops the field entirely, so the packet
        /// changes shape and not just its contents.
        ///
        /// character_xp.json comes out of the client bundles and does not change while the server
        /// is up, so there was never anything to reload.
        /// </remarks>
        public static void Initialize() => Ensure();

        private static void Ensure()
        {
            if (_loaded) return;
            lock (_lock)
            {
                if (_loaded) return;
                try
                {
                    Load();
                }
                finally
                {
                    // In a finally so a missing file still counts as tried: otherwise every
                    // level lookup from here on would go back to the disk and log again.
                    _loaded = true;
                }
            }
        }

        private static void Load()
        {
            string path = Paths.CharacterXpJson;

            if (!File.Exists(path))
            {
                Console.WriteLine($"[ExperienceTable] WARNING: {path} not found. " +
                                  "Experience cannot be computed; run extract_character_xp.py.");
                return;
            }

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (int.TryParse(prop.Name, out int level) && prop.Value.TryGetInt64(out long xp))
                    {
                        _floors[level] = xp;
                    }
                }
                _maxLevel = _floors.Count > 0 ? _floors.Keys.Max() : 1;
                Console.WriteLine($"[ExperienceTable] Experience table loaded: {_floors.Count} levels (up to {_maxLevel}).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ExperienceTable] Error reading {path}: {ex.Message}");
            }
        }

        /// <summary>Accumulated experience the given level starts at.</summary>
        public static long LevelFloor(int level)
        {
            Ensure();
            if (_floors.Count == 0) return 0;
            if (_floors.TryGetValue(level, out long xp)) return xp;
            return level <= 1 ? 0 : _floors[Math.Min(level, _maxLevel)];
        }

        /// <summary>Accumulated experience at which the next level is reached.</summary>
        public static long NextLevelFloor(int level)
        {
            Ensure();
            if (_floors.Count == 0) return 0;
            int next = level + 1;
            if (next > _maxLevel) return LevelFloor(_maxLevel);
            return LevelFloor(next);
        }

        /// <summary>The level that a given amount of accumulated experience corresponds to.</summary>
        /// <remarks>
        /// Walks the whole table, so it is the read that suffered most from the table being
        /// rebuilt underneath it: enumerating a SortedDictionary somebody else is writing to is
        /// undefined, not merely stale.
        /// </remarks>
        public static int LevelForXp(long experience)
        {
            Ensure();
            if (_floors.Count == 0) return 1;
            int level = 1;
            foreach (var kv in _floors)
            {
                if (kv.Value > experience) break;
                level = kv.Key;
            }
            return level;
        }
    }
}

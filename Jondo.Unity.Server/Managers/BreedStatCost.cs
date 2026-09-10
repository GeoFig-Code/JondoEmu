using Jondo.Unity.Launcher;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// What a point of each characteristic costs, per breed.
    ///
    /// It has to be the client's own table and not a reasonable-looking one: the client works the
    /// cost out itself, shows the player the result, and only then sends the total in kum. A
    /// server with a different table hands back a different number of points than the panel just
    /// promised.
    ///
    /// The real one turns out not to be the table this emulator assumed either. Each band starts
    /// every hundred points, not every fifty:
    ///
    ///   strength, intelligence, chance, agility   1 up to 100, 2 to 200, 3 to 300, 4 above
    ///   vitality                                  1, always
    ///   wisdom                                    3, always
    ///
    /// From data_assets_breedsdataroot.asset.bundle through extract_breed_stats.py.
    /// </summary>
    public static class BreedStatCost
    {
        /// <summary>breed -> characteristic -> bands, each one (from this value, this price).</summary>
        private static readonly Dictionary<int, Dictionary<string, List<(int From, int Price)>>> _table =
            new Dictionary<int, Dictionary<string, List<(int, int)>>>();

        /// <summary>
        /// Whether the tables are read in and safe to use. Volatile because the fast path in
        /// <see cref="Ensure"/> reads it outside the lock, and raised LAST.
        /// </summary>
        private static volatile bool _loaded;
        private static readonly object _lock = new object();

        /// <summary>
        /// Whether there are cost tables to work with. Reads them in first, so this answers "is
        /// there price data" and not "has somebody remembered to call Initialize".
        /// </summary>
        /// <remarks>
        /// It has to load: CommandHandler asks this before charging for a characteristic point,
        /// and falls back to its own count when the answer is no. A false here because nothing
        /// had been read in yet would quietly charge the player a different price from the one
        /// the window just quoted them.
        /// </remarks>
        public static bool IsLoaded { get { Ensure(); return _table.Count > 0; } }

        /// <summary>
        /// Reads the file, once per run. Kept as a separate call so the server pays for it at
        /// boot, with its log line, rather than on the first point somebody spends.
        /// </summary>
        /// <remarks>
        /// Calling it again does nothing, on purpose: breed_stats.json comes out of the client
        /// and does not change while the server is up.
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
                    // In a finally so a missing file counts as tried, rather than going back to
                    // the disk on every point spent from here on.
                    _loaded = true;
                }
            }
        }

        private static void Load()
        {
            string path = Paths.BreedStatsJson;

            if (!File.Exists(path))
            {
                Console.WriteLine($"[BreedStatCost] WARNING: {Path.GetFileName(path)} is not there. " +
                                  "Spending points will fall back to one point each; run " +
                                  "extract_breed_stats.py.");
                return;
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var breed in doc.RootElement.EnumerateObject())
                {
                    if (!int.TryParse(breed.Name, out int breedId)) continue;

                    var characteristics = new Dictionary<string, List<(int, int)>>();
                    foreach (var characteristic in breed.Value.EnumerateObject())
                    {
                        var bands = new List<(int, int)>();
                        foreach (var band in characteristic.Value.EnumerateArray())
                        {
                            var pair = band.EnumerateArray();
                            if (!pair.MoveNext()) continue;
                            int from = pair.Current.GetInt32();
                            if (!pair.MoveNext()) continue;
                            bands.Add((from, pair.Current.GetInt32()));
                        }
                        if (bands.Count > 0) characteristics[characteristic.Name] = bands;
                    }
                    if (characteristics.Count > 0) _table[breedId] = characteristics;
                }

                Console.WriteLine($"[BreedStatCost] Cost tables for {_table.Count} breeds.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BreedStatCost] Could not read {Path.GetFileName(path)}: {ex.Message}");
            }
        }

        /// <summary>
        /// What the next point of a characteristic costs a character that already has
        /// <paramref name="current"/> of it. One when there is no table, which is the cheapest
        /// answer and never charges the player for something we cannot price.
        /// </summary>
        public static int PriceOf(int breed, string characteristic, int current)
        {
            Ensure();
            if (!_table.TryGetValue(breed, out var characteristics)) return 1;
            if (!characteristics.TryGetValue(characteristic, out var bands)) return 1;

            int price = 1;
            foreach (var band in bands)
            {
                if (current >= band.From) price = band.Price;
            }
            return price;
        }
    }
}

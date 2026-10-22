using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jondo.Unity.Launcher;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// Which points of interest -- the hunts' landmarks, the client's PointsOfInterest -- each map
    /// shows: what a hunt's «find the landmark that way» step needs and the client never carries.
    /// </summary>
    /// <remarks>
    /// The pairing is the players' own: DofusDB's treasure hunt helper, read once along every row
    /// of the world (tools/extract_hunt_pois.py, datos/hunt_pois.json). It fits the capture's
    /// hunt: its first step, «882 to the south» from [2,4], is the «Champiñón rayado», which
    /// DofusDB shows first that way on [2,11], seven maps on -- within the ten a step may go.
    /// </remarks>
    public static class TreasureHuntPois
    {
        private static IReadOnlyDictionary<long, IReadOnlyList<int>>? _byMap;
        private static readonly object _lock = new();

        private static readonly IReadOnlyList<int> None = Array.Empty<int>();

        /// <summary>The landmarks a map shows; none when it shows none or nothing is known of it.</summary>
        public static IReadOnlyList<int> Of(long mapId)
            => Load().TryGetValue(mapId, out var pois) ? pois : None;

        /// <summary>How many maps have landmarks known.</summary>
        public static int Count => Load().Count;

        private static IReadOnlyDictionary<long, IReadOnlyList<int>> Load()
        {
            if (_byMap != null) return _byMap;
            lock (_lock)
            {
                if (_byMap != null) return _byMap;
                var byMap = new Dictionary<long, IReadOnlyList<int>>();
                string path = Paths.HuntPoisJson;
                try
                {
                    if (File.Exists(path))
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(path));
                        foreach (var map in doc.RootElement.GetProperty("maps").EnumerateObject())
                        {
                            if (!long.TryParse(map.Name, out long mapId)) continue;
                            var pois = map.Value.EnumerateArray().Select(p => p.GetInt32()).ToList();
                            if (pois.Count > 0) byMap[mapId] = pois;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Hunts] Could not read {Path.GetFileName(path)}: {ex.Message}");
                }
                if (byMap.Count > 0) Console.WriteLine($"[Hunts] Landmarks known on {byMap.Count} maps.");
                _byMap = byMap;
                return _byMap;
            }
        }
    }
}

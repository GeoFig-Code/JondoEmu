using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jondo.Unity.Launcher;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The breeding data of the client: the six paddocks, their gauges, the rides.
    /// </summary>
    /// <remarks>
    /// Every player has six private paddocks, «Cercado de principiante» to «Cercado de maestro»,
    /// opened by the breeder's level (0, 40, 80, 120, 160, 200). Each one is a map of its own with a
    /// gate of type 120, «Cercado», whose skill «Abrir el cercado de …» (423 to 428) belongs to the
    /// breeder job (79). Each paddock holds ten rides and has six gauges, two of them on at a time,
    /// each one charged with fuel up to a tier. Generated from the client by
    /// tools/extract_breeding.py; see the client's own guide, texts 1178359 and 1178404.
    /// </remarks>
    public static class Breeding
    {
        /// <summary>The breeder job.</summary>
        public const int BreederJob = 79;

        /// <summary>How many rides a paddock holds.</summary>
        public const int RidesPerPaddock = 10;

        /// <summary>How many of a paddock's gauges can be on at once.</summary>
        public const int ActiveGauges = 2;

        public sealed record Paddock(int Id, string Name, int Skill, int MinLevel, long MapId, int Element, int Cell);

        public sealed record GaugeTier(int Max, int ConsumptionTick);

        public sealed record PaddockGauge(int Id, string Name, IReadOnlyList<GaugeTier> Tiers);

        public sealed record Ride(int Id, string Name, int Species, int Generation, int GeneticWeight, int Item,
                                  int Extraction, int SenileExtraction, int Tokens, int BreedingXp);

        private sealed record Document(int GateType, IReadOnlyList<Paddock> Paddocks,
                                       IReadOnlyList<PaddockGauge> Gauges, IReadOnlyDictionary<int, Ride> Rides);

        private static Document? _document;
        private static readonly object _lock = new();

        private static Document Loaded
        {
            get
            {
                if (_document != null) return _document;
                lock (_lock)
                {
                    _document ??= Load(Paths.BreedingJson);
                    return _document;
                }
            }
        }

        /// <summary>The interactive type of a paddock's gate.</summary>
        public static int GateType => Loaded.GateType;

        /// <summary>The six paddocks, in their order.</summary>
        public static IReadOnlyList<Paddock> Paddocks => Loaded.Paddocks;

        /// <summary>The six paddock gauges, in their order.</summary>
        public static IReadOnlyList<PaddockGauge> Gauges => Loaded.Gauges;

        /// <summary>The paddock with that id, or null.</summary>
        public static Paddock? PaddockOf(int id) => Loaded.Paddocks.FirstOrDefault(p => p.Id == id);

        /// <summary>The paddock whose gate offers that skill, or null.</summary>
        public static Paddock? PaddockBySkill(int skill) => Loaded.Paddocks.FirstOrDefault(p => p.Skill == skill);

        /// <summary>The ride with that id, or null.</summary>
        public static Ride? RideOf(int id) => Loaded.Rides.TryGetValue(id, out var ride) ? ride : null;

        /// <summary>Whether a breeder of that level may open the paddock.</summary>
        public static bool Opens(Paddock paddock, int breederLevel) => breederLevel >= paddock.MinLevel;

        private static Document Load(string path)
        {
            var paddocks = new List<Paddock>();
            var gauges = new List<PaddockGauge>();
            var rides = new Dictionary<int, Ride>();
            int gateType = 0;
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Breeding] {path} not found: no paddock can be opened. Generate it with tools/extract_breeding.py.");
                return new Document(gateType, paddocks, gauges, rides);
            }

            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(path));
                var root = json.RootElement;
                if (root.TryGetProperty("gateType", out var type)) gateType = type.GetInt32();
                foreach (var p in root.GetProperty("paddocks").EnumerateArray())
                {
                    paddocks.Add(new Paddock(p.GetProperty("id").GetInt32(), p.GetProperty("name").GetString() ?? "",
                                             p.GetProperty("skill").GetInt32(), p.GetProperty("minLevel").GetInt32(),
                                             p.GetProperty("mapId").GetInt64(), p.GetProperty("element").GetInt32(),
                                             p.GetProperty("cell").GetInt32()));
                }
                foreach (var g in root.GetProperty("paddockGauges").EnumerateArray())
                {
                    var tiers = g.GetProperty("tiers").EnumerateArray()
                                 .Select(t => new GaugeTier(t.GetProperty("max").GetInt32(), t.GetProperty("consumptionTick").GetInt32()))
                                 .ToList();
                    gauges.Add(new PaddockGauge(g.GetProperty("id").GetInt32(), g.GetProperty("name").GetString() ?? "", tiers));
                }
                foreach (var r in root.GetProperty("rides").EnumerateArray())
                {
                    var ride = new Ride(r.GetProperty("id").GetInt32(), r.GetProperty("name").GetString() ?? "",
                                        r.GetProperty("species").GetInt32(), r.GetProperty("generation").GetInt32(),
                                        r.GetProperty("geneticWeight").GetInt32(), r.GetProperty("item").GetInt32(),
                                        r.GetProperty("extraction").GetInt32(), r.GetProperty("senileExtraction").GetInt32(),
                                        r.GetProperty("tokens").GetInt32(), r.GetProperty("breedingXp").GetInt32());
                    rides[ride.Id] = ride;
                }
                Console.WriteLine($"[Breeding] {paddocks.Count} paddocks, {gauges.Count} gauges, {rides.Count} rides.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Breeding] Could not read {Path.GetFileName(path)}: {ex.Message}");
            }
            return new Document(gateType, paddocks, gauges, rides);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jondo.Unity.Launcher;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The breeding data of the client: the six paddocks, their gauges, the rides, their species
    /// and gauges, their characteristics by level and their experience.
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

        /// <summary>How many rides a paddock holds (PaddockUi.MaxPaddockMount).</summary>
        public const int RidesPerPaddock = 10;

        /// <summary>How many rides the stable holds (BreedingUi.StableMaxSize).</summary>
        public const int StableSize = 250;

        /// <summary>How many of a paddock's gauges can be on at once (PaddockUi.MaxEnabledGauges).</summary>
        public const int ActiveGauges = 2;

        /// <summary>A ride's serenity runs from -5000 to 5000 (BreedingUi.MinMood, MaxMood).</summary>
        public const int MinMood = -5000;
        public const int MaxMood = 5000;

        /// <summary>How often a paddock's gauges work, in seconds: "El indicador pierde {0} cada {1} s".</summary>
        public const int TickSeconds = 10;

        public sealed record Paddock(int Id, string Name, int Skill, int MinLevel, long MapId, int Element, int Cell);

        public sealed record GaugeTier(int Max, int ConsumptionTick);

        public sealed record PaddockGauge(int Id, string Name, IReadOnlyList<GaugeTier> Tiers);

        public sealed record Ride(int Id, string Name, int Species, int Generation, int GeneticWeight, int Item,
                                  int Extraction, int SenileExtraction, int Tokens, int BreedingXp,
                                  IReadOnlyList<(int Parent1, int Parent2)> Parents = null,
                                  IReadOnlyList<(int Parent, int Child)> Children = null);

        public sealed record Species(int Id, string Name, int ExtractionItem);

        /// <summary>A ride's gauge: love (1), maturity (2) or stamina (3), and the serenity it grows in.</summary>
        public sealed record RideGauge(int Id, int Max, int MinMood, int MaxMood);

        /// <summary>A characteristic a ride item gives, by level: up to each level, so much per level.</summary>
        public sealed record Evolution(int Action, IReadOnlyList<(int UpTo, double PerLevel)> Ranges);

        /// <summary>A capture net: the spell it gives, the wild rides it takes, how far around and how many times each.</summary>
        public sealed record CaptureNet(int Item, int Spell, IReadOnlyList<int> Wild, int Radius, int Multiplier);

        private sealed record Document(int GateType, IReadOnlyList<Paddock> Paddocks,
                                       IReadOnlyList<PaddockGauge> Gauges, IReadOnlyDictionary<int, Ride> Rides,
                                       IReadOnlyDictionary<int, Ride> RidesByItem, IReadOnlyList<Species> Species,
                                       IReadOnlyList<RideGauge> RideGauges,
                                       IReadOnlyDictionary<int, IReadOnlyList<Evolution>> Evolutions,
                                       double XpCoefficient, double XpExponent, int MaxLevel,
                                       IReadOnlyList<CaptureNet> Nets, IReadOnlyDictionary<int, int> WildRides);

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

        /// <summary>The ride's three gauges.</summary>
        public static IReadOnlyList<RideGauge> RideGauges => Loaded.RideGauges;

        /// <summary>The paddock with that id, or null.</summary>
        public static Paddock? PaddockOf(int id) => Loaded.Paddocks.FirstOrDefault(p => p.Id == id);

        /// <summary>The paddock whose gate offers that skill, or null.</summary>
        public static Paddock? PaddockBySkill(int skill) => Loaded.Paddocks.FirstOrDefault(p => p.Skill == skill);

        /// <summary>The paddock whose map that is, or null.</summary>
        public static Paddock? PaddockOnMap(long mapId) => Loaded.Paddocks.FirstOrDefault(p => p.MapId == mapId && mapId != 0);

        /// <summary>The ride with that id, or null.</summary>
        public static Ride? RideOf(int id) => Loaded.Rides.TryGetValue(id, out var ride) ? ride : null;

        /// <summary>The ride an item is, or null when the item is no ride.</summary>
        public static Ride? RideByItem(int gid) => Loaded.RidesByItem.TryGetValue(gid, out var ride) ? ride : null;

        /// <summary>Every ride of the client's.</summary>
        public static IEnumerable<Ride> AllRides => Loaded.Rides.Values;

        /// <summary>A species, or null.</summary>
        public static Species? SpeciesOf(int id) => Loaded.Species.FirstOrDefault(s => s.Id == id);

        /// <summary>A ride gauge by id, or null.</summary>
        public static RideGauge? RideGaugeOf(int id) => Loaded.RideGauges.FirstOrDefault(g => g.Id == id);

        /// <summary>A paddock gauge by id (1 to 6), or null.</summary>
        public static PaddockGauge? GaugeOf(int id) => Loaded.Gauges.FirstOrDefault(g => g.Id == id);

        /// <summary>The characteristics a ride item gives by level.</summary>
        public static IReadOnlyList<Evolution> EvolutionsOf(int gid)
            => Loaded.Evolutions.TryGetValue(gid, out var list) ? list : Array.Empty<Evolution>();

        /// <summary>A ride's highest level.</summary>
        public static int MaxLevel => Loaded.MaxLevel;

        /// <summary>The capture net an item is, or null.</summary>
        public static CaptureNet? NetByItem(int gid) => Loaded.Nets.FirstOrDefault(n => n.Item == gid);

        /// <summary>The capture net whose spell that is, or null.</summary>
        public static CaptureNet? NetBySpell(int spell) => spell == 0 ? null : Loaded.Nets.FirstOrDefault(n => n.Spell == spell);

        /// <summary>The ride a wild monster is caught as ("Dragopavo almendrado salvaje" → "Dragopavo almendrado"), or null.</summary>
        public static Ride? RideOfWild(int monster) => Loaded.WildRides.TryGetValue(monster, out int ride) ? RideOf(ride) : null;

        /// <summary>
        /// The experience a ride has at a level: the client's formula 111, roundToNearest(3.795 ·
        /// lvl^2.329), which its breeding window asks for the bar of the level it is at and of the next.
        /// </summary>
        public static long ExperienceAt(int level)
        {
            int l = Math.Clamp(level, 1, Math.Max(1, MaxLevel));
            return (long)Math.Round(Loaded.XpCoefficient * Math.Pow(l, Loaded.XpExponent), MidpointRounding.AwayFromZero);
        }

        /// <summary>The level an amount of experience gets a ride to.</summary>
        public static int LevelFor(long experience)
        {
            int level = 1;
            for (int l = 2; l <= MaxLevel; l++)
            {
                if (experience >= ExperienceAt(l)) level = l;
                else break;
            }
            return level;
        }

        /// <summary>Whether a breeder of that level may open the paddock.</summary>
        public static bool Opens(Paddock paddock, int breederLevel) => breederLevel >= paddock.MinLevel;

        private static Document Load(string path)
        {
            var paddocks = new List<Paddock>();
            var gauges = new List<PaddockGauge>();
            var rides = new Dictionary<int, Ride>();
            var species = new List<Species>();
            var rideGauges = new List<RideGauge>();
            var evolutions = new Dictionary<int, IReadOnlyList<Evolution>>();
            var nets = new List<CaptureNet>();
            var wildRides = new Dictionary<int, int>();
            double coefficient = 0, exponent = 0;
            int maxLevel = 0;
            int gateType = 0;
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Breeding] {path} not found: no paddock can be opened. Generate it with tools/extract_breeding.py.");
                return new Document(gateType, paddocks, gauges, rides, new Dictionary<int, Ride>(), species, rideGauges,
                                    evolutions, 0, 0, 1, nets, wildRides);
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
                                 .OrderBy(t => t.Max)
                                 .ToList();
                    gauges.Add(new PaddockGauge(g.GetProperty("id").GetInt32(), g.GetProperty("name").GetString() ?? "", tiers));
                }
                foreach (var r in root.GetProperty("rides").EnumerateArray())
                {
                    var parents = r.TryGetProperty("parents", out var ps)
                        ? ps.EnumerateArray().Select(x => (x[0].GetInt32(), x[1].GetInt32())).ToList()
                        : new List<(int, int)>();
                    var children = r.TryGetProperty("children", out var cs)
                        ? cs.EnumerateArray().Select(x => (x[0].GetInt32(), x[1].GetInt32())).ToList()
                        : new List<(int, int)>();
                    var ride = new Ride(r.GetProperty("id").GetInt32(), r.GetProperty("name").GetString() ?? "",
                                        r.GetProperty("species").GetInt32(), r.GetProperty("generation").GetInt32(),
                                        r.GetProperty("geneticWeight").GetInt32(), r.GetProperty("item").GetInt32(),
                                        r.GetProperty("extraction").GetInt32(), r.GetProperty("senileExtraction").GetInt32(),
                                        r.GetProperty("tokens").GetInt32(), r.GetProperty("breedingXp").GetInt32(),
                                        parents, children);
                    rides[ride.Id] = ride;
                }
                if (root.TryGetProperty("species", out var sp))
                    foreach (var s in sp.EnumerateArray())
                        species.Add(new Species(s.GetProperty("id").GetInt32(), s.GetProperty("name").GetString() ?? "",
                                                s.GetProperty("extractionItem").GetInt32()));
                if (root.TryGetProperty("rideGauges", out var rg))
                    foreach (var g in rg.EnumerateArray())
                        rideGauges.Add(new RideGauge(g.GetProperty("id").GetInt32(), g.GetProperty("max").GetInt32(),
                                                     g.GetProperty("minMood").GetInt32(), g.GetProperty("maxMood").GetInt32()));
                if (root.TryGetProperty("evolutive", out var ev))
                    foreach (var item in ev.EnumerateObject())
                    {
                        if (!int.TryParse(item.Name, out int gid)) continue;
                        evolutions[gid] = item.Value.EnumerateArray()
                            .Select(e => new Evolution(e.GetProperty("action").GetInt32(),
                                e.GetProperty("ranges").EnumerateArray()
                                 .Select(x => (x[0].GetInt32(), x[1].GetDouble())).ToList()))
                            .ToList();
                    }
                if (root.TryGetProperty("captureNets", out var ns))
                    foreach (var n in ns.EnumerateArray())
                        nets.Add(new CaptureNet(n.GetProperty("item").GetInt32(), n.GetProperty("spell").GetInt32(),
                                                n.GetProperty("wild").EnumerateArray().Select(w => w.GetInt32()).ToList(),
                                                n.GetProperty("radius").GetInt32(), n.GetProperty("multiplier").GetInt32()));
                if (root.TryGetProperty("wildRides", out var wr))
                    foreach (var w in wr.EnumerateObject())
                        if (int.TryParse(w.Name, out int monster)) wildRides[monster] = w.Value.GetInt32();
                if (root.TryGetProperty("rideExperience", out var xp) && xp.ValueKind == JsonValueKind.Object)
                {
                    coefficient = xp.GetProperty("coefficient").GetDouble();
                    exponent = xp.GetProperty("exponent").GetDouble();
                    maxLevel = xp.GetProperty("maxLevel").GetInt32();
                }
                Console.WriteLine($"[Breeding] {paddocks.Count} paddocks, {gauges.Count} gauges, {rides.Count} rides, " +
                                  $"{evolutions.Count} with characteristics by level.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Breeding] Could not read {Path.GetFileName(path)}: {ex.Message}");
            }
            var byItem = new Dictionary<int, Ride>();
            foreach (var ride in rides.Values)
                if (ride.Item > 0) byItem[ride.Item] = ride;
            return new Document(gateType, paddocks, gauges, rides, byItem, species, rideGauges, evolutions,
                                coefficient, exponent, Math.Max(1, maxLevel), nets, wildRides);
        }
    }
}

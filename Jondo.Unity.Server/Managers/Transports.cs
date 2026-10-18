using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jondo.Unity.Launcher;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The transport networks whose stops and fare the client names: the karts of route 666 in the
    /// Landas de Sidimote ("Kart de Nodoymas", seven stops, "Recorrer la carretera con Nodoymas por
    /// 100 kamas").
    /// </summary>
    /// <remarks>
    /// datos/transportes_3.6.10.11.json, from tools/extraer_transportes.py: the stops are the
    /// client's transport hints (category 9) of one name, the vehicle the element drawn at those
    /// stops and nowhere else, the fare the one text naming the driver and a price.
    ///
    /// INFERRED: what a vehicle does. A stop takes one to any other stop of its network for the
    /// fare, through the zaapi's destination window -- as the Frigost transporters, the same
    /// category of hint, go through a list of stops.
    /// </remarks>
    public static class Transports
    {
        /// <summary>A vehicle's declaration: no type of its own in the client, "Usar" as a passage.</summary>
        public const int Type = -1;
        public const int UseSkill = TeleportManager.UseSkill;

        public sealed record Stop(long MapId, Interactives.Element Vehicle, int SubAreaId, IReadOnlyList<long> Maps);

        public sealed class Network
        {
            public string Name { get; init; } = "";
            public int Cost { get; init; }
            public IReadOnlyList<Stop> Stops { get; init; } = Array.Empty<Stop>();
        }

        private static readonly List<Network> _networks = new();
        private static readonly Dictionary<long, (Network Network, Stop Stop)> _byMap = new();

        public static IReadOnlyList<Network> Networks => _networks;

        public static void Initialize()
        {
            _networks.Clear();
            _byMap.Clear();
            string path = Paths.Resolve("transportes_3.6.10.11.json");
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Transports] {Path.GetFileName(path)} is missing: no karts. Generate it with tools/extraer_transportes.py.");
                return;
            }
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var red in doc.RootElement.GetProperty("redes").EnumerateArray())
                {
                    var stops = new List<Stop>();
                    foreach (var p in red.GetProperty("paradas").EnumerateArray())
                    {
                        long map = p.GetProperty("mapa").GetInt64();
                        var vehicle = new Interactives.Element(p.GetProperty("elemento").GetInt32(),
                                                               p.GetProperty("casilla").GetInt32(),
                                                               p.GetProperty("gfx").GetInt32());
                        var maps = p.TryGetProperty("mapas", out var m)
                            ? m.EnumerateArray().Select(x => x.GetInt64()).ToList()
                            : new List<long> { map };
                        if (!maps.Contains(map)) maps.Add(map);
                        stops.Add(new Stop(map, vehicle, p.GetProperty("subzona").GetInt32(), maps));
                    }
                    var network = new Network
                    {
                        Name = red.GetProperty("nombre").GetString() ?? "",
                        Cost = red.GetProperty("coste").GetInt32(),
                        Stops = stops,
                    };
                    _networks.Add(network);
                    foreach (var stop in stops)
                        foreach (long map in stop.Maps)
                            _byMap[map] = (network, stop);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Transports] Could not read {Path.GetFileName(path)}: {ex.Message}");
                return;
            }
            foreach (var network in _networks)
                Console.WriteLine($"[Transports] {network.Name}: {network.Stops.Count} stops at {network.Cost} kamas.");
        }

        /// <summary>The network and stop whose vehicle this map shows, if any.</summary>
        public static (Network Network, Stop Stop)? On(long mapId)
            => _byMap.TryGetValue(mapId, out var found) ? found : null;

        /// <summary>The vehicle elements a map shows, to declare.</summary>
        public static IEnumerable<Interactives.Element> VehiclesOn(long mapId)
        {
            if (_byMap.TryGetValue(mapId, out var found))
            {
                // The element as this map's data has it: on a neighbour it stands on another cell.
                var here = Interactives.ElementsOf(mapId).FirstOrDefault(e => e.Id == found.Stop.Vehicle.Id);
                yield return here.Id != 0 ? here : found.Stop.Vehicle;
            }
        }

        /// <summary>The stop a network has on this map, as a destination; null when none.</summary>
        public static Stop? StopAt(Network network, long mapId)
            => network.Stops.FirstOrDefault(s => s.MapId == mapId);
    }
}

using Jondo.Unity.Launcher;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The world's houses: their doors, and which interior each one leads to.
    ///
    /// ─── What is measured ───────────────────────────────────────────────────────────────────
    ///
    /// That an element is a house door is said by the real server declaring it with TYPE
    /// 300, and that type brings three skills only a dwelling has: enter (84), access
    /// code (100) and put up for sale (98, or 108 if it already is). A building that is not a house comes
    /// out with type −1 and cannot even be clicked.
    ///
    /// Entering and leaving are NOT the same message, and that took some seeing:
    ///
    ///   enter    iwo { f1: skill, f2: element, f3: instance } →  iwn  →  jqw { f1: map }
    ///   leave    iwo { f1: skill, f2: element }               →  iwn  →  jru { f2: map }
    ///
    /// Mind the map's field number: in the jqw it goes in f1 and in the jru it goes in f2.
    ///
    /// The f3 of the entering iwo says WHICH INSTANCE is entered, and it is not a floor. When Ankama
    /// merged servers there were not houses for everybody, so one same door with one same
    /// interior came to belong to many people at once: each owner has his own copy
    /// of the SAME map, separate from the others. In the capture that building has eleven owners, not
    /// eleven floors.
    ///
    /// So a building here has as many COPIES as the highest instance the real server numbered on
    /// it (<see cref="Door.Copies"/>): 36 buildings have more than one -- the Casa grande de Bonta
    /// of the capture 24, Astrub's and Frigost's some too --, every other building one. Each copy
    /// is a house of its own, with its owner, code, chests and guild; the street declares the
    /// copies that have an owner and the first free one, and the client's door menu turns them into
    /// one entry per copy («Casa de X», «Casa sin dueño»: SkillMenuMaker.CreateMenu over the
    /// lpx f7 plaques). The iwo's f3, izv f1, jan f2, jai f4 and jao f3 say which copy.
    ///
    /// ─── What we decided ourselves ──────────────────────────────────────────────────────────
    ///
    /// Which map each door leads to IS NOT IN THE CLIENT. It was checked three ways:
    /// HousesDataRoot brings six fields and none is a map, the 569 map bundles do not carry
    /// a single field with «house», and «doorCell» gives zero hits in global-metadata.dat. That
    /// link is set by Ankama's server and we do not have it.
    ///
    /// What the client does have is its houses: an indoor map with a house chest is a house, the
    /// world graph of its own 2.73 client joins it to its other rooms (stairs, trapdoors, stars),
    /// and its way out is the room's door or a star no inside passage uses. They do not stand at
    /// their street's coordinates -- the burgo of Frigost keeps its 24 on a grid of their own,
    /// x 0..16, y 0..35, when its streets run from -85 to 0 -- so tools/casas_mundo.py gives each
    /// door one of its own subarea's houses (its area's when it has none: Bonta's and Brakmar's
    /// residences), the one at its coordinates first, then the one with its model's rooms, then
    /// the least used. 142 houses, 744 of the 765 doors behind one with a chest; the two doors the
    /// captures go through lead where they went in. Several doors share a house, and the copies
    /// keep it private all the same. The .json can be corrected by hand.
    ///
    /// ─── Owners ──────────────────────────────────────────────────────────────────────────────
    ///
    /// Every house whose model is known can have an owner (see <see cref="HouseStore"/>): 764 of
    /// the 765, the model measured for 37 doors and inferred from their subarea for the rest. The
    /// plaque names the house by its model (f4 of the jss f9 entry) and the model is where the
    /// price comes from.
    ///
    /// The plaque -- lnx -- only ever travels for a house WITH an owner: of the 1,276 plaques in
    /// the 34 capture folders, all 1,276 have one, and there is no sample of a free house. A
    /// house that can be owned and has no owner sends one all the same, without a name and at its
    /// model's price: the client opens no buyer's window for a house its map did not declare.
    ///
    /// The house id the protocol carries is ours, the building's rank in (map, element) order,
    /// stable while the doors are; a copy is that id and its instance. What is kept is keyed by
    /// the door and the instance, not by that number.
    /// </summary>
    public static class Houses
    {
        /// <summary>The type the client draws a house door with.</summary>
        public const int DoorType = 300;

        /// <summary>The «enter the house» skill.</summary>
        public const int EnterSkill = 84;

        /// <summary>
        /// "Comprar". Not in any capture: nobody bought a house. It is the client's own skill for
        /// it, with the same action (5) as the four door skills that are measured.
        /// </summary>
        public const int BuySkill = 97;

        /// <summary>"Vender": the owner's door when the house is not on sale, "poner casa en venta", frame 2.</summary>
        public const int SellSkill = 98;

        /// <summary>"Modificar el código", the owner's door, "cambiar codigo acceso", frame 5.</summary>
        public const int CodeSkill = 100;

        /// <summary>"Modificar el precio de venta": the owner's door once on sale, "retirar casa de la venta", frame 2.</summary>
        public const int PriceSkill = 108;

        /// <summary>
        /// The skills a door of a house that can be owned is registered with, each with its own
        /// skill instance. Which of them a viewer is offered is decided when the map is sent:
        /// see <see cref="Handlers.HouseHandler.DoorSkillsFor"/>.
        /// </summary>
        public static readonly IReadOnlyList<int> OwnableDoorSkills = new[] { EnterSkill, BuySkill, SellSkill, CodeSkill, PriceSkill };

        /// <summary>
        /// The first copy of every building, the only one of most. The real server numbers the
        /// owners of a building -- 22 is Sacrogrito69's in "Casas/" -- and the client sends the
        /// number back in iwo f3, izv f1, jan f2, jai f4 and jao f3.
        /// </summary>
        public const int FirstInstance = 1;

        /// <summary>A house chest, as the interior's jss declares it: type 85, "entrar en mi casa", frame 16.</summary>
        public const int ChestType = 85;

        /// <summary>"Abrir", the chest's first skill (104), and "Poner el cerrojo", its second (105).</summary>
        public const int ChestOpenSkill = 104;
        public const int ChestLockSkill = 105;

        /// <summary>
        /// The house chests: 12367 -- the one in the capture, element 522477 -- and 46581, which
        /// the captures declare with type 85 three times; 15817 and 682 are the same golden chest
        /// drawn another way, the one of Sufokia's, Astrub's and the breeders' houses.
        /// </summary>
        private static readonly HashSet<int> ChestGraphics = new() { 12367, 46581, 15817, 682 };

        /// <summary>The type of the inside door, the one leading back to the street.</summary>
        public const int ExitType = 316;

        /// <summary>The inside door's skill. It is the generic «use» one.</summary>
        public const int ExitSkill = 184;

        /// <summary>A house door: where it is, where it leads and which house it belongs to.</summary>
        public readonly struct Door
        {
            public Door(long mapId, int elementId, int cell, int gfx, long interiorMapId,
                        int model, string name, long price, int rooms, int copies)
            {
                MapId = mapId; ElementId = elementId; Cell = cell; Gfx = gfx;
                InteriorMapId = interiorMapId;
                Model = model; Name = name; Price = price; Rooms = rooms; Copies = Math.Max(1, copies);
            }

            public long MapId { get; }
            public int ElementId { get; }
            public int Cell { get; }
            public int Gfx { get; }
            public long InteriorMapId { get; }

            /// <summary>
            /// The house model, HousesDataRoot's typeId, or zero if it is not known.
            ///
            /// Only the 37 doors of the 25 maps where the house list the real server sends matches
            /// in number the doors we recognise have it. There they are paired in order, and the
            /// only checkable case says the order is right: door 522653 of map 212601864 comes out
            /// as the eleven-owner «Casa grande de Bonta», which is exactly the building entered in
            /// the capture.
            /// </summary>
            public int Model { get; }

            public string Name { get; }
            public long Price { get; }
            public int Rooms { get; }

            /// <summary>
            /// How many copies the building has, each with its owner and its copy of the same
            /// interior: the highest instance the real server numbered on it ("instancias" in the
            /// data), one when none was seen. They are not floors: see the class's explanation.
            /// </summary>
            public int Copies { get; }

            /// <summary>Whether this is one of the building's copies.</summary>
            public bool HasCopy(int instance) => instance >= FirstInstance && instance <= Copies;

            public bool IsKnown => Model != 0;

            /// <summary>Whether the house can have an owner: its model, and so its price, is known.</summary>
            public bool IsOwnable => IsKnown && Price > 0;
        }

        /// <summary>
        /// The element an interior is left through.
        ///
        /// When <see cref="IsRealDoor"/> is true, it is a real door —it carries drawing
        /// 44035, the one measured in the capture—. When it is false, it is the lowest-numbered
        /// element of the map and we chose it ourselves: it may be a piece of furniture. It is on purpose, because
        /// a piece of furniture one leaves through is better than an interior one cannot leave.
        /// </summary>
        public readonly struct Exit
        {
            public Exit(int elementId, int cell, int gfx, bool isRealDoor)
            {
                ElementId = elementId; Cell = cell; Gfx = gfx; IsRealDoor = isRealDoor;
            }

            public int ElementId { get; }
            public int Cell { get; }
            public int Gfx { get; }
            public bool IsRealDoor { get; }
        }

        /// <summary>
        /// The houses as one set: <see cref="Initialize"/> fills a new one on its own thread and
        /// publishes it whole, so whoever reads meanwhile -- a map being sent, the interactives being
        /// registered, a test running beside -- sees the old houses or the new ones, never a half.
        /// </summary>
        private sealed class Tables
        {
            public readonly Dictionary<long, List<Door>> ByMap = new();
            public readonly Dictionary<(long MapId, int ElementId), Door> ByElement = new();
            public readonly Dictionary<long, Exit> Exits = new();
            public readonly Dictionary<long, Door> WayBack = new();
            public readonly Dictionary<int, long> Canonical = new();
            public readonly Dictionary<(long MapId, int ElementId), int> HouseIds = new();
            public readonly Dictionary<int, Door> ByHouseId = new();
            public readonly Dictionary<long, long> HallOf = new();
            public readonly Dictionary<long, List<long>> Rooms = new();
        }

        private static volatile Tables _published = new();
        [ThreadStatic] private static Tables? _building;
        private static Tables Current => _building ?? _published;

        private static Dictionary<long, List<Door>> _byMap => Current.ByMap;
        private static Dictionary<(long MapId, int ElementId), Door> _byElement => Current.ByElement;
        private static Dictionary<long, Exit> _exits => Current.Exits;

        /// <summary>Which door leads back to the street from each interior.</summary>
        private static Dictionary<long, Door> _wayBack => Current.WayBack;

        /// <summary>
        /// The map each door's house is kept under: the lowest of the maps its element stands on.
        /// A door is an element, and 502 of the 765 stand on two to four maps -- the building
        /// crosses the edge --: clicked from any of them it is the same house, with one owner, one
        /// code and the same chests.
        /// </summary>
        private static Dictionary<int, long> _canonical => Current.Canonical;

        /// <summary>The house id of every door, and the door of every house id.</summary>
        private static Dictionary<(long MapId, int ElementId), int> _houseIds => Current.HouseIds;
        private static Dictionary<int, Door> _byHouseId => Current.ByHouseId;

        /// <summary>
        /// The other rooms of a house with its own interior (the data's "habitaciones"), each to
        /// the room one comes in by -- the Palacio de los Lagos encantados' fourteen. They are the
        /// house's as its entrance is: its chests, its owner. The ways between them are the client's
        /// own world graph's (interactive_teleports_worldgraph_2.73.json): its stars and doors.
        /// </summary>
        private static Dictionary<long, long> _hallOf => Current.HallOf;
        private static Dictionary<long, List<long>> _rooms => Current.Rooms;

        /// <summary>Every interior: the entrances, with their exit, and the other rooms of a house.</summary>
        public static IEnumerable<long> Interiors => _exits.Keys.Concat(_hallOf.Keys);

        /// <summary>The room one comes into a house by, for any of its rooms; the map itself otherwise.</summary>
        public static long HallOf(long mapId) => _hallOf.TryGetValue(mapId, out long hall) ? hall : mapId;

        /// <summary>The entrances that have other rooms, and those rooms.</summary>
        public static IEnumerable<long> HallsWithRooms => _rooms.Keys;

        public static IReadOnlyList<long> RoomsOf(long hall)
            => _rooms.TryGetValue(hall, out var rooms) ? rooms : (IReadOnlyList<long>)Array.Empty<long>();

        public static void Initialize()
        {
            var fresh = new Tables();
            _building = fresh;
            try
            {
                Fill();
            }
            finally
            {
                _building = null;
            }
            _published = fresh;
        }

        /// <summary>The loading proper, into the set being built (<see cref="Tables"/>).</summary>
        private static void Fill()
        {
            string path = Paths.Resolve("casas_mundo_3.6.10.10.json");
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Houses] {Path.GetFileName(path)} is missing; without it there are no houses. " +
                                  "Generate it with tools/casas_mundo.py.");
                return;
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty("puertas", out var list)) return;

                foreach (var entry in list.EnumerateArray())
                {
                    long mapId = entry.GetProperty("mapa").GetInt64();
                    int elementId = entry.GetProperty("elemento").GetInt32();
                    long interior = entry.TryGetProperty("interior", out var i) ? i.GetInt64() : 0;

                    // A door that leads nowhere is not declared: the player would click and nothing
                    // would happen, which is worse than not being able to click.
                    if (interior <= 0) continue;
                    if (MapManager.GetMapInfo(interior) == null) continue;

                    int copies = 1;
                    if (entry.TryGetProperty("instancias", out var flats))
                    {
                        foreach (var flat in flats.EnumerateArray()) copies = Math.Max(copies, flat.GetInt32());
                    }

                    var door = new Door(
                        mapId, elementId,
                        entry.TryGetProperty("casilla", out var c) ? c.GetInt32() : 0,
                        entry.TryGetProperty("gfx", out var g) ? g.GetInt32() : 0,
                        interior,
                        entry.TryGetProperty("casa", out var h) ? h.GetInt32() : 0,
                        entry.TryGetProperty("nombre", out var n) ? (n.GetString() ?? "") : "",
                        entry.TryGetProperty("precio", out var pr) ? pr.GetInt64() : 0,
                        entry.TryGetProperty("habitaciones", out var rm) ? rm.GetInt32() : 0,
                        copies);

                    if (_byElement.ContainsKey((mapId, elementId))) continue;
                    _byElement.Add((mapId, elementId), door);
                    if (!_byMap.TryGetValue(mapId, out var doors))
                    {
                        doors = new List<Door>();
                        _byMap.Add(mapId, doors);
                    }
                    doors.Add(door);
                }
                if (doc.RootElement.TryGetProperty("habitaciones", out var rooms))
                {
                    foreach (var entry in rooms.EnumerateObject())
                    {
                        if (!long.TryParse(entry.Name, out long hall)) continue;
                        var others = entry.Value.EnumerateArray().Select(r => r.GetInt64()).Where(r => r != hall).ToList();
                        _rooms[hall] = others;
                        foreach (long room in others) _hallOf[room] = hall;
                    }
                }
                if (doc.RootElement.TryGetProperty("salidas", out var exits))
                {
                    foreach (var entry in exits.EnumerateObject())
                    {
                        if (!long.TryParse(entry.Name, out long interior)) continue;
                        _exits[interior] = new Exit(
                            entry.Value.GetProperty("elemento").GetInt32(),
                            entry.Value.TryGetProperty("casilla", out var c) ? c.GetInt32() : 0,
                            entry.Value.TryGetProperty("gfx", out var g) ? g.GetInt32() : 0,
                            entry.Value.TryGetProperty("puerta", out var d) && d.GetBoolean());
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Houses] Could not read the doors: {ex.Message}");
                return;
            }

            // A door whose interior has no declared exit is not declared either: entering
            // there would lock the player in.
            var huerfanas = new List<(long, int)>();
            foreach (var pair in _byElement)
            {
                if (!_exits.ContainsKey(pair.Value.InteriorMapId)) huerfanas.Add(pair.Key);
            }
            foreach (var clave in huerfanas)
            {
                var door = _byElement[clave];
                _byElement.Remove(clave);
                if (_byMap.TryGetValue(door.MapId, out var lista)) lista.Remove(door);
            }
            foreach (long mapId in new List<long>(_byMap.Keys))
            {
                if (_byMap[mapId].Count == 0) _byMap.Remove(mapId);
            }
            if (huerfanas.Count > 0)
                Console.WriteLine($"[Houses] {huerfanas.Count} doors discarded: their interior has " +
                                  "no way out.");

            // And where one goes back through: the first door leading to each interior. It is taken
            // from the data and not from the session on purpose, so that leaving still works after
            // disconnecting inside a house.
            foreach (long mapId in SortedKeys(_byMap))
            {
                foreach (var door in _byMap[mapId])
                {
                    if (!_wayBack.ContainsKey(door.InteriorMapId)) _wayBack[door.InteriorMapId] = door;
                }
            }

            // Each door's house is its element's, kept under the lowest of its maps.
            foreach (var key in _byElement.Keys)
            {
                if (!_canonical.TryGetValue(key.ElementId, out long kept) || key.MapId < kept)
                    _canonical[key.ElementId] = key.MapId;
            }

            // The house ids: the houses' rank in (map, element) order, from 1 -- one per house, the
            // same from every map its door stands on.
            var houses = _canonical.Select(p => (MapId: p.Value, ElementId: p.Key)).OrderBy(k => k).ToList();
            var idOf = new Dictionary<int, int>();
            for (int i = 0; i < houses.Count; i++)
            {
                idOf[houses[i].ElementId] = i + 1;
                _byHouseId[i + 1] = _byElement[houses[i]];
            }
            foreach (var key in _byElement.Keys) _houseIds[key] = idOf[key.ElementId];

            int puertasDeVerdad = 0;
            foreach (var exit in _exits.Values)
            {
                if (exit.IsRealDoor) puertasDeVerdad++;
            }

            int conNombre = 0;
            foreach (var door in _byElement.Values)
            {
                if (door.IsKnown) conNombre++;
            }

            Console.WriteLine($"[Houses] {_byElement.Count} doors on {_byMap.Count} maps, " +
                              $"{_exits.Count} interiors ({puertasDeVerdad} with a real door), " +
                              $"{conNombre} with an identified house.");
        }

        public static IReadOnlyList<Door> On(long mapId)
            => _byMap.TryGetValue(mapId, out var doors)
                ? doors
                : (IReadOnlyList<Door>)Array.Empty<Door>();

        public static bool TryGetDoor(long mapId, int elementId, out Door door)
            => _byElement.TryGetValue((mapId, elementId), out door);

        /// <summary>Every house once: its door on the map it is kept under.</summary>
        public static IEnumerable<Door> AllHouses()
            => _canonical.OrderBy(p => (p.Value, p.Key)).Select(p => _byElement[(p.Value, p.Key)]);

        /// <summary>The map this door's house is kept under; the map itself for anything that is not a door.</summary>
        public static long CanonicalMapOf(long mapId, int elementId)
            => _byElement.ContainsKey((mapId, elementId)) && _canonical.TryGetValue(elementId, out long kept) ? kept : mapId;

        /// <summary>Every map a house's door stands on, for the house changes the client is told of.</summary>
        public static List<long> MapsOfDoor(int elementId)
            => _byElement.Keys.Where(k => k.ElementId == elementId).Select(k => k.MapId).OrderBy(m => m).ToList();

        /// <summary>Is this map the interior of some house?</summary>
        public static bool IsInterior(long mapId) => _exits.ContainsKey(mapId) || _hallOf.ContainsKey(mapId);

        public static bool TryGetExit(long interiorMapId, out Exit exit)
            => _exits.TryGetValue(interiorMapId, out exit);

        /// <summary>The door that leads back to the street from this interior.</summary>
        public static bool TryGetWayBack(long interiorMapId, out Door door)
            => _wayBack.TryGetValue(HallOf(interiorMapId), out door);

        /// <summary>The house id this door carries on the wire; zero for a door that is not a house.</summary>
        public static int HouseIdOf(long mapId, int elementId)
            => _houseIds.TryGetValue((mapId, elementId), out int id) ? id : 0;

        /// <summary>The door of a house id, as the client sends it back in izv f2.</summary>
        public static bool TryGetByHouseId(int houseId, out Door door)
            => _byHouseId.TryGetValue(houseId, out door);

        /// <summary>The chests of a whole house: its entrance's and its other rooms'.</summary>
        public static int ChestCountOf(long hall)
            => ChestsIn(hall).Count + RoomsOf(hall).Sum(room => ChestsIn(room).Count);

        /// <summary>The chests standing in an interior: the elements with a chest's graphic.</summary>
        public static List<Interactives.Element> ChestsIn(long interiorMapId)
        {
            var chests = new List<Interactives.Element>();
            if (!IsInterior(interiorMapId)) return chests;
            foreach (var element in Interactives.ElementsOf(interiorMapId))
            {
                if (ChestGraphics.Contains(element.Gfx)) chests.Add(element);
            }
            return chests;
        }

        private static List<long> SortedKeys(Dictionary<long, List<Door>> source)
        {
            var keys = new List<long>(source.Keys);
            keys.Sort();
            return keys;
        }
    }
}

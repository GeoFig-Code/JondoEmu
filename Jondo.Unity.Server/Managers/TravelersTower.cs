using System;
using System.Collections.Generic;
using System.Linq;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The Travelers' Tower ([-22,-24]) and the divine dimensions it opens: the teleporters between
    /// its rooms, the dimensional portal's mechanism and its portal, and each dimension's way back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured: «Interactivos varios/entrar y salir de dimensiones-usando creador de portales de
    /// la torre de viajeros» and «…/salir de anutropia por portal-navegar por salas de torre de los
    /// viajeros-lanzar busqueda portal». The rules are DofusPourLesNoobs' (Quêtes d'Osavora,
    /// Enutrosor): a dimension is reached by its portal once the lifelong alteration «Portal de X»
    /// is had, from the «Sala del portal dimensional», whose central mechanism chooses which
    /// portal to show.
    /// </para>
    /// <para>
    /// The mechanism (element 539177) offers each viewer «Abrir el portal de X» (skill 184,
    /// named 346-350) for every dimension whose alteration he has (763-767), and «Cerrar el
    /// portal» (359). The portal (539176) is the map's, one for everybody: its staging shows
    /// which dimension it is open to (the map's sequences 4-8, «Portal_enutrosor»…), and it lets
    /// through (skill 184) whoever has that dimension's alteration -- the second capture's
    /// character, without any, saw it with no skill. Every dimension has a portal back (skills 259
    /// «Permanecer en la dimensión» and 258 «Salir de la dimensión»), to the tower's cell 315.
    /// </para>
    /// <para>
    /// The rooms' teleporters (type 235, skills 449-456 «Sala de información»… «Prisión») take to
    /// the room's cell 522 (the information room's 257), and on the room one stands in, put him
    /// back on it (jse). Their staging is the room's «TP» (2). The «Prisión - Bajos fondos» (455)
    /// is offered by none of the seven captured.
    /// </para>
    /// <para>
    /// The alteration is had by finding the dimension's fissure: in the «Sala de localización»
    /// each dimension's model (types 441-445, «Localizar una fisura», skill 184 named 360)
    /// launches its legendary hunt (TreasureHunts), and winning the fight at its end gives the
    /// alteration to everybody who fought it (DofusPourLesNoobs, Quêtes d'Osavora). Level 50 at
    /// least, says the guide for Osavora; the others are taken to ask the same.
    /// </para>
    /// </remarks>
    public static class TravelersTower
    {
        /// <summary>A room: the teleporter skill that leads there, its map, its teleporter and where one lands.</summary>
        public sealed record Room(int Skill, string Name, long MapId, int Teleporter, int Cell);

        /// <summary>
        /// A divine dimension: its alteration, the mechanism's name for it, its staging, where its
        /// portal leads, and its model in the localization room with the legendary hunt it launches.
        /// </summary>
        public sealed record Dimension(string Name, int Alteration, int OpenName, int Staging, long EntryMap, int EntryCell,
                                       int Model, int ModelType, int Legendary);

        public const int TeleporterType = 235;
        public const int PortalType = 232;
        public const int CreatorType = -1;

        /// <summary>«Utilizar»: the mechanism's and the portal's skill.</summary>
        public const int UseSkill = 184;

        /// <summary>The dimensions' portals back: «Permanecer en la dimensión» and «Salir de la dimensión».</summary>
        public const int StaySkill = 259, LeaveSkill = 258;

        /// <summary>«Cerrar el portal», the mechanism's last name.</summary>
        public const int CloseName = 359;

        /// <summary>The portal room's sequences: the mechanism, the teleporter, the portal closing.</summary>
        public const int ConsoleStaging = 1, TeleporterStaging = 2, CloseStaging = 3;

        public const long PortalRoom = 130155011;
        public const int Creator = 539177, Portal = 539176;

        /// <summary>The «Sala de localización», with the dimensions' models.</summary>
        public const long FissureRoom = 130025985;

        /// <summary>«Localizar una fisura», the models' skill name.</summary>
        public const int LocateName = 360;

        /// <summary>The level a fissure's hunt asks for.</summary>
        public const int LocateLevel = 50;

        /// <summary>Where one lands in the portal room coming back from a dimension.</summary>
        public const int BackCell = 315;

        /// <summary>The rooms in the order their teleporters list them (the captures' jss).</summary>
        public static readonly IReadOnlyList<Room> Rooms = new[]
        {
            new Room(456, "Prisión", 130023937, 539182, 522),
            new Room(454, "Sala del generador", 130023939, 539183, 522),
            new Room(453, "Sala del portal dimensional", PortalRoom, 539172, 522),
            new Room(452, "Vestíbulo de los viajeros", 130024961, 539184, 522),
            new Room(451, "Sala de localización", 130025985, 539186, 522),
            new Room(450, "Laboratorio", 130024963, 539185, 522),
            new Room(449, "Sala de información", 130025987, 539187, 257),
        };

        /// <summary>
        /// The five dimensions. Entry cells measured for Anutropía, Sramvil and Zurcalia; Xelorium's
        /// and Osavora's are their portal back's cells, the nearest walkable taken on arrival. The
        /// models as the capture's jss declares them, their type named after the dimension; the
        /// legendary hunts are the client's «Tesoro de X» rows, whose monster is «Fisura de X».
        /// </summary>
        public static readonly IReadOnlyList<Dimension> Dimensions = new[]
        {
            new Dimension("Anutropía", 763, 346, 4, 131596288, 429, 484967, 441, 58),
            new Dimension("Sramvil", 764, 347, 5, 134351108, 372, 485536, 442, 59),
            new Dimension("Xelorium", 765, 348, 6, 145100034, 269, 486664, 443, 61),
            new Dimension("Zurcalia", 766, 349, 7, 161351684, 399, 502767, 444, 60),
            new Dimension("Osavora", 767, 350, 8, 234751496, 516, 539218, 445, 62),
        };

        /// <summary>The dimensions' portals back, by map: the arrival's and the others the client data draws.</summary>
        public static readonly IReadOnlyList<(long MapId, int Element)> WaysBack = new[]
        {
            (131596288L, 539188), (131729408L, 539194), (134351108L, 539191), (145100034L, 539196),
            (161351684L, 539197), (161353732L, 539198), (234751496L, 539199),
        };

        /// <summary>
        /// A skill instance of these elements: apart from the registry's elementId + 10000, which
        /// would give the room's teleporter the instances of the portal and the mechanism beside it.
        /// </summary>
        public static int InstanceOf(int elementId, int index) => 5_000_000 + (elementId % 900_000) * 10 + index;

        public static Room RoomOf(int skill) => Rooms.FirstOrDefault(r => r.Skill == skill);

        public static Dimension DimensionOf(int openName) => Dimensions.FirstOrDefault(d => d.OpenName == openName);

        public static Dimension ModelOf(int element) => Dimensions.FirstOrDefault(d => d.Model == element);

        public static Dimension LegendaryOf(int legendary) => Dimensions.FirstOrDefault(d => d.Legendary == legendary);

        public static bool IsWayBack(long mapId, int element) => WaysBack.Contains((mapId, element));

        // ─── The portal ─────────────────────────────────────────────────────

        private static readonly object _lock = new();
        private static Dimension _open;

        /// <summary>The dimension the portal is open to, or null while closed.</summary>
        public static Dimension Open
        {
            get { lock (_lock) return _open; }
        }

        /// <summary>
        /// The mechanism used: the portal open to a dimension, or closed (null). The stagings that
        /// show it, in the capture's order: the mechanism, the open portal closing, the new one.
        /// </summary>
        public static List<int> Switch(Dimension to)
        {
            lock (_lock)
            {
                var stagings = new List<int> { ConsoleStaging };
                if (_open != null && _open != to) stagings.Add(CloseStaging);
                if (to != null && to != _open) stagings.Add(to.Staging);
                _open = to;
                return stagings;
            }
        }

        public static void Reset()
        {
            lock (_lock) _open = null;
        }

        /// <summary>The mechanism's names a character is offered: «Cerrar el portal», then his dimensions, as the capture lists them.</summary>
        public static List<int> CreatorNamesFor(long characterId, Func<long, int, bool> has)
        {
            var names = new List<int> { CloseName };
            foreach (var dimension in Dimensions.OrderByDescending(d => d.OpenName))
                if (has(characterId, dimension.Alteration)) names.Add(dimension.OpenName);
            return names;
        }

        /// <summary>Whether the portal lets this character through: open, and to a dimension of his.</summary>
        public static bool LetsThrough(long characterId, Func<long, int, bool> has)
        {
            var open = Open;
            return open != null && has(characterId, open.Alteration);
        }
    }
}

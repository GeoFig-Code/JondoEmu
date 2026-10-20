using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The divine dimensions' portals out in the world: one for each of Anutropía, Sramvil,
    /// Xelorium and Zurcalia, on an outdoor map drawn at random, until it has been crossed so many
    /// times or nobody has crossed it for a day -- then it closes and opens somewhere else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No capture shows one; the client has them all (Ghidra, 3.6.10.11). A portal is a map actor
    /// of kind 3 (lnh { f2 the portal, f3 the dimension's area }), drawn with its NPC's look and
    /// named after the area. Its menu offers «Hablar» -- the NPC dialogue (iov 3), whose ioc
    /// carries the portal's type in f2 so that the client makes the portal's NPC speak (fsw:
    /// 1 → 2374 «Portal de Anutropía», 2 → 2594, 3 → 2667, 4 → 3136) -- and «Usar», which asks
    /// to be sure and sends hhu { f3 the portal }. The NPC's sentence is «*evaluar la energía
    /// del portal…* Queda(n) #1 utilización(es) antes de que se cierre el portal. El portal
    /// desaparecerá en $time#2 si nadie lo utiliza», #2 the closing time in unix seconds; its
    /// one reply, «Atravesar el portal hacia X», goes through.
    /// </para>
    /// <para>
    /// The numbers are JOL's (Dimensions Divines): 128 crossings, and closed after 24 hours
    /// unused. The maps are the hunts' outdoor ones (TreasureHunts.World).
    /// </para>
    /// <para>
    /// The game took these portals out with its 3.2 update (July 2025, Osavora): the way into a
    /// dimension became the Travelers' Tower's portal with its alteration, and the localization
    /// room's models answer «not available» to whoever has it -- no hunt leads to a world portal
    /// any more. So they are a server setting (<see cref="ServerSettings.WorldPortals"/>), off by
    /// default.
    /// </para>
    /// <para>
    /// Kept in world.db, in a table of its own created at first use:
    /// </para>
    /// <code>
    ///   DimensionPortals (PortalType, MapId, Cell, UsesLeft, ClosesMs)
    /// </code>
    /// </remarks>
    public static class DimensionPortals
    {
        /// <summary>How many times a portal can be crossed before it closes.</summary>
        public const int Uses = 128;

        /// <summary>How long a portal stays open with nobody crossing it.</summary>
        public static readonly TimeSpan IdleLife = TimeSpan.FromHours(24);

        /// <summary>The map actor kind (lja) of a portal.</summary>
        public const int ActorKind = 3;

        /// <summary>The portals' actor ids, one each, below everything else's.</summary>
        public const long ContextBase = -980000;

        public sealed class Portal
        {
            public TravelersTower.Dimension Dimension { get; init; } = null!;
            public long MapId { get; set; }
            public int Cell { get; set; }
            public int UsesLeft { get; set; }
            public long ClosesMs { get; set; }

            public int Type => Dimension.PortalType;
            public long ContextId => ContextBase - Type;
        }

        private static readonly object _lock = new();
        private static readonly Dictionary<int, Portal> _portals = new();
        private static bool _tableReady;

        /// <summary>The dimensions that have a portal in the world.</summary>
        public static IEnumerable<TravelersTower.Dimension> WithPortal
            => TravelersTower.Dimensions.Where(d => d.PortalType > 0);

        /// <summary>Every portal open now.</summary>
        public static IReadOnlyList<Portal> All
        {
            get { lock (_lock) return _portals.Values.ToList(); }
        }

        public static Portal? OfType(int type)
        {
            lock (_lock) return _portals.TryGetValue(type, out var portal) ? portal : null;
        }

        public static Portal? ByContext(long contextId)
        {
            lock (_lock) return _portals.Values.FirstOrDefault(p => p.ContextId == contextId);
        }

        public static IReadOnlyList<Portal> OnMap(long mapId)
        {
            lock (_lock) return _portals.Values.Where(p => p.MapId == mapId).ToList();
        }

        /// <summary>
        /// Somewhere for a portal: a map of the hunts' world no other portal stands on, and a cell
        /// of it <paramref name="cellsOf"/> offers. Null when none is found.
        /// </summary>
        public static (long MapId, int Cell)? Place(TreasureHunts.Grid grid, IEnumerable<long> taken,
                                                  Func<long, IReadOnlyList<int>> cellsOf, Random dice)
        {
            var maps = grid.Maps.ToList();
            var busy = taken.ToHashSet();
            for (int attempt = 0; attempt < 200 && maps.Count > 0; attempt++)
            {
                long mapId = maps[dice.Next(maps.Count)];
                if (busy.Contains(mapId)) continue;
                var cells = cellsOf(mapId);
                if (cells.Count == 0) continue;
                return (mapId, cells[dice.Next(cells.Count)]);
            }
            return null;
        }

        /// <summary>A portal opened anew: full uses, a day before it closes unused.</summary>
        public static void Open(Portal portal, long mapId, int cell, long nowMs)
        {
            portal.MapId = mapId;
            portal.Cell = cell;
            portal.UsesLeft = Uses;
            portal.ClosesMs = nowMs + (long)IdleLife.TotalMilliseconds;
        }

        /// <summary>A crossing: one use less, and the day starts again. True when that was its last.</summary>
        public static bool Cross(Portal portal, long nowMs)
        {
            portal.UsesLeft = Math.Max(0, portal.UsesLeft - 1);
            portal.ClosesMs = nowMs + (long)IdleLife.TotalMilliseconds;
            return portal.UsesLeft == 0;
        }

        public static bool Expired(Portal portal, long nowMs) => portal.ClosesMs <= nowMs;

        /// <summary>The portals as kept, and those missing or off the hunts' world placed anew; each change kept.</summary>
        public static void Load(TreasureHunts.Grid grid, Func<long, IReadOnlyList<int>> cellsOf, long nowMs, Random dice)
        {
            var kept = Read();
            lock (_lock)
            {
                _portals.Clear();
                foreach (var dimension in WithPortal)
                {
                    var portal = new Portal { Dimension = dimension };
                    if (kept.TryGetValue(dimension.PortalType, out var row) && grid.Contains(row.MapId))
                    {
                        portal.MapId = row.MapId;
                        portal.Cell = row.Cell;
                        portal.UsesLeft = row.UsesLeft;
                        portal.ClosesMs = row.ClosesMs;
                    }
                    else
                    {
                        var place = Place(grid, _portals.Values.Select(p => p.MapId), cellsOf, dice);
                        if (place == null) continue;
                        Open(portal, place.Value.MapId, place.Value.Cell, nowMs);
                        Store(portal);
                    }
                    _portals[portal.Type] = portal;
                }
            }
        }

        /// <summary>A portal closed and opened elsewhere. Returns the map it was on, or 0 when it could not move.</summary>
        public static long Move(Portal portal, TreasureHunts.Grid grid, Func<long, IReadOnlyList<int>> cellsOf, long nowMs, Random dice)
        {
            lock (_lock)
            {
                var place = Place(grid, _portals.Values.Select(p => p.MapId), cellsOf, dice);
                if (place == null) return 0;
                long from = portal.MapId;
                Open(portal, place.Value.MapId, place.Value.Cell, nowMs);
                Store(portal);
                return from;
            }
        }

        /// <summary>A portal kept as it now is.</summary>
        public static void Save(Portal portal)
        {
            lock (_lock) Store(portal);
        }

        /// <summary>For the tests: a portal set by hand.</summary>
        internal static void Put(Portal portal)
        {
            lock (_lock) _portals[portal.Type] = portal;
        }

        internal static void Forget()
        {
            lock (_lock) _portals.Clear();
        }

        private sealed record Row(long MapId, int Cell, int UsesLeft, long ClosesMs);

        private static Dictionary<int, Row> Read()
        {
            var rows = new Dictionary<int, Row>();
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT PortalType, MapId, Cell, UsesLeft, ClosesMs FROM DimensionPortals;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    rows[reader.GetInt32(0)] = new Row(reader.GetInt64(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt64(4));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Portals] Could not read the portals: {ex.Message}");
            }
            return rows;
        }

        private static void Store(Portal portal)
        {
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "INSERT INTO DimensionPortals (PortalType, MapId, Cell, UsesLeft, ClosesMs) VALUES ($t, $m, $c, $u, $x) " +
                    "ON CONFLICT(PortalType) DO UPDATE SET MapId = excluded.MapId, Cell = excluded.Cell, " +
                    "UsesLeft = excluded.UsesLeft, ClosesMs = excluded.ClosesMs;";
                command.Parameters.AddWithValue("$t", portal.Type);
                command.Parameters.AddWithValue("$m", portal.MapId);
                command.Parameters.AddWithValue("$c", portal.Cell);
                command.Parameters.AddWithValue("$u", portal.UsesLeft);
                command.Parameters.AddWithValue("$x", portal.ClosesMs);
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Portals] Could not store the portal to {portal.Dimension.Name}: {ex.Message}");
            }
        }

        private static void EnsureTable()
        {
            if (_tableReady) return;
            using var connection = Open();
            using var create = connection.CreateCommand();
            create.CommandText = @"
                CREATE TABLE IF NOT EXISTS DimensionPortals (
                    PortalType INTEGER PRIMARY KEY,
                    MapId      INTEGER NOT NULL,
                    Cell       INTEGER NOT NULL,
                    UsesLeft   INTEGER NOT NULL,
                    ClosesMs   INTEGER NOT NULL);";
            create.ExecuteNonQuery();
            _tableReady = true;
        }

        private static SqliteConnection Open()
        {
            var connection = new SqliteConnection(DatabaseManager.WorldConnectionString);
            connection.Open();
            return connection;
        }
    }
}

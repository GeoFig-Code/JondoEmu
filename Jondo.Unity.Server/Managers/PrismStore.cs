using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The alliances' prisms, one per subarea at most, stored in world.db.
    /// </summary>
    /// <remarks>
    /// A prism holds a subarea for its alliance: the world map lists them by subarea (ife at world
    /// entry, the iew of one gone names only its subarea), «Ya hay un prisma en esta zona» (info
    /// 1/149) refuses a second one, and the subarea must be one the client's data marks capturable.
    /// </remarks>
    public static class PrismStore
    {
        /// <summary>A prism standing on a map.</summary>
        public sealed class Prism
        {
            public long Id { get; init; }
            public long AllianceId { get; init; }
            public int SubAreaId { get; init; }
            public long MapId { get; init; }
            public int CellId { get; init; }
            public int Direction { get; init; }

            /// <summary>When it was placed, in Unix milliseconds: the ley's f2 while it stands normal.</summary>
            public long PlacedUtcMs { get; init; }

            /// <summary>Normal, weakened, vulnerable or pacified: see <see cref="Normal"/>.</summary>
            public int State { get; set; }

            /// <summary>
            /// When a weakened prism turns vulnerable, a vulnerable one's conquest runs out, or a
            /// pacified one goes back to normal, in Unix milliseconds; 0 when it stands normal.
            /// </summary>
            public long NextChangeUtcMs { get; set; }

            /// <summary>How many crystals it has consumed in its life: four at most (Translations 1149314).</summary>
            public int CrystalsUsed { get; set; }

            /// <summary>The template of the crystal it holds, while it lasts; 0 for none.</summary>
            public int CrystalGid { get; set; }

            /// <summary>Whether a crystal pacifies it now: no attack, and no teleport to it.</summary>
            public bool HasActiveCrystal => CrystalGid != 0 && State == Pacified;

            /// <summary>Its id among the map's actors, in a range of its own.</summary>
            public long ContextId => ContextBase - Id;
        }

        /// <summary>Where the prisms' actor ids start, going down, far below the tax collectors'.</summary>
        public const long ContextBase = -900000;

        /// <summary>
        /// A prism's four states, the client's lew (Translations 1149320): normal, the alliance holds
        /// the zone; weakened, it was beaten and will soon turn vulnerable; vulnerable, a conquest
        /// is being fought over it; pacified, protected from attacks. The conquests window colours
        /// them white, yellow, red and pink (AllianceConquestsUi.SetPrismLabels).
        /// </summary>
        public const int Normal = 0;
        public const int Weakened = 1;
        public const int Vulnerable = 2;
        public const int Pacified = 3;

        public static void EnsureTables(SqliteConnection world)
        {
            var create = world.CreateCommand();
            create.CommandText = @"
                CREATE TABLE IF NOT EXISTS Prisms (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    AllianceId INTEGER NOT NULL,
                    SubAreaId INTEGER NOT NULL,
                    MapId INTEGER NOT NULL,
                    CellId INTEGER NOT NULL DEFAULT 0,
                    Direction INTEGER NOT NULL DEFAULT 1,
                    PlacedUtcMs INTEGER NOT NULL DEFAULT 0,
                    State INTEGER NOT NULL DEFAULT 0,
                    NextChangeUtcMs INTEGER NOT NULL DEFAULT 0,
                    CrystalsUsed INTEGER NOT NULL DEFAULT 0,
                    CrystalGid INTEGER NOT NULL DEFAULT 0
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IdxPrismsSubArea ON Prisms (SubAreaId);";
            create.ExecuteNonQuery();

            // The state and the crystals came later: a table of before gets their columns.
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var info = world.CreateCommand();
            info.CommandText = "PRAGMA table_info(Prisms);";
            using (var reader = info.ExecuteReader())
                while (reader.Read()) columns.Add(reader.GetString(1));
            foreach (var column in new[] { "State", "NextChangeUtcMs", "CrystalsUsed", "CrystalGid" })
            {
                if (columns.Contains(column)) continue;
                var alter = world.CreateCommand();
                alter.CommandText = $"ALTER TABLE Prisms ADD COLUMN {column} INTEGER NOT NULL DEFAULT 0;";
                alter.ExecuteNonQuery();
            }
        }

        /// <summary>A connection string a test can point at a temp database; null uses world.db.</summary>
        internal static string ConnectionStringOverride
        {
            get => _override;
            set { _override = value; _bySubArea = null; }
        }
        private static string _override;

        private static SqliteConnection Open()
        {
            var conexion = new SqliteConnection(_override ?? DatabaseManager.WorldConnectionString);
            conexion.Open();
            EnsureTables(conexion);
            return conexion;
        }

        /// <summary>Every prism by its subarea, read once: every world entry lists them all.</summary>
        private static ConcurrentDictionary<int, Prism> _bySubArea;
        private static readonly object _loading = new();

        private static ConcurrentDictionary<int, Prism> BySubArea
        {
            get
            {
                var loaded = _bySubArea;
                if (loaded != null) return loaded;
                lock (_loading)
                {
                    if (_bySubArea != null) return _bySubArea;
                    var all = new ConcurrentDictionary<int, Prism>();
                    using var conexion = Open();
                    var select = conexion.CreateCommand();
                    select.CommandText = "SELECT Id, AllianceId, SubAreaId, MapId, CellId, Direction, PlacedUtcMs, State, NextChangeUtcMs, CrystalsUsed, CrystalGid FROM Prisms;";
                    using var lector = select.ExecuteReader();
                    while (lector.Read())
                    {
                        var prism = new Prism
                        {
                            Id = lector.GetInt64(0),
                            AllianceId = lector.GetInt64(1),
                            SubAreaId = lector.GetInt32(2),
                            MapId = lector.GetInt64(3),
                            CellId = lector.GetInt32(4),
                            Direction = lector.GetInt32(5),
                            PlacedUtcMs = lector.GetInt64(6),
                            State = lector.GetInt32(7),
                            NextChangeUtcMs = lector.GetInt64(8),
                            CrystalsUsed = lector.GetInt32(9),
                            CrystalGid = lector.GetInt32(10),
                        };
                        all[prism.SubAreaId] = prism;
                    }
                    _bySubArea = all;
                    return all;
                }
            }
        }

        /// <summary>Places a prism and returns it; null when its subarea already has one.</summary>
        public static Prism Place(long allianceId, int subAreaId, long mapId, int cellId, int direction,
                                  int state = Normal, long nextChangeUtcMs = 0)
        {
            if (InSubArea(subAreaId) != null) return null;
            using var conexion = Open();
            long placed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var insert = conexion.CreateCommand();
            insert.CommandText = @"
                INSERT INTO Prisms (AllianceId, SubAreaId, MapId, CellId, Direction, PlacedUtcMs, State, NextChangeUtcMs)
                VALUES ($alliance, $subarea, $map, $cell, $dir, $placed, $state, $next);
                SELECT last_insert_rowid();";
            insert.Parameters.AddWithValue("$alliance", allianceId);
            insert.Parameters.AddWithValue("$subarea", subAreaId);
            insert.Parameters.AddWithValue("$map", mapId);
            insert.Parameters.AddWithValue("$cell", cellId);
            insert.Parameters.AddWithValue("$dir", direction);
            insert.Parameters.AddWithValue("$placed", placed);
            insert.Parameters.AddWithValue("$state", state);
            insert.Parameters.AddWithValue("$next", nextChangeUtcMs);
            long id;
            try { id = Convert.ToInt64(insert.ExecuteScalar()); }
            catch (SqliteException) { return null; }

            var prism = new Prism
            {
                Id = id, AllianceId = allianceId, SubAreaId = subAreaId, MapId = mapId,
                CellId = cellId, Direction = direction, PlacedUtcMs = placed,
                State = state, NextChangeUtcMs = nextChangeUtcMs,
            };
            BySubArea[subAreaId] = prism;
            return prism;
        }

        public static Prism InSubArea(int subAreaId) => BySubArea.TryGetValue(subAreaId, out var p) ? p : null;

        public static Prism OnMap(long mapId) => BySubArea.Values.FirstOrDefault(p => p.MapId == mapId);

        public static Prism ByContextId(long contextId) => BySubArea.Values.FirstOrDefault(p => p.ContextId == contextId);

        /// <summary>Every prism of the world, by subarea.</summary>
        public static List<Prism> All() => BySubArea.Values.OrderBy(p => p.SubAreaId).ToList();

        public static List<Prism> OfAlliance(long allianceId)
            => BySubArea.Values.Where(p => p.AllianceId == allianceId).OrderBy(p => p.Id).ToList();

        /// <summary>A prism's state changed, and when the next change comes.</summary>
        public static void SetState(Prism prism, int state, long nextChangeUtcMs)
        {
            if (prism == null) return;
            using var conexion = Open();
            var update = conexion.CreateCommand();
            update.CommandText = "UPDATE Prisms SET State = $state, NextChangeUtcMs = $next WHERE Id = $id;";
            update.Parameters.AddWithValue("$state", state);
            update.Parameters.AddWithValue("$next", nextChangeUtcMs);
            update.Parameters.AddWithValue("$id", prism.Id);
            update.ExecuteNonQuery();
            prism.State = state;
            prism.NextChangeUtcMs = nextChangeUtcMs;
        }

        /// <summary>The crystal it holds now, 0 for none, and how many it has consumed.</summary>
        public static void SetCrystal(Prism prism, int crystalGid, int crystalsUsed)
        {
            if (prism == null) return;
            using var conexion = Open();
            var update = conexion.CreateCommand();
            update.CommandText = "UPDATE Prisms SET CrystalGid = $gid, CrystalsUsed = $used WHERE Id = $id;";
            update.Parameters.AddWithValue("$gid", crystalGid);
            update.Parameters.AddWithValue("$used", crystalsUsed);
            update.Parameters.AddWithValue("$id", prism.Id);
            update.ExecuteNonQuery();
            prism.CrystalGid = crystalGid;
            prism.CrystalsUsed = crystalsUsed;
        }

        public static void Remove(Prism prism)
        {
            if (prism == null) return;
            using var conexion = Open();
            var delete = conexion.CreateCommand();
            delete.CommandText = "DELETE FROM Prisms WHERE Id = $id;";
            delete.Parameters.AddWithValue("$id", prism.Id);
            delete.ExecuteNonQuery();
            BySubArea.TryRemove(prism.SubAreaId, out _);
        }
    }
}

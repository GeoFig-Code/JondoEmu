using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// Who owns each house, what it is on sale for and its codes, kept in world.db.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per ACCOUNT, as the captures show it: the plaque names the owner by the account's nickname
    /// and tag -- f8 { f1: "OwnerAccount", f2: "0001" } in every house frame of "Casas/" -- and jaa,
    /// the list of one's houses, comes with the account at the world entry. Every character of the
    /// account is the owner.
    /// </para>
    /// <para>
    /// A house is one copy of a building: its street door, (map, element), and its instance --
    /// most buildings have one copy, some many (see <see cref="Houses"/>). Two tables, created at
    /// start with CREATE TABLE IF NOT EXISTS -- datos/world.zip, the base CI tests against, has
    /// never heard of them:
    ///
    ///   Houses          (MapId, ElementId, Instance, AccountId, Price, AccessCode, GuildId, GuildRights)
    ///   HouseChestCodes (MapId, ElementId, Instance, ChestElementId, Code)
    ///
    /// A copy without a row has no owner and is on sale at its model's price; a row with a zero
    /// price is not on sale. What its chests hold is in <see cref="StorageStacks"/>. Tables made
    /// before the copies get them at start, every house kept being its building's first copy.
    /// </para>
    /// <para>
    /// Thread-safety: every change to a house takes that house's gate, so a buyer and the owner
    /// taking it off sale at the same moment queue instead of both winning.
    /// </para>
    /// </remarks>
    public static class HouseStore
    {
        /// <summary>A house as the store keeps it.</summary>
        public sealed class House
        {
            public long MapId { get; init; }
            public int ElementId { get; init; }

            /// <summary>Which copy of the building it is.</summary>
            public int Instance { get; init; } = Houses.FirstInstance;

            /// <summary>The owning account; zero when nobody owns it.</summary>
            public long AccountId { get; init; }

            /// <summary>What its owner asks for it; zero when it is not on sale.</summary>
            public long Price { get; init; }

            /// <summary>The access code; empty when anyone may come in.</summary>
            public string AccessCode { get; init; } = "";

            /// <summary>The guild the house is shared with; zero when it is not.</summary>
            public long GuildId { get; init; }

            /// <summary>What the guild may do there: the client's bits, see <see cref="GuildRights"/>.</summary>
            public int GuildRights { get; init; }

            public bool SharedWithGuild => GuildId > 0;

            public bool Owned => AccountId > 0;
            public bool ForSale => Owned && Price > 0;
            public bool Locked => AccessCode.Length > 0;
        }

        /// <summary>What a purchase did.</summary>
        public sealed class Purchase
        {
            public long Price { get; init; }

            /// <summary>Whom the money went to; zero when the house had no owner.</summary>
            public long SellerAccountId { get; init; }

            /// <summary>The buyer's purse once paid.</summary>
            public long BuyerKamas { get; init; }

            /// <summary>What the house's chests held, now in the seller's bank.</summary>
            public int LotsToSeller { get; init; }
        }

        /// <summary>Why a purchase did not happen.</summary>
        public enum Refusal
        {
            None,
            NotForSale,
            PriceChanged,
            OwnHouse,
            NotEnoughKamas,
            SellerBankFull,
            Failed,
        }

        /// <summary>
        /// The longest code the client lets one type: kia's f3, 8 in the five code dialogs of the
        /// captures (the house's and the chest's alike).
        /// </summary>
        public const int CodeLength = 8;

        /// <summary>
        /// What a guild may do in a house shared with it: the bits of the client's guild house
        /// window (PersonalGuildHouseManagerUi.ComputeRightsAsInt), one a checkbox, the same as
        /// Dofus 1.29's.
        /// </summary>
        public static class GuildRights
        {
            /// <summary>«Compartir con el gremio».</summary>
            public const int Share = 1;
            /// <summary>The guild comes in without the code.</summary>
            public const int HouseGuild = 8;
            /// <summary>Nobody outside the guild comes in, not even with the code.</summary>
            public const int HouseNoGuild = 16;
            /// <summary>The guild opens the chests without their codes.</summary>
            public const int ChestGuild = 32;
            /// <summary>Nobody outside the guild opens the chests.</summary>
            public const int ChestNoGuild = 64;
            public const int Teleport = 128;
            public const int Rest = 256;
        }

        private static readonly ConcurrentDictionary<(long, int, int), SemaphoreSlim> _gates = new();
        private static readonly object _tablesLock = new object();
        private static volatile bool _tablesReady;

        private static SemaphoreSlim GateOf(long mapId, int elementId, int instance)
            => _gates.GetOrAdd((mapId, elementId, instance), _ => new SemaphoreSlim(1, 1));

        /// <summary>The columns a house is read with, in the order <see cref="Read"/> takes them.</summary>
        private const string Columns = "MapId, ElementId, Instance, AccountId, Price, AccessCode, GuildId, GuildRights";

        private static House Read(SqliteDataReader reader) => new House
        {
            MapId = reader.GetInt64(0),
            ElementId = reader.GetInt32(1),
            Instance = reader.GetInt32(2),
            AccountId = reader.GetInt64(3),
            Price = reader.GetInt64(4),
            AccessCode = reader.IsDBNull(5) ? "" : reader.GetString(5),
            GuildId = reader.GetInt64(6),
            GuildRights = reader.GetInt32(7),
        };

        // ─── Tables ─────────────────────────────────────────────────────────────

        public static void Initialize()
        {
            EnsureTables();
            MoveToTheirDoors();
            Console.WriteLine($"[Houses] {OwnedCount()} house(s) with an owner, {ForSaleCount()} of them on sale.");
        }

        private static void EnsureTables()
        {
            if (_tablesReady) return;
            lock (_tablesLock)
            {
                if (_tablesReady) return;
                try
                {
                    using var connection = Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = @"
                        CREATE TABLE IF NOT EXISTS Houses (
                            MapId       INTEGER NOT NULL,
                            ElementId   INTEGER NOT NULL,
                            Instance    INTEGER NOT NULL DEFAULT 1,
                            AccountId   INTEGER NOT NULL DEFAULT 0,
                            Price       INTEGER NOT NULL DEFAULT 0,
                            AccessCode  TEXT NOT NULL DEFAULT '',
                            GuildId     INTEGER NOT NULL DEFAULT 0,
                            GuildRights INTEGER NOT NULL DEFAULT 0,
                            PRIMARY KEY (MapId, ElementId, Instance));

                        CREATE TABLE IF NOT EXISTS HouseChestCodes (
                            MapId          INTEGER NOT NULL,
                            ElementId      INTEGER NOT NULL,
                            Instance       INTEGER NOT NULL DEFAULT 1,
                            ChestElementId INTEGER NOT NULL,
                            Code           TEXT NOT NULL DEFAULT '',
                            PRIMARY KEY (MapId, ElementId, Instance, ChestElementId));";
                    command.ExecuteNonQuery();

                    // Tables made before the guild sharing, and before the copies.
                    var houses = ColumnsOf(connection, "Houses");
                    foreach (var column in new[] { "GuildId", "GuildRights" })
                    {
                        if (houses.Contains(column)) continue;
                        using var alter = connection.CreateCommand();
                        alter.CommandText = $"ALTER TABLE Houses ADD COLUMN {column} INTEGER NOT NULL DEFAULT 0;";
                        alter.ExecuteNonQuery();
                    }
                    if (!houses.Contains("Instance"))
                    {
                        Rebuild(connection, "Houses", @"
                            CREATE TABLE HousesByCopy (
                                MapId       INTEGER NOT NULL,
                                ElementId   INTEGER NOT NULL,
                                Instance    INTEGER NOT NULL DEFAULT 1,
                                AccountId   INTEGER NOT NULL DEFAULT 0,
                                Price       INTEGER NOT NULL DEFAULT 0,
                                AccessCode  TEXT NOT NULL DEFAULT '',
                                GuildId     INTEGER NOT NULL DEFAULT 0,
                                GuildRights INTEGER NOT NULL DEFAULT 0,
                                PRIMARY KEY (MapId, ElementId, Instance));
                            INSERT INTO HousesByCopy (MapId, ElementId, Instance, AccountId, Price, AccessCode, GuildId, GuildRights)
                                SELECT MapId, ElementId, 1, AccountId, Price, AccessCode, GuildId, GuildRights FROM Houses;
                            DROP TABLE Houses;
                            ALTER TABLE HousesByCopy RENAME TO Houses;");
                    }
                    if (!ColumnsOf(connection, "HouseChestCodes").Contains("Instance"))
                    {
                        Rebuild(connection, "HouseChestCodes", @"
                            CREATE TABLE HouseChestCodesByCopy (
                                MapId          INTEGER NOT NULL,
                                ElementId      INTEGER NOT NULL,
                                Instance       INTEGER NOT NULL DEFAULT 1,
                                ChestElementId INTEGER NOT NULL,
                                Code           TEXT NOT NULL DEFAULT '',
                                PRIMARY KEY (MapId, ElementId, Instance, ChestElementId));
                            INSERT INTO HouseChestCodesByCopy (MapId, ElementId, Instance, ChestElementId, Code)
                                SELECT MapId, ElementId, 1, ChestElementId, Code FROM HouseChestCodes;
                            DROP TABLE HouseChestCodes;
                            ALTER TABLE HouseChestCodesByCopy RENAME TO HouseChestCodes;");
                    }

                    using (var index = connection.CreateCommand())
                    {
                        index.CommandText = "CREATE INDEX IF NOT EXISTS HousesByAccount ON Houses (AccountId);";
                        index.ExecuteNonQuery();
                    }
                    _tablesReady = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Houses] The tables could not be created: {ex.Message}");
                }
            }
        }

        private static HashSet<string> ColumnsOf(SqliteConnection connection, string table)
        {
            var columns = new HashSet<string>();
            using var pragma = connection.CreateCommand();
            pragma.CommandText = $"PRAGMA table_info({table});";
            using var reader = pragma.ExecuteReader();
            while (reader.Read()) columns.Add(reader.GetString(1));
            return columns;
        }

        /// <summary>A table made again with its new key, in one transaction, its rows the first copy's.</summary>
        private static void Rebuild(SqliteConnection connection, string table, string sql)
        {
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
            transaction.Commit();
            Console.WriteLine($"[Houses] {table}: every house is now its building's first copy.");
        }

        /// <summary>
        /// Houses kept under a map their door is not kept under any more -- bought from the second
        /// map a door stands on, before a door was one house -- go to their door's map, chest codes
        /// and chests with them. One already there wins: two owners of one copy cannot both stay.
        /// </summary>
        private static void MoveToTheirDoors()
        {
            try
            {
                using var connection = Open();
                var rows = new List<(long MapId, int ElementId)>();
                using (var read = connection.CreateCommand())
                {
                    read.CommandText = "SELECT DISTINCT MapId, ElementId FROM Houses;";
                    using var reader = read.ExecuteReader();
                    while (reader.Read()) rows.Add((reader.GetInt64(0), reader.GetInt32(1)));
                }
                int moved = 0;
                foreach (var (mapId, elementId) in rows)
                {
                    long kept = Houses.CanonicalMapOf(mapId, elementId);
                    if (kept == mapId) continue;
                    using var transaction = connection.BeginTransaction();
                    using var move = connection.CreateCommand();
                    move.Transaction = transaction;
                    move.CommandText = @"
                        UPDATE OR IGNORE Houses SET MapId = $k WHERE MapId = $m AND ElementId = $e;
                        DELETE FROM Houses WHERE MapId = $m AND ElementId = $e;
                        UPDATE OR IGNORE HouseChestCodes SET MapId = $k WHERE MapId = $m AND ElementId = $e;
                        DELETE FROM HouseChestCodes WHERE MapId = $m AND ElementId = $e;
                        UPDATE StorageItems SET Storage = $to || substr(Storage, length($from) + 1)
                            WHERE substr(Storage, 1, length($from)) = $from;
                        UPDATE StorageItems SET Storage = $toCopy || substr(Storage, length($fromCopy) + 1)
                            WHERE substr(Storage, 1, length($fromCopy)) = $fromCopy;";
                    move.Parameters.AddWithValue("$k", kept);
                    move.Parameters.AddWithValue("$m", mapId);
                    move.Parameters.AddWithValue("$e", elementId);
                    move.Parameters.AddWithValue("$from", $"house:{mapId}:{elementId}:");
                    move.Parameters.AddWithValue("$to", $"house:{kept}:{elementId}:");
                    move.Parameters.AddWithValue("$fromCopy", $"house:{mapId}:{elementId}/");
                    move.Parameters.AddWithValue("$toCopy", $"house:{kept}:{elementId}/");
                    move.ExecuteNonQuery();
                    transaction.Commit();
                    moved++;
                }
                if (moved > 0) Console.WriteLine($"[Houses] {moved} house(s) moved to the map their door is kept under.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Houses] Could not move the houses to their doors: {ex.Message}");
            }
        }

        private static SqliteConnection Open()
        {
            var connection = new SqliteConnection(DatabaseManager.WorldConnectionString);
            connection.Open();
            return connection;
        }

        private static long OwnedCount() => Scalar("SELECT COUNT(*) FROM Houses WHERE AccountId > 0;");
        private static long ForSaleCount() => Scalar("SELECT COUNT(*) FROM Houses WHERE AccountId > 0 AND Price > 0;");

        private static long Scalar(string sql)
        {
            try
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                return command.ExecuteScalar() is long n ? n : 0;
            }
            catch
            {
                return 0;
            }
        }

        // ─── Reading ────────────────────────────────────────────────────────────

        /// <summary>This copy of the building behind this door, ownerless when the store has nothing on it.</summary>
        public static House Of(long mapId, int elementId, int instance)
        {
            mapId = Houses.CanonicalMapOf(mapId, elementId);
            EnsureTables();
            try
            {
                using var connection = Open();
                return Of(connection, null, mapId, elementId, instance);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Houses] Could not read the house {mapId}/{elementId}/{instance}: {ex.Message}");
                return new House { MapId = mapId, ElementId = elementId, Instance = instance };
            }
        }

        private static House Of(SqliteConnection connection, SqliteTransaction? transaction, long mapId, int elementId, int instance)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"SELECT {Columns} FROM Houses WHERE MapId = $m AND ElementId = $e AND Instance = $i;";
            command.Parameters.AddWithValue("$m", mapId);
            command.Parameters.AddWithValue("$e", elementId);
            command.Parameters.AddWithValue("$i", instance);
            using var reader = command.ExecuteReader();
            return reader.Read() ? Read(reader) : new House { MapId = mapId, ElementId = elementId, Instance = instance };
        }

        /// <summary>
        /// The copies of a building the street is shown: those with an owner, by instance, and the
        /// first free one if the building has one left -- the copy a newcomer buys.
        /// </summary>
        /// <remarks>
        /// The client builds a street's houses from its jss alone (ehl and fka over jss f9); the
        /// izz that follows a change only replaces a copy it already has (ehl over izz, by
        /// instance). So once the free copy is bought, the next one shows when the street is
        /// loaded again, not before.
        /// </remarks>
        public static List<House> CopiesOf(long mapId, int elementId, int copies)
        {
            mapId = Houses.CanonicalMapOf(mapId, elementId);
            var shown = new List<House>();
            foreach (var house in OwnedOf(mapId, elementId))
            {
                if (house.Instance >= Houses.FirstInstance && house.Instance <= copies) shown.Add(house);
            }
            for (int instance = Houses.FirstInstance; instance <= copies; instance++)
            {
                if (shown.Exists(h => h.Instance == instance)) continue;
                shown.Add(new House { MapId = mapId, ElementId = elementId, Instance = instance });
                shown.Sort((a, b) => a.Instance.CompareTo(b.Instance));
                break;
            }
            return shown;
        }

        /// <summary>The copies of one building that have an owner.</summary>
        private static List<House> OwnedOf(long mapId, int elementId)
        {
            EnsureTables();
            var houses = new List<House>();
            try
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = $"SELECT {Columns} FROM Houses WHERE MapId = $m AND ElementId = $e AND AccountId > 0 ORDER BY Instance;";
                command.Parameters.AddWithValue("$m", mapId);
                command.Parameters.AddWithValue("$e", elementId);
                using var reader = command.ExecuteReader();
                while (reader.Read()) houses.Add(Read(reader));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Houses] Could not read the copies of {mapId}/{elementId}: {ex.Message}");
            }
            return houses;
        }

        /// <summary>Every house with an owner: what the estate agency lists from.</summary>
        public static List<House> AllOwned()
        {
            EnsureTables();
            var houses = new List<House>();
            try
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = $"SELECT {Columns} FROM Houses WHERE AccountId > 0 ORDER BY MapId, ElementId, Instance;";
                using var reader = command.ExecuteReader();
                while (reader.Read()) houses.Add(Read(reader));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Houses] Could not read the owned houses: {ex.Message}");
            }
            return houses;
        }

        /// <summary>The houses shared with a guild.</summary>
        public static List<House> SharedWith(long guildId)
            => guildId <= 0 ? new List<House>() : AllOwned().Where(h => h.GuildId == guildId).ToList();

        /// <summary>Every house this account owns, by door and copy.</summary>
        public static List<House> OwnedBy(long accountId)
        {
            EnsureTables();
            var houses = new List<House>();
            if (accountId <= 0) return houses;
            try
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = $"SELECT {Columns} FROM Houses WHERE AccountId = $a ORDER BY MapId, ElementId, Instance;";
                command.Parameters.AddWithValue("$a", accountId);
                using var reader = command.ExecuteReader();
                while (reader.Read()) houses.Add(Read(reader));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Houses] Could not read the houses of account {accountId}: {ex.Message}");
            }
            return houses;
        }

        /// <summary>The code on one of this house's chests; empty when it has none.</summary>
        public static string ChestCodeOf(long mapId, int elementId, int instance, int chestElementId)
        {
            mapId = Houses.CanonicalMapOf(mapId, elementId);
            EnsureTables();
            try
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT Code FROM HouseChestCodes " +
                                      "WHERE MapId = $m AND ElementId = $e AND Instance = $i AND ChestElementId = $c;";
                command.Parameters.AddWithValue("$m", mapId);
                command.Parameters.AddWithValue("$e", elementId);
                command.Parameters.AddWithValue("$i", instance);
                command.Parameters.AddWithValue("$c", chestElementId);
                return command.ExecuteScalar() as string ?? "";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Houses] Could not read the code of chest {chestElementId}: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// Whether a typed code is the one that was set. Codes are digits the client's keypad
        /// sends as text (khv/khw f1: "13581321", "135813", "1358", "1234"), compared as typed.
        /// </summary>
        public static bool Matches(string expected, string typed)
            => expected.Length > 0 && string.Equals(expected, typed ?? "", StringComparison.Ordinal);

        /// <summary>
        /// A code as it will be kept: at most <see cref="CodeLength"/> characters, digits only.
        /// Null when it is not one. Empty is a valid code and means "none" -- the chest capture
        /// whose Accept went out with nothing typed sends khv with no f1.
        /// </summary>
        public static string? Normalize(string? code)
        {
            code ??= "";
            if (code.Length > CodeLength) return null;
            foreach (char c in code)
            {
                if (c < '0' || c > '9') return null;
            }
            return code;
        }

        // ─── The owner's changes ────────────────────────────────────────────────

        /// <summary>
        /// Puts the house on sale at <paramref name="price"/>, or takes it off with zero. Only
        /// its owner can, and a negative price is refused. False when nothing changed.
        /// </summary>
        public static async Task<bool> SetPriceAsync(long mapId, int elementId, int instance, long ownerAccountId, long price)
        {
            mapId = Houses.CanonicalMapOf(mapId, elementId);
            if (ownerAccountId <= 0 || price < 0) return false;
            EnsureTables();

            var gate = GateOf(mapId, elementId, instance);
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                // On sale it is not the guild's any more: «Mientras tengas tu casa en venta, no
                // puedes compartirla con tu gremio» (1/336), and the sale capture's jjt.
                command.CommandText = "UPDATE Houses SET Price = $p, " +
                                      "GuildId = CASE WHEN $p > 0 THEN 0 ELSE GuildId END, " +
                                      "GuildRights = CASE WHEN $p > 0 THEN 0 ELSE GuildRights END " +
                                      "WHERE MapId = $m AND ElementId = $e AND Instance = $i AND AccountId = $a;";
                command.Parameters.AddWithValue("$p", price);
                command.Parameters.AddWithValue("$m", mapId);
                command.Parameters.AddWithValue("$e", elementId);
                command.Parameters.AddWithValue("$i", instance);
                command.Parameters.AddWithValue("$a", ownerAccountId);
                return command.ExecuteNonQuery() == 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Houses] Could not set the price of {mapId}/{elementId}/{instance}: {ex.Message}");
                return false;
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>The house's access code, set by its owner; empty takes it off. False when not changed.</summary>
        public static async Task<bool> SetAccessCodeAsync(long mapId, int elementId, int instance, long ownerAccountId, string code)
        {
            mapId = Houses.CanonicalMapOf(mapId, elementId);
            string? kept = Normalize(code);
            if (ownerAccountId <= 0 || kept == null) return false;
            EnsureTables();

            var gate = GateOf(mapId, elementId, instance);
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE Houses SET AccessCode = $c " +
                                      "WHERE MapId = $m AND ElementId = $e AND Instance = $i AND AccountId = $a;";
                command.Parameters.AddWithValue("$c", kept);
                command.Parameters.AddWithValue("$m", mapId);
                command.Parameters.AddWithValue("$e", elementId);
                command.Parameters.AddWithValue("$i", instance);
                command.Parameters.AddWithValue("$a", ownerAccountId);
                return command.ExecuteNonQuery() == 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Houses] Could not set the code of {mapId}/{elementId}/{instance}: {ex.Message}");
                return false;
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>A chest's code, set by the house's owner; empty takes it off. False when not changed.</summary>
        public static async Task<bool> SetChestCodeAsync(long mapId, int elementId, int instance, int chestElementId,
                                                         long ownerAccountId, string code)
        {
            mapId = Houses.CanonicalMapOf(mapId, elementId);
            string? kept = Normalize(code);
            if (ownerAccountId <= 0 || kept == null) return false;
            EnsureTables();

            var gate = GateOf(mapId, elementId, instance);
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                using var connection = Open();
                if (Of(connection, null, mapId, elementId, instance).AccountId != ownerAccountId) return false;

                using var command = connection.CreateCommand();
                command.CommandText = kept.Length == 0
                    ? "DELETE FROM HouseChestCodes WHERE MapId = $m AND ElementId = $e AND Instance = $i AND ChestElementId = $ch;"
                    : "INSERT INTO HouseChestCodes (MapId, ElementId, Instance, ChestElementId, Code) VALUES ($m, $e, $i, $ch, $c) " +
                      "ON CONFLICT(MapId, ElementId, Instance, ChestElementId) DO UPDATE SET Code = $c;";
                command.Parameters.AddWithValue("$m", mapId);
                command.Parameters.AddWithValue("$e", elementId);
                command.Parameters.AddWithValue("$i", instance);
                command.Parameters.AddWithValue("$ch", chestElementId);
                if (kept.Length > 0) command.Parameters.AddWithValue("$c", kept);
                command.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Houses] Could not set the code of chest {chestElementId}: {ex.Message}");
                return false;
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// Shares a house with its owner's guild, with these rights, or stops sharing it (guild
        /// zero). Only its owner can, and not while it is on sale: 1/336 «Mientras tengas tu casa
        /// en venta, no puedes compartirla con tu gremio». False when nothing changed.
        /// </summary>
        public static async Task<bool> SetGuildShareAsync(long mapId, int elementId, int instance, long ownerAccountId,
                                                          long guildId, int rights)
        {
            mapId = Houses.CanonicalMapOf(mapId, elementId);
            if (ownerAccountId <= 0 || guildId < 0) return false;
            EnsureTables();

            var gate = GateOf(mapId, elementId, instance);
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE Houses SET GuildId = $g, GuildRights = $r " +
                                      "WHERE MapId = $m AND ElementId = $e AND Instance = $i AND AccountId = $a AND Price = 0;";
                command.Parameters.AddWithValue("$g", guildId);
                command.Parameters.AddWithValue("$r", guildId > 0 ? rights : 0);
                command.Parameters.AddWithValue("$m", mapId);
                command.Parameters.AddWithValue("$e", elementId);
                command.Parameters.AddWithValue("$i", instance);
                command.Parameters.AddWithValue("$a", ownerAccountId);
                return command.ExecuteNonQuery() > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Houses] Could not share the house {mapId}/{elementId}/{instance}: {ex.Message}");
                return false;
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>The houses of these accounts shared with a guild, to stop sharing them when their owner leaves it.</summary>
        public static int StopSharing(long guildId, long accountId)
        {
            EnsureTables();
            try
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE Houses SET GuildId = 0, GuildRights = 0 WHERE GuildId = $g AND AccountId = $a;";
                command.Parameters.AddWithValue("$g", guildId);
                command.Parameters.AddWithValue("$a", accountId);
                return command.ExecuteNonQuery();
            }
            catch
            {
                return 0;
            }
        }

        // ─── Buying ─────────────────────────────────────────────────────────────

        /// <summary>
        /// What this house costs to whoever wants it now: the owner's price when it is on sale,
        /// the model's when it has no owner, zero when it cannot be bought.
        /// </summary>
        public static long AskingPrice(House house, long modelPrice)
            => house.Owned ? (house.ForSale ? house.Price : 0) : Math.Max(0, modelPrice);

        /// <summary>
        /// Buys the house for <paramref name="buyerAccountId"/>, paying from the character's purse
        /// the price the buyer was shown. The seller's bank gets the money and whatever the house's
        /// chests held; the codes go and the house is off sale.
        /// </summary>
        /// <remarks>
        /// Inference throughout: no capture buys a house. What it rests on is the client's own
        /// sentences, which say what happens -- 4/5 "{1} acaba de comprar una de tus casas por
        /// $quantity{0} kamas. El dinero ha sido transferido a tu cuenta bancaria.", 1/264 "Se
        /// ha(n) transferido {0} lote(s) de objetos a tu banco desde los cofres de tu casa.", and
        /// 0/342 "No puedes comprar esta casa porque la cuenta del banco del vendedor está
        /// repleta." -- and on the seller's price being what the buyer pays and nothing else.
        ///
        /// <paramref name="expectedPrice"/> is the price the buyer's window showed: a house whose
        /// price changed in between is not bought at the new one.
        /// </remarks>
        public static async Task<(Purchase? Done, Refusal Why)> BuyAsync(
            long mapId, int elementId, int instance, long modelPrice, long buyerAccountId, long buyerCharacterId,
            long buyerKamas, long expectedPrice)
        {
            mapId = Houses.CanonicalMapOf(mapId, elementId);
            if (buyerAccountId <= 0 || buyerCharacterId <= 0) return (null, Refusal.Failed);
            EnsureTables();

            var gate = GateOf(mapId, elementId, instance);
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                House before;
                long price;
                using (var connection = Open())
                {
                    before = Of(connection, null, mapId, elementId, instance);
                    price = AskingPrice(before, modelPrice);
                    if (price <= 0) return (null, Refusal.NotForSale);
                    if (before.AccountId == buyerAccountId) return (null, Refusal.OwnHouse);
                    if (price != expectedPrice) return (null, Refusal.PriceChanged);
                    if (buyerKamas < price) return (null, Refusal.NotEnoughKamas);

                    using var transaction = connection.BeginTransaction();
                    WriteOwner(connection, transaction, mapId, elementId, instance, buyerAccountId);
                    using (var codes = connection.CreateCommand())
                    {
                        codes.Transaction = transaction;
                        codes.CommandText = "DELETE FROM HouseChestCodes WHERE MapId = $m AND ElementId = $e AND Instance = $i;";
                        codes.Parameters.AddWithValue("$m", mapId);
                        codes.Parameters.AddWithValue("$e", elementId);
                        codes.Parameters.AddWithValue("$i", instance);
                        codes.ExecuteNonQuery();
                    }
                    using (var purse = connection.CreateCommand())
                    {
                        purse.Transaction = transaction;
                        purse.CommandText = "UPDATE Characters SET Kamas = $k WHERE Id = $id;";
                        purse.Parameters.AddWithValue("$k", buyerKamas - price);
                        purse.Parameters.AddWithValue("$id", buyerCharacterId);
                        purse.ExecuteNonQuery();
                    }
                    transaction.Commit();
                }

                // The money, to a seller online or not. A bank that cannot take it undoes the sale:
                // the buyer keeps his kamas and the seller his house.
                if (before.Owned && !await Bank.AddKamasAsync(before.AccountId, price).ConfigureAwait(false))
                {
                    Restore(before, buyerCharacterId, buyerKamas);
                    return (null, Refusal.SellerBankFull);
                }

                int lots = 0;
                var left = StorageStacks.TakeAll(StorageStacks.HouseChestsPrefix(mapId, elementId, instance));
                if (before.Owned)
                {
                    foreach (var item in left)
                    {
                        if (await Bank.AddItemAsync(before.AccountId, item.Gid, item.Quantity, item.Effects).ConfigureAwait(false))
                            lots++;
                    }
                }

                return (new Purchase
                {
                    Price = price,
                    SellerAccountId = before.AccountId,
                    BuyerKamas = buyerKamas - price,
                    LotsToSeller = lots,
                }, Refusal.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Houses] Could not sell {mapId}/{elementId}/{instance} to account {buyerAccountId}: {ex.Message}");
                return (null, Refusal.Failed);
            }
            finally
            {
                gate.Release();
            }
        }

        private static void WriteOwner(SqliteConnection connection, SqliteTransaction transaction,
                                       long mapId, int elementId, int instance, long accountId)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO Houses (MapId, ElementId, Instance, AccountId, Price, AccessCode) VALUES ($m, $e, $i, $a, 0, '') " +
                                  "ON CONFLICT(MapId, ElementId, Instance) DO UPDATE SET AccountId = $a, Price = 0, AccessCode = '', GuildId = 0, GuildRights = 0;";
            command.Parameters.AddWithValue("$m", mapId);
            command.Parameters.AddWithValue("$e", elementId);
            command.Parameters.AddWithValue("$i", instance);
            command.Parameters.AddWithValue("$a", accountId);
            command.ExecuteNonQuery();
        }

        /// <summary>Puts a house and a purse back as they were before a purchase that could not finish.</summary>
        private static void Restore(House before, long buyerCharacterId, long buyerKamas)
        {
            try
            {
                using var connection = Open();
                using var transaction = connection.BeginTransaction();
                using (var house = connection.CreateCommand())
                {
                    house.Transaction = transaction;
                    house.CommandText = before.Owned
                        ? "UPDATE Houses SET AccountId = $a, Price = $p, AccessCode = $c WHERE MapId = $m AND ElementId = $e AND Instance = $i;"
                        : "DELETE FROM Houses WHERE MapId = $m AND ElementId = $e AND Instance = $i;";
                    house.Parameters.AddWithValue("$m", before.MapId);
                    house.Parameters.AddWithValue("$e", before.ElementId);
                    house.Parameters.AddWithValue("$i", before.Instance);
                    if (before.Owned)
                    {
                        house.Parameters.AddWithValue("$a", before.AccountId);
                        house.Parameters.AddWithValue("$p", before.Price);
                        house.Parameters.AddWithValue("$c", before.AccessCode);
                    }
                    house.ExecuteNonQuery();
                }
                using (var purse = connection.CreateCommand())
                {
                    purse.Transaction = transaction;
                    purse.CommandText = "UPDATE Characters SET Kamas = $k WHERE Id = $id;";
                    purse.Parameters.AddWithValue("$k", buyerKamas);
                    purse.Parameters.AddWithValue("$id", buyerCharacterId);
                    purse.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Houses] Could not undo the purchase of {before.MapId}/{before.ElementId}/{before.Instance}: {ex.Message}");
            }
        }

        /// <summary>Forgets a building entirely, every copy: no owner, no codes, nothing in its chests. For tests and tools.</summary>
        internal static void Forget(long mapId, int elementId)
        {
            EnsureTables();
            var instances = new HashSet<int> { Houses.FirstInstance };
            using var connection = Open();
            using (var read = connection.CreateCommand())
            {
                read.CommandText = "SELECT Instance FROM Houses WHERE MapId = $m AND ElementId = $e;";
                read.Parameters.AddWithValue("$m", mapId);
                read.Parameters.AddWithValue("$e", elementId);
                using var reader = read.ExecuteReader();
                while (reader.Read()) instances.Add(reader.GetInt32(0));
            }
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Houses WHERE MapId = $m AND ElementId = $e; " +
                                  "DELETE FROM HouseChestCodes WHERE MapId = $m AND ElementId = $e;";
            command.Parameters.AddWithValue("$m", mapId);
            command.Parameters.AddWithValue("$e", elementId);
            command.ExecuteNonQuery();
            foreach (int instance in instances)
                StorageStacks.TakeAll(StorageStacks.HouseChestsPrefix(mapId, elementId, instance));
        }
    }
}

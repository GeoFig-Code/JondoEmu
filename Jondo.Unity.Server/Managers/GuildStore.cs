using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// Los gremios y quién está en cada uno, guardados en world.db.
    ///
    /// Es la base sobre la que se apoya todo lo demás del gremio -la ventana, la tienda, el
    /// cofre y, más adelante, las raids-, así que va aquí y no en el aire: un gremio creado
    /// sobrevive al reinicio del servidor.
    ///
    /// Lo que sabemos de una captura y lo que no: el nombre, el emblema, el nivel, la fecha de
    /// fundación y quién pertenece están medidos en las 12 capturas de Gremio/. La experiencia,
    /// los kamas de gremio, los guildatones y los permisos por rango salen en las tramas pero su
    /// escala completa no se ha reconstruido; se guardan los que se ven y el resto arranca a cero.
    /// </summary>
    public static class GuildStore
    {
        /// <summary>Un gremio: lo que lo identifica y lo que el cliente pinta de él.</summary>
        public sealed class Guild
        {
            public long Id { get; init; }
            public string Name { get; init; } = "";
            public int Level { get; set; } = 1;
            public long Experience { get; set; }

            /// <summary>
            /// El emblema, los cuatro números del <c>jjg</c> de creación: el símbolo, el índice
            /// de color del símbolo, el color de fondo (RGB) y el color del símbolo (RGB).
            /// </summary>
            public int EmblemSymbol { get; init; }
            public int EmblemSymbolColor { get; init; }
            public int EmblemBackground { get; init; }
            public int EmblemSymbolRgb { get; init; }

            /// <summary>La fecha de fundación, en ISO-8601 con Z, tal y como viaja en el <c>jhh</c>.</summary>
            public string FoundedUtc { get; init; } = "";

            /// <summary>Los kamas de gremio que quedan por gastar (contribuciones menos compras).</summary>
            public long GuildKamas { get; set; }
        }

        /// <summary>Un miembro: su personaje, su rango y cuándo entró.</summary>
        public sealed class Member
        {
            public long CharacterId { get; init; }
            public long GuildId { get; init; }
            public int Rank { get; set; } = 1;
            public long JoinedUtcMs { get; init; }
            public long Experience { get; set; }
        }

        /// <summary>
        /// El nivel máximo de miembros por nivel de gremio. Medido en dos puntos -un gremio de
        /// nivel 1 dice 50 (jhh f9=50) y uno de nivel 7 dice 410-; entre ellos es una inferencia
        /// y va dicha como tal. El resto de la curva no está medido, así que fuera de esos dos
        /// niveles se devuelve el más cercano conocido.
        /// </summary>
        public static int MaxMembers(int level) => level <= 1 ? 50 : level >= 7 ? 410 : 50 + (level - 1) * 60;

        public static void EnsureTables(SqliteConnection world)
        {
            var create = world.CreateCommand();
            create.CommandText = @"
                CREATE TABLE IF NOT EXISTS Guilds (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Level INTEGER NOT NULL DEFAULT 1,
                    Experience INTEGER NOT NULL DEFAULT 0,
                    EmblemSymbol INTEGER NOT NULL DEFAULT 0,
                    EmblemSymbolColor INTEGER NOT NULL DEFAULT 0,
                    EmblemBackground INTEGER NOT NULL DEFAULT 0,
                    EmblemSymbolRgb INTEGER NOT NULL DEFAULT 0,
                    FoundedUtc TEXT NOT NULL DEFAULT '',
                    GuildKamas INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS GuildMembers (
                    CharacterId INTEGER PRIMARY KEY,
                    GuildId INTEGER NOT NULL,
                    Rank INTEGER NOT NULL DEFAULT 1,
                    JoinedUtcMs INTEGER NOT NULL DEFAULT 0,
                    Experience INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS IdxGuildMembersGuild ON GuildMembers (GuildId);
                CREATE TABLE IF NOT EXISTS GuildApplications (
                    GuildId INTEGER NOT NULL,
                    CharacterId INTEGER NOT NULL,
                    Message TEXT NOT NULL DEFAULT '',
                    WhenMs INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (GuildId, CharacterId)
                );
                CREATE TABLE IF NOT EXISTS GuildOracles (
                    GuildId INTEGER NOT NULL,
                    Oracle INTEGER NOT NULL,
                    DeadlineUtc TEXT NOT NULL DEFAULT '',
                    PRIMARY KEY (GuildId, Oracle)
                );
                CREATE TABLE IF NOT EXISTS GuildOwnedRaids (
                    GuildId INTEGER NOT NULL,
                    RaidId INTEGER NOT NULL,
                    BoughtUtcMs INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (GuildId, RaidId)
                );
                CREATE TABLE IF NOT EXISTS GuildContributions (
                    CharacterId INTEGER NOT NULL,
                    Week TEXT NOT NULL,
                    Done INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (CharacterId, Week)
                );";
            create.ExecuteNonQuery();
        }

        /// <summary>A connection string a test can point at a temp database; null uses world.db.</summary>
        internal static string ConnectionStringOverride { get; set; }

        private static SqliteConnection Open()
        {
            var conexion = new SqliteConnection(ConnectionStringOverride ?? DatabaseManager.WorldConnectionString);
            conexion.Open();
            EnsureTables(conexion);
            return conexion;
        }

        /// <summary>Crea el gremio con su primer miembro -el fundador- de rango 1, y lo devuelve.</summary>
        public static Guild Create(long founderCharacterId, string name, int symbol, int symbolColor,
                                   int background, int symbolRgb)
        {
            using var conexion = Open();
            long ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string founded = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");

            var insertGuild = conexion.CreateCommand();
            insertGuild.CommandText = @"
                INSERT INTO Guilds (Name, Level, EmblemSymbol, EmblemSymbolColor, EmblemBackground, EmblemSymbolRgb, FoundedUtc)
                VALUES ($name, 1, $sym, $symc, $bg, $symrgb, $founded);
                SELECT last_insert_rowid();";
            insertGuild.Parameters.AddWithValue("$name", name);
            insertGuild.Parameters.AddWithValue("$sym", symbol);
            insertGuild.Parameters.AddWithValue("$symc", symbolColor);
            insertGuild.Parameters.AddWithValue("$bg", background);
            insertGuild.Parameters.AddWithValue("$symrgb", symbolRgb);
            insertGuild.Parameters.AddWithValue("$founded", founded);
            long id = (long)insertGuild.ExecuteScalar();

            var insertMember = conexion.CreateCommand();
            insertMember.CommandText = @"
                INSERT OR REPLACE INTO GuildMembers (CharacterId, GuildId, Rank, JoinedUtcMs)
                VALUES ($c, $g, 1, $ms);";
            insertMember.Parameters.AddWithValue("$c", founderCharacterId);
            insertMember.Parameters.AddWithValue("$g", id);
            insertMember.Parameters.AddWithValue("$ms", ms);
            insertMember.ExecuteNonQuery();

            return new Guild
            {
                Id = id, Name = name, Level = 1,
                EmblemSymbol = symbol, EmblemSymbolColor = symbolColor,
                EmblemBackground = background, EmblemSymbolRgb = symbolRgb,
                FoundedUtc = founded,
            };
        }

        /// <summary>El gremio de un personaje, o null si no tiene.</summary>
        public static Guild GuildOf(long characterId)
        {
            using var conexion = Open();
            var query = conexion.CreateCommand();
            query.CommandText = @"
                SELECT g.Id, g.Name, g.Level, g.Experience, g.EmblemSymbol, g.EmblemSymbolColor,
                       g.EmblemBackground, g.EmblemSymbolRgb, g.FoundedUtc, g.GuildKamas
                FROM GuildMembers m JOIN Guilds g ON g.Id = m.GuildId
                WHERE m.CharacterId = $c;";
            query.Parameters.AddWithValue("$c", characterId);
            using var lector = query.ExecuteReader();
            if (!lector.Read()) return null;
            return new Guild
            {
                Id = lector.GetInt64(0), Name = lector.GetString(1), Level = lector.GetInt32(2),
                Experience = lector.GetInt64(3), EmblemSymbol = lector.GetInt32(4),
                EmblemSymbolColor = lector.GetInt32(5), EmblemBackground = lector.GetInt32(6),
                EmblemSymbolRgb = lector.GetInt32(7), FoundedUtc = lector.GetString(8),
                GuildKamas = lector.GetInt64(9),
            };
        }

        /// <summary>Un gremio por su nombre, sin distinguir mayúsculas. Null si no hay ninguno así.</summary>
        public static Guild ByName(string name)
        {
            using var conexion = Open();
            var query = conexion.CreateCommand();
            query.CommandText = @"
                SELECT Id, Name, Level, Experience, EmblemSymbol, EmblemSymbolColor,
                       EmblemBackground, EmblemSymbolRgb, FoundedUtc, GuildKamas
                FROM Guilds WHERE Name = $n COLLATE NOCASE LIMIT 1;";
            query.Parameters.AddWithValue("$n", name ?? "");
            using var lector = query.ExecuteReader();
            if (!lector.Read()) return null;
            return new Guild
            {
                Id = lector.GetInt64(0), Name = lector.GetString(1), Level = lector.GetInt32(2),
                Experience = lector.GetInt64(3), EmblemSymbol = lector.GetInt32(4),
                EmblemSymbolColor = lector.GetInt32(5), EmblemBackground = lector.GetInt32(6),
                EmblemSymbolRgb = lector.GetInt32(7), FoundedUtc = lector.GetString(8),
                GuildKamas = lector.GetInt64(9),
            };
        }

        /// <summary>El puesto de un personaje en su gremio, o cero si no está en ninguno.</summary>
        public static int RankOf(long characterId)
        {
            using var conexion = Open();
            var query = conexion.CreateCommand();
            query.CommandText = "SELECT Rank FROM GuildMembers WHERE CharacterId = $c;";
            query.Parameters.AddWithValue("$c", characterId);
            var value = query.ExecuteScalar();
            return value == null || value is DBNull ? 0 : Convert.ToInt32(value);
        }

        /// <summary>Los miembros de un gremio, por su personaje y su rango.</summary>
        public static List<Member> Members(long guildId)
        {
            using var conexion = Open();
            var query = conexion.CreateCommand();
            query.CommandText = @"
                SELECT CharacterId, GuildId, Rank, JoinedUtcMs, Experience
                FROM GuildMembers WHERE GuildId = $g ORDER BY Rank, CharacterId;";
            query.Parameters.AddWithValue("$g", guildId);
            using var lector = query.ExecuteReader();
            var fuera = new List<Member>();
            while (lector.Read())
            {
                fuera.Add(new Member
                {
                    CharacterId = lector.GetInt64(0), GuildId = lector.GetInt64(1),
                    Rank = lector.GetInt32(2), JoinedUtcMs = lector.GetInt64(3),
                    Experience = lector.GetInt64(4),
                });
            }
            return fuera;
        }

        /// <summary>
        /// Mete a un personaje en un gremio con el rango que se le dé. El rango 4 es el de los
        /// que acaban de entrar: es el que lleva en la lista de miembros el que entró por
        /// candidatura en la captura de crear «Jondo».
        /// </summary>
        public const int RankNewcomer = 4;

        public static Member Join(long characterId, long guildId, int rank = RankNewcomer)
        {
            using var conexion = Open();
            long ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var insert = conexion.CreateCommand();
            insert.CommandText = @"
                INSERT OR REPLACE INTO GuildMembers (CharacterId, GuildId, Rank, JoinedUtcMs)
                VALUES ($c, $g, $r, $ms);
                DELETE FROM GuildApplications WHERE CharacterId = $c;";
            insert.Parameters.AddWithValue("$c", characterId);
            insert.Parameters.AddWithValue("$g", guildId);
            insert.Parameters.AddWithValue("$r", rank);
            insert.Parameters.AddWithValue("$ms", ms);
            insert.ExecuteNonQuery();
            return new Member { CharacterId = characterId, GuildId = guildId, Rank = rank, JoinedUtcMs = ms };
        }

        // ─── Candidaturas ───────────────────────────────────────────────────────

        /// <summary>Una candidatura: quién la manda, a qué gremio, con qué texto y cuándo.</summary>
        public sealed class Application
        {
            public long GuildId { get; init; }
            public long CharacterId { get; init; }
            public string Message { get; init; } = "";
            public long WhenMs { get; init; }
        }

        public static Application Apply(long characterId, long guildId, string message)
        {
            using var conexion = Open();
            long ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var insert = conexion.CreateCommand();
            insert.CommandText = @"
                INSERT OR REPLACE INTO GuildApplications (GuildId, CharacterId, Message, WhenMs)
                VALUES ($g, $c, $m, $ms);";
            insert.Parameters.AddWithValue("$g", guildId);
            insert.Parameters.AddWithValue("$c", characterId);
            insert.Parameters.AddWithValue("$m", message ?? "");
            insert.Parameters.AddWithValue("$ms", ms);
            insert.ExecuteNonQuery();
            return new Application { GuildId = guildId, CharacterId = characterId, Message = message ?? "", WhenMs = ms };
        }

        public static List<Application> Applications(long guildId)
        {
            using var conexion = Open();
            var query = conexion.CreateCommand();
            query.CommandText = @"
                SELECT GuildId, CharacterId, Message, WhenMs FROM GuildApplications
                WHERE GuildId = $g ORDER BY WhenMs;";
            query.Parameters.AddWithValue("$g", guildId);
            using var lector = query.ExecuteReader();
            var fuera = new List<Application>();
            while (lector.Read())
            {
                fuera.Add(new Application
                {
                    GuildId = lector.GetInt64(0), CharacterId = lector.GetInt64(1),
                    Message = lector.GetString(2), WhenMs = lector.GetInt64(3),
                });
            }
            return fuera;
        }

        public static Application ApplicationOf(long guildId, long characterId)
        {
            foreach (var one in Applications(guildId))
            {
                if (one.CharacterId == characterId) return one;
            }
            return null;
        }

        public static void DropApplication(long guildId, long characterId)
        {
            using var conexion = Open();
            var borra = conexion.CreateCommand();
            borra.CommandText = "DELETE FROM GuildApplications WHERE GuildId = $g AND CharacterId = $c;";
            borra.Parameters.AddWithValue("$g", guildId);
            borra.Parameters.AddWithValue("$c", characterId);
            borra.ExecuteNonQuery();
        }

        // ─── Kamas de gremio, contribuciones y tienda ───────────────────────────

        /// <summary>
        /// Lo que una contribución mueve: 10.000 kamas del personaje por 10 de gremio, y cinco
        /// como mucho por semana. Medido en «contribuir en el gremio»: el jle dice 10.000 y los
        /// kamas de gremio suben de 10 a 20; el contador de las que quedan bajó de 4 a 3, o sea
        /// que la quinta era la última.
        /// </summary>
        public const int ContributionKamas = 10000;
        public const int ContributionGuildKamas = 10;
        public const int ContributionsPerWeek = 5;

        /// <summary>El martes en que empieza la semana, que es cuando el juego reinicia lo semanal.</summary>
        public static string WeekOf(DateTimeOffset when)
        {
            int back = ((int)when.UtcDateTime.DayOfWeek - (int)DayOfWeek.Tuesday + 7) % 7;
            return when.UtcDateTime.Date.AddDays(-back).ToString("yyyy-MM-dd");
        }

        /// <summary>Cuántas contribuciones le quedan esta semana a un personaje.</summary>
        public static int ContributionsLeft(long characterId)
        {
            using var conexion = Open();
            var query = conexion.CreateCommand();
            query.CommandText = "SELECT Done FROM GuildContributions WHERE CharacterId = $c AND Week = $w;";
            query.Parameters.AddWithValue("$c", characterId);
            query.Parameters.AddWithValue("$w", WeekOf(DateTimeOffset.UtcNow));
            var value = query.ExecuteScalar();
            int done = value == null || value is DBNull ? 0 : Convert.ToInt32(value);
            return Math.Max(0, ContributionsPerWeek - done);
        }

        /// <summary>
        /// Apunta una contribución y le suma al gremio sus kamas. Devuelve cuántas le quedan al
        /// personaje esta semana, o menos uno si ya no le quedaba ninguna.
        /// </summary>
        public static int Contribute(long characterId, long guildId)
        {
            if (ContributionsLeft(characterId) <= 0) return -1;
            using var conexion = Open();
            var apunta = conexion.CreateCommand();
            apunta.CommandText = @"
                INSERT INTO GuildContributions (CharacterId, Week, Done) VALUES ($c, $w, 1)
                ON CONFLICT (CharacterId, Week) DO UPDATE SET Done = Done + 1;
                UPDATE Guilds SET GuildKamas = GuildKamas + $k WHERE Id = $g;";
            apunta.Parameters.AddWithValue("$c", characterId);
            apunta.Parameters.AddWithValue("$w", WeekOf(DateTimeOffset.UtcNow));
            apunta.Parameters.AddWithValue("$k", ContributionGuildKamas);
            apunta.Parameters.AddWithValue("$g", guildId);
            apunta.ExecuteNonQuery();
            return ContributionsLeft(characterId);
        }

        /// <summary>Gasta kamas de gremio. Falso -y no gasta nada- cuando no llegan.</summary>
        public static bool SpendGuildKamas(long guildId, long amount)
        {
            using var conexion = Open();
            var gasta = conexion.CreateCommand();
            gasta.CommandText = "UPDATE Guilds SET GuildKamas = GuildKamas - $k WHERE Id = $g AND GuildKamas >= $k;";
            gasta.Parameters.AddWithValue("$k", amount);
            gasta.Parameters.AddWithValue("$g", guildId);
            return gasta.ExecuteNonQuery() > 0;
        }

        /// <summary>Apunta un oráculo comprado, con el plazo que tienen los miembros para activarlo.</summary>
        public static void BuyOracle(long guildId, int oracle, DateTimeOffset deadline)
        {
            using var conexion = Open();
            var insert = conexion.CreateCommand();
            insert.CommandText = @"
                INSERT OR REPLACE INTO GuildOracles (GuildId, Oracle, DeadlineUtc)
                VALUES ($g, $o, $d);";
            insert.Parameters.AddWithValue("$g", guildId);
            insert.Parameters.AddWithValue("$o", oracle);
            insert.Parameters.AddWithValue("$d", deadline.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"));
            insert.ExecuteNonQuery();
        }

        /// <summary>El plazo de un oráculo comprado, o null si el gremio no lo tiene.</summary>
        public static string OracleDeadline(long guildId, int oracle)
        {
            using var conexion = Open();
            var query = conexion.CreateCommand();
            query.CommandText = "SELECT DeadlineUtc FROM GuildOracles WHERE GuildId = $g AND Oracle = $o;";
            query.Parameters.AddWithValue("$g", guildId);
            query.Parameters.AddWithValue("$o", oracle);
            return query.ExecuteScalar() as string;
        }

        // ─── Las raids compradas ────────────────────────────────────────────────

        /// <summary>Apunta una raid comprada por el gremio, a la espera de lanzarla.</summary>
        public static void BuyRaid(long guildId, int raidId)
        {
            using var conexion = Open();
            var insert = conexion.CreateCommand();
            insert.CommandText = @"
                INSERT OR REPLACE INTO GuildOwnedRaids (GuildId, RaidId, BoughtUtcMs)
                VALUES ($g, $r, $ms);";
            insert.Parameters.AddWithValue("$g", guildId);
            insert.Parameters.AddWithValue("$r", raidId);
            insert.Parameters.AddWithValue("$ms", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            insert.ExecuteNonQuery();
        }

        public static bool OwnsRaid(long guildId, int raidId)
        {
            using var conexion = Open();
            var query = conexion.CreateCommand();
            query.CommandText = "SELECT 1 FROM GuildOwnedRaids WHERE GuildId = $g AND RaidId = $r;";
            query.Parameters.AddWithValue("$g", guildId);
            query.Parameters.AddWithValue("$r", raidId);
            return query.ExecuteScalar() != null;
        }

        /// <summary>Las raids que el gremio tiene compradas y sin gastar.</summary>
        public static List<int> OwnedRaids(long guildId)
        {
            using var conexion = Open();
            var query = conexion.CreateCommand();
            query.CommandText = "SELECT RaidId FROM GuildOwnedRaids WHERE GuildId = $g ORDER BY RaidId;";
            query.Parameters.AddWithValue("$g", guildId);
            using var lector = query.ExecuteReader();
            var fuera = new List<int>();
            while (lector.Read()) fuera.Add(lector.GetInt32(0));
            return fuera;
        }

        /// <summary>Se gasta al lanzarla: una raid comprada es un uso, no una llave permanente.</summary>
        public static void DropRaid(long guildId, int raidId)
        {
            using var conexion = Open();
            var borra = conexion.CreateCommand();
            borra.CommandText = "DELETE FROM GuildOwnedRaids WHERE GuildId = $g AND RaidId = $r;";
            borra.Parameters.AddWithValue("$g", guildId);
            borra.Parameters.AddWithValue("$r", raidId);
            borra.ExecuteNonQuery();
        }

        /// <summary>Saca a un personaje de su gremio. Devuelve el gremio que dejó, o null si no tenía.</summary>
        public static Guild Leave(long characterId)
        {
            var guild = GuildOf(characterId);
            if (guild == null) return null;
            using var conexion = Open();
            var borra = conexion.CreateCommand();
            borra.CommandText = "DELETE FROM GuildMembers WHERE CharacterId = $c;";
            borra.Parameters.AddWithValue("$c", characterId);
            borra.ExecuteNonQuery();
            return guild;
        }
    }
}

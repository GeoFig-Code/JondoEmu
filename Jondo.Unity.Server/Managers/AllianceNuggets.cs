using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// An alliance's nuggets: what its members' recycling at their tax collectors earns it, held in
    /// its prisms, until someone with the right hands it out (see <see cref="Handlers.RecycleHandler"/>).
    /// </summary>
    /// <remarks>
    /// The client says where they go: «Las pepitas se reparten de forma proporcional entre el
    /// conjunto de los prismas de la alianza en función del nivel de las zonas» (Translations
    /// 1096954), and «Al final de una guerra entre alianzas, las pepitas retenidas en el prisma del
    /// territorio se reparten entre el vencedor y del dominante» (997824). So the alliance's share
    /// is kept per prism, by its zone's level; an alliance with no prism keeps it loose. The pot the
    /// window shows (mec) is the lot: the loose nuggets and every prism's.
    /// Kept in world.db, in tables created at first use:
    /// <code>
    ///   AllianceNuggetPots (AllianceId, Pot)
    ///   PrismNuggets (PrismId, AllianceId, Nuggets)
    /// </code>
    /// </remarks>
    public static class AllianceNuggets
    {
        private static readonly object _gate = new object();
        private static volatile bool _tableReady;

        /// <summary>The nuggets in the alliance's pot: the loose ones and those its prisms hold.</summary>
        public static long PotOf(long allianceId)
        {
            if (allianceId <= 0) return 0;
            return LooseOf(allianceId) + Scalar("SELECT COALESCE(SUM(Nuggets), 0) FROM PrismNuggets WHERE AllianceId = $a;",
                                                ("$a", allianceId));
        }

        /// <summary>The nuggets a prism holds.</summary>
        public static long RetainedIn(long prismId)
            => prismId <= 0 ? 0 : Scalar("SELECT Nuggets FROM PrismNuggets WHERE PrismId = $p;", ("$p", prismId));

        /// <summary>Puts nuggets in the pot, loose, and returns what the pot holds now.</summary>
        public static long Add(long allianceId, long amount)
        {
            if (allianceId <= 0 || amount <= 0) return PotOf(allianceId);
            lock (_gate)
            {
                Write("INSERT INTO AllianceNuggetPots (AllianceId, Pot) VALUES ($a, $n) " +
                      "ON CONFLICT(AllianceId) DO UPDATE SET Pot = Pot + excluded.Pot;", ("$a", allianceId), ("$n", amount));
                return PotOf(allianceId);
            }
        }

        /// <summary>
        /// The alliance's share of a recycling: across its prisms in proportion to their zones'
        /// levels -- the rounding's leftover to the highest --, or loose when it holds none.
        /// <paramref name="prisms"/> are its prisms, with their zones' levels.
        /// </summary>
        public static void Collect(long allianceId, long amount, IReadOnlyCollection<(long PrismId, int Level)> prisms)
        {
            if (allianceId <= 0 || amount <= 0) return;
            if (prisms == null || prisms.Count == 0)
            {
                Add(allianceId, amount);
                return;
            }
            long weights = prisms.Sum(p => (long)Math.Max(1, p.Level));
            var shares = prisms.Select(p => (p.PrismId, Share: amount * Math.Max(1, p.Level) / weights)).ToList();
            long leftover = amount - shares.Sum(s => s.Share);
            long highest = prisms.OrderByDescending(p => p.Level).ThenBy(p => p.PrismId).First().PrismId;
            lock (_gate)
            {
                foreach (var (prismId, share) in shares)
                {
                    long put = share + (prismId == highest ? leftover : 0);
                    if (put <= 0) continue;
                    Write("INSERT INTO PrismNuggets (PrismId, AllianceId, Nuggets) VALUES ($p, $a, $n) " +
                          "ON CONFLICT(PrismId) DO UPDATE SET Nuggets = Nuggets + excluded.Nuggets, AllianceId = excluded.AllianceId;",
                          ("$p", prismId), ("$a", allianceId), ("$n", put));
                }
            }
        }

        /// <summary>Takes nuggets out of the pot, loose ones first; false, and nothing taken, when it holds fewer.</summary>
        public static bool TryTake(long allianceId, long amount)
        {
            if (allianceId <= 0 || amount <= 0) return false;
            lock (_gate)
            {
                if (PotOf(allianceId) < amount) return false;
                long left = amount;
                long loose = Math.Min(left, LooseOf(allianceId));
                if (loose > 0)
                {
                    Write("UPDATE AllianceNuggetPots SET Pot = Pot - $n WHERE AllianceId = $a;", ("$a", allianceId), ("$n", loose));
                    left -= loose;
                }
                foreach (var (prismId, held) in HeldBy(allianceId))
                {
                    if (left <= 0) break;
                    long take = Math.Min(left, held);
                    Write("UPDATE PrismNuggets SET Nuggets = Nuggets - $n WHERE PrismId = $p;", ("$p", prismId), ("$n", take));
                    left -= take;
                }
                return true;
            }
        }

        /// <summary>What a prism held, taken out of it: the war over its zone shares it out, or its fall loses it.</summary>
        public static long TakeRetained(long prismId)
        {
            lock (_gate)
            {
                long held = RetainedIn(prismId);
                Write("DELETE FROM PrismNuggets WHERE PrismId = $p;", ("$p", prismId));
                return held;
            }
        }

        /// <summary>
        /// A prism's nuggets at the end of its zone's war: half to the winner, half to the dominant --
        /// the alliance that dominated longest of the others --, every one to the winner when nobody
        /// else was there. INFERRED: the halves; the client only says they are shared between the two.
        /// </summary>
        public static (long ToWinner, long ToDominant) ShareOutAfterWar(long prismId, long winnerId, long dominantId)
        {
            long held = TakeRetained(prismId);
            if (held <= 0) return (0, 0);
            if (dominantId <= 0 || dominantId == winnerId)
            {
                Add(winnerId, held);
                return (held, 0);
            }
            long toDominant = held / 2;
            Add(winnerId, held - toDominant);
            Add(dominantId, toDominant);
            return (held - toDominant, toDominant);
        }

        private static long LooseOf(long allianceId)
            => Scalar("SELECT Pot FROM AllianceNuggetPots WHERE AllianceId = $a;", ("$a", allianceId));

        private static List<(long PrismId, long Held)> HeldBy(long allianceId)
        {
            var held = new List<(long, long)>();
            try
            {
                EnsureTables();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT PrismId, Nuggets FROM PrismNuggets WHERE AllianceId = $a AND Nuggets > 0 ORDER BY PrismId;";
                command.Parameters.AddWithValue("$a", allianceId);
                using var reader = command.ExecuteReader();
                while (reader.Read()) held.Add((reader.GetInt64(0), reader.GetInt64(1)));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Nuggets] Could not read the prisms of alliance {allianceId}: {ex.Message}");
            }
            return held;
        }

        private static long Scalar(string sql, params (string Name, long Value)[] parameters)
        {
            try
            {
                EnsureTables();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
                return command.ExecuteScalar() is long value2 ? value2 : 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Nuggets] Could not read the nuggets: {ex.Message}");
                return 0;
            }
        }

        private static void Write(string sql, params (string Name, long Value)[] parameters)
        {
            try
            {
                EnsureTables();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Nuggets] Could not change the nuggets: {ex.Message}");
            }
        }

        private static void EnsureTables()
        {
            if (_tableReady) return;
            lock (_gate)
            {
                if (_tableReady) return;
                using var connection = Open();
                using var create = connection.CreateCommand();
                create.CommandText = @"
                    CREATE TABLE IF NOT EXISTS AllianceNuggetPots (
                        AllianceId INTEGER PRIMARY KEY,
                        Pot        INTEGER NOT NULL);
                    CREATE TABLE IF NOT EXISTS PrismNuggets (
                        PrismId    INTEGER PRIMARY KEY,
                        AllianceId INTEGER NOT NULL,
                        Nuggets    INTEGER NOT NULL);";
                create.ExecuteNonQuery();
                _tableReady = true;
            }
        }

        private static SqliteConnection Open()
        {
            var connection = new SqliteConnection(DatabaseManager.WorldConnectionString);
            connection.Open();
            return connection;
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The alterations a character has: which, since when, and how long they have left.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The real server lists them all (lzp) when the client asks on entering the world (lzh) and again
    /// at the end of every fight, with the counts brought down; it sets one with lzs and takes one
    /// away with lzo and «Has perdido la alteración» (info 1665). All of that is measured, in the
    /// Trool fair's potion and its larva race, the guild's oracle and the gladiators' fight.
    /// </para>
    /// <para>
    /// How it lasts is the kind the wire carries in field 4: 1 until taken away, 2 until a date, 3 so
    /// many fights, 4 so many victories. Field 5 is the date for the second and the count for the other
    /// two. The kind comes from the alteration's own effects (<see cref="Start"/>).
    /// </para>
    /// <para>
    /// Kept in world.db, in a table of its own created at first use -- the base CI tests against has
    /// never heard of it:
    /// </para>
    /// <code>
    ///   CharacterAlterations (CharacterId, AlterationId, FromMs, Kind, LeftCount, Total, UntilMs)
    /// </code>
    /// </remarks>
    public static class Alterations
    {
        /// <summary>How an alteration lasts, as field 4 of its entry says.</summary>
        public enum Kind
        {
            /// <summary>Until something takes it away: the orbalantir's spells, a quest's mark.</summary>
            Lasting = 1,

            /// <summary>Until a date: the oracle's two hours, the gladiators' ten minutes.</summary>
            Timed = 2,

            /// <summary>So many fights: the Trool fair's potion, a candy.</summary>
            Fights = 3,

            /// <summary>So many fights won.</summary>
            Victories = 4,
        }

        /// <summary>One alteration a character has.</summary>
        public sealed class Active
        {
            public int Id { get; init; }

            /// <summary>When it was set, in unix milliseconds: field 1.</summary>
            public long FromMs { get; init; }

            public Kind Kind { get; init; }

            /// <summary>The fights or victories still to go.</summary>
            public int Left { get; set; }

            /// <summary>The fights or victories it started with.</summary>
            public int Total { get; init; }

            /// <summary>When a timed one ends, in unix milliseconds.</summary>
            public long UntilMs { get; init; }

            public Active Copy() => new()
            {
                Id = Id, FromMs = FromMs, Kind = Kind, Left = Left, Total = Total, UntilMs = UntilMs,
            };
        }

        private static readonly ConcurrentDictionary<long, List<Active>> _known = new();
        private static readonly object _tableLock = new();
        private static volatile bool _tableReady;

        /// <summary>
        /// What an alteration starts as. A given end wins -- the raid's fragments last as long as the
        /// raid --; otherwise its effects say: 811 its fights, 817 its victories, 2833 its minutes, which
        /// end on the minute, as the captured oracle set at 01:13:12 ends at 03:13:00.
        /// </summary>
        public static Active Start(AlterationCatalogue.Definition definition, long nowMs, long untilMs = 0)
        {
            if (untilMs > 0)
                return new Active { Id = definition.Id, FromMs = nowMs, Kind = Kind.Timed, UntilMs = untilMs };

            foreach (var effect in definition.Effects)
            {
                int count = CountOf(effect);
                switch (effect.Effect)
                {
                    case AlterationCatalogue.FightsLeft:
                        return new Active { Id = definition.Id, FromMs = nowMs, Kind = Kind.Fights, Left = count, Total = count };
                    case AlterationCatalogue.VictoriesLeft:
                        return new Active { Id = definition.Id, FromMs = nowMs, Kind = Kind.Victories, Left = count, Total = count };
                    case AlterationCatalogue.Expiry:
                        long until = nowMs + count * 60_000L;
                        return new Active
                        {
                            Id = definition.Id, FromMs = nowMs, Kind = Kind.Timed, UntilMs = until - until % 60_000L,
                        };
                }
            }
            return new Active { Id = definition.Id, FromMs = nowMs, Kind = Kind.Lasting };
        }

        /// <summary>The number an effect carries, wherever the data put it.</summary>
        internal static int CountOf(Equipment.ItemEffect effect)
            => (int)Math.Max(effect.Value, Math.Max(effect.DiceNum, effect.DiceSide));

        /// <summary>
        /// One fight over: the fights count down, and the victories if it was won. Returns the ones
        /// that reached zero; they stay in the list, which is how the real lzp shows them right after
        /// the fight -- the larva race's 624 and 625 at zero, then their lzo.
        /// </summary>
        internal static List<Active> CountDown(List<Active> list, bool won)
        {
            var spent = new List<Active>();
            foreach (var active in list)
            {
                bool counts = active.Kind == Kind.Fights || (won && active.Kind == Kind.Victories);
                if (!counts || active.Left <= 0) continue;
                active.Left--;
                if (active.Left == 0) spent.Add(active);
            }
            return spent;
        }

        /// <summary>The timed ones whose end has come.</summary>
        internal static List<Active> Due(IEnumerable<Active> list, long nowMs)
            => list.Where(a => a.Kind == Kind.Timed && a.UntilMs <= nowMs).ToList();

        /// <summary>A copy of what the character has, in the order they were set.</summary>
        public static IReadOnlyList<Active> Of(long characterId)
        {
            if (characterId == 0) return Array.Empty<Active>();
            var list = ListOf(characterId);
            lock (list) return list.Select(a => a.Copy()).ToList();
        }

        /// <summary>Whether the character has the alteration.</summary>
        public static bool Has(long characterId, int alteration)
        {
            if (characterId == 0) return false;
            var list = ListOf(characterId);
            lock (list) return list.Any(a => a.Id == alteration);
        }

        /// <summary>
        /// An alteration set, taking the place of the same one if it was already there. Null when the
        /// client does not know it.
        /// </summary>
        public static Active? Gain(long characterId, int alteration, DateTime nowUtc, long untilMs = 0)
        {
            var definition = AlterationCatalogue.Of(alteration);
            if (definition == null || characterId == 0) return null;

            var active = Start(definition, new DateTimeOffset(nowUtc).ToUnixTimeMilliseconds(), untilMs);
            var list = ListOf(characterId);
            lock (list)
            {
                list.RemoveAll(a => a.Id == alteration);
                list.Add(active);
            }
            Store(characterId, active);
            return active.Copy();
        }

        /// <summary>An alteration taken away; null when the character did not have it.</summary>
        public static Active? Lose(long characterId, int alteration)
        {
            if (characterId == 0) return null;
            var list = ListOf(characterId);
            Active? gone;
            lock (list)
            {
                gone = list.FirstOrDefault(a => a.Id == alteration);
                if (gone == null) return null;
                list.Remove(gone);
            }
            Delete(characterId, alteration);
            return gone.Copy();
        }

        /// <summary>
        /// One fight over for the character: the counts brought down and stored. Returns the whole
        /// list as it stands -- the spent ones still in it, at zero -- and the spent ones, now gone.
        /// </summary>
        public static (IReadOnlyList<Active> Listed, IReadOnlyList<Active> Spent) CountFight(long characterId, bool won)
        {
            if (characterId == 0) return (Array.Empty<Active>(), Array.Empty<Active>());
            var list = ListOf(characterId);
            List<Active> listed, spent, changed;
            lock (list)
            {
                changed = list.Where(a => a.Left > 0 && (a.Kind == Kind.Fights || (won && a.Kind == Kind.Victories)))
                              .ToList();
                spent = CountDown(list, won);
                listed = list.Select(a => a.Copy()).ToList();
                list.RemoveAll(spent.Contains);
            }
            foreach (var active in changed)
            {
                if (spent.Contains(active)) Delete(characterId, active.Id);
                else Store(characterId, active);
            }
            return (listed, spent.Select(a => a.Copy()).ToList());
        }

        /// <summary>The timed ones that have ended, taken away.</summary>
        public static IReadOnlyList<Active> Expire(long characterId, DateTime nowUtc)
        {
            if (characterId == 0) return Array.Empty<Active>();
            long nowMs = new DateTimeOffset(nowUtc).ToUnixTimeMilliseconds();
            var list = ListOf(characterId);
            List<Active> due;
            lock (list)
            {
                due = Due(list, nowMs);
                if (due.Count == 0) return due;
                list.RemoveAll(due.Contains);
            }
            foreach (var active in due) Delete(characterId, active.Id);
            return due;
        }

        /// <summary>
        /// What the character's alterations add to the sheet, characteristic by characteristic: the
        /// +50 initiative of «Influencia de Jibejan», the wisdom of a candy's. The kub carries it in
        /// field 5 of each characteristic, where the captured initiative had its 50 while it lasted.
        /// </summary>
        public static Dictionary<int, long> Bonuses(long characterId)
        {
            var total = new Dictionary<int, long>();
            foreach (var active in Of(characterId))
            {
                var definition = AlterationCatalogue.Of(active.Id);
                if (definition == null) continue;
                foreach (var effect in definition.Effects)
                {
                    if (!EffectTable.TryGet(effect.Effect, out var what) || what.Characteristic == 0) continue;
                    long value = effect.Sheet;
                    if (value == 0) continue;
                    total.TryGetValue(what.Characteristic, out long had);
                    total[what.Characteristic] = had + what.Sign * value;
                }
            }
            return total;
        }

        /// <summary>
        /// The spells the character's alterations lend him in a fight (1175, the spell in its dice),
        /// held as attitudes: an uchronic elixir's «[!] Élixir uchronique mineur», which puts state
        /// 3383 on the allies.
        /// </summary>
        public static List<int> FightAttitudes(long characterId)
        {
            var spells = new List<int>();
            foreach (var active in Of(characterId))
            {
                var definition = AlterationCatalogue.Of(active.Id);
                if (definition == null) continue;
                foreach (var effect in definition.Effects)
                    if (effect.Effect == FightSpell && effect.DiceNum > 0 && !spells.Contains((int)effect.DiceNum))
                        spells.Add((int)effect.DiceNum);
            }
            return spells;
        }

        /// <summary>«Da el hechizo»: the spell an alteration or an item lends for the fight.</summary>
        public const int FightSpell = 1175;

        /// <summary>What they add to one characteristic.</summary>
        public static long BonusOf(long characterId, int characteristic)
            => Bonuses(characterId).TryGetValue(characteristic, out long value) ? value : 0;

        /// <summary>For the tests: forget what was read, so the next read goes to the base.</summary>
        internal static void Forget(long characterId) => _known.TryRemove(characterId, out _);

        private static List<Active> ListOf(long characterId) => _known.GetOrAdd(characterId, Read);

        private static List<Active> Read(long characterId)
        {
            var list = new List<Active>();
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT AlterationId, FromMs, Kind, LeftCount, Total, UntilMs " +
                                      "FROM CharacterAlterations WHERE CharacterId = $c ORDER BY FromMs, AlterationId;";
                command.Parameters.AddWithValue("$c", characterId);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new Active
                    {
                        Id = reader.GetInt32(0),
                        FromMs = reader.GetInt64(1),
                        Kind = (Kind)reader.GetInt32(2),
                        Left = reader.GetInt32(3),
                        Total = reader.GetInt32(4),
                        UntilMs = reader.GetInt64(5),
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Alterations] Could not read the alterations of {characterId}: {ex.Message}");
            }
            return list;
        }

        private static void Store(long characterId, Active active)
        {
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "INSERT INTO CharacterAlterations (CharacterId, AlterationId, FromMs, Kind, LeftCount, Total, UntilMs) " +
                    "VALUES ($c, $a, $f, $k, $l, $t, $u) ON CONFLICT(CharacterId, AlterationId) DO UPDATE SET " +
                    "FromMs = excluded.FromMs, Kind = excluded.Kind, LeftCount = excluded.LeftCount, " +
                    "Total = excluded.Total, UntilMs = excluded.UntilMs;";
                command.Parameters.AddWithValue("$c", characterId);
                command.Parameters.AddWithValue("$a", active.Id);
                command.Parameters.AddWithValue("$f", active.FromMs);
                command.Parameters.AddWithValue("$k", (int)active.Kind);
                command.Parameters.AddWithValue("$l", active.Left);
                command.Parameters.AddWithValue("$t", active.Total);
                command.Parameters.AddWithValue("$u", active.UntilMs);
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Alterations] Could not store alteration {active.Id} of {characterId}: {ex.Message}");
            }
        }

        private static void Delete(long characterId, int alteration)
        {
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM CharacterAlterations WHERE CharacterId = $c AND AlterationId = $a;";
                command.Parameters.AddWithValue("$c", characterId);
                command.Parameters.AddWithValue("$a", alteration);
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Alterations] Could not remove alteration {alteration} of {characterId}: {ex.Message}");
            }
        }

        private static void EnsureTable()
        {
            if (_tableReady) return;
            lock (_tableLock)
            {
                if (_tableReady) return;
                using var connection = Open();
                using var create = connection.CreateCommand();
                create.CommandText = @"
                    CREATE TABLE IF NOT EXISTS CharacterAlterations (
                        CharacterId  INTEGER NOT NULL,
                        AlterationId INTEGER NOT NULL,
                        FromMs       INTEGER NOT NULL,
                        Kind         INTEGER NOT NULL,
                        LeftCount    INTEGER NOT NULL DEFAULT 0,
                        Total        INTEGER NOT NULL DEFAULT 0,
                        UntilMs      INTEGER NOT NULL DEFAULT 0,
                        PRIMARY KEY (CharacterId, AlterationId));";
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

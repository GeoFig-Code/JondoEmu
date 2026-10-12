using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Jondo.Unity.Server.Network;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The special spells a character has learnt: the ones no class carries, taught by a scroll.
    /// </summary>
    /// <remarks>
    /// <para>
    /// They are the common pairs of the spell variants -- Llamita and Llamilla, Liberación and
    /// Magnetismo, Fulminación and Lección de Grunob, the Arakna and Chaferloko summons, Dominio del
    /// Arma... -- and Captura de Almas, which pairs with nothing. A character does not have them
    /// because of his class: the one just created in the captures has none in his hms, and the
    /// account's level 200 has ten of them and not Bumerán, Manticolmillo or Kaniburbuja. They travel
    /// in the hms with the origin 2 the melee has, not the 1 of the class spells.
    /// </para>
    /// <para>
    /// A scroll (type 75, effect 604 «Aprende el nivel de hechizo #3») names what it teaches twice: its
    /// third number is a spell level, and its criterion asks for neither spell of the pair --
    /// «PT!24006&amp;PT!350». The level is stale in the newer ones (Llamita's 28324 is a level of
    /// «Atracción de Balí» in this client), so the criterion rules and the level is only for the
    /// scroll that has none, Captura de Almas's.
    /// </para>
    /// <para>
    /// Kept in world.db, in a table of its own created at first use:
    /// </para>
    /// <code>
    ///   CharacterLearnedSpells (CharacterId, SpellId)
    /// </code>
    /// </remarks>
    public static class LearnedSpells
    {
        /// <summary>«Aprende el nivel de hechizo #3».</summary>
        public const int LearnEffect = 604;

        /// <summary>The spell scrolls' item type, «Pergamino con un hechizo».</summary>
        public const int ScrollType = 75;

        private static readonly Regex HasSpell = new(@"PT[!=](\d+)", RegexOptions.Compiled);

        private static readonly ConcurrentDictionary<long, HashSet<int>> _known = new();
        private static readonly object _tableLock = new();
        private static volatile bool _tableReady;

        private static IReadOnlyCollection<int>? _everything;
        private static readonly object _everythingLock = new();

        /// <summary>
        /// The spell a scroll teaches: the first spell of a common pair its criterion names, as the
        /// pair's base; or, with none named, the spell of the level it carries. 0 when neither says.
        /// </summary>
        internal static int Resolve(string criterion, long level, Func<long, int> spellOfLevel)
        {
            foreach (Match match in HasSpell.Matches(criterion ?? ""))
            {
                if (int.TryParse(match.Groups[1].Value, out int spell) && SpellTable.CommonPairOf(spell) is { } pair)
                    return pair.Base;
            }
            int byLevel = level > 0 ? spellOfLevel(level) : 0;
            if (byLevel == 0) return 0;
            if (SpellTable.CommonPairOf(byLevel) is { } itsPair) return itsPair.Base;
            return SpellTable.PairOf(byLevel) == null ? byLevel : 0;
        }

        /// <summary>The spell an item teaches, or 0 when it teaches none.</summary>
        public static int TaughtBy(int template)
        {
            var effects = DatabaseManager.GetItemEffectsData(DatabaseManager.GetItemTemplatePossibleEffects(template));
            var teach = effects.FirstOrDefault(e => e.EffectId == LearnEffect);
            if (teach == null) return 0;
            long level = teach.Value != 0 ? teach.Value : (teach.DiceNum != 0 ? teach.DiceNum : teach.DiceSide);
            return Resolve(DatabaseManager.GetItemTemplateCriterion(template), level, SpellOfLevel);
        }

        /// <summary>
        /// Every special spell there is to learn: each common pair and what the scrolls teach outside
        /// them. An administrator has them all, as he has every attitude.
        /// </summary>
        public static IReadOnlyCollection<int> Everything
        {
            get
            {
                if (_everything != null) return _everything;
                lock (_everythingLock)
                {
                    if (_everything != null) return _everything;
                    var all = new HashSet<int>();
                    foreach (var pair in SpellTable.CommonPairs) all.Add(pair.Base);
                    foreach (int scroll in DatabaseManager.ItemTemplatesOfType(ScrollType))
                    {
                        int spell = TaughtBy(scroll);
                        if (spell != 0) all.Add(spell);
                    }
                    _everything = all;
                    return all;
                }
            }
        }

        /// <summary>What a character has learnt.</summary>
        public static IReadOnlyCollection<int> Of(long characterId)
        {
            if (characterId == 0) return Array.Empty<int>();
            var set = _known.GetOrAdd(characterId, Read);
            lock (set) return set.ToList();
        }

        /// <summary>What a character has to draw his spells with: all of them for an administrator.</summary>
        public static IReadOnlyCollection<int> For(long characterId, long accountId)
            => AdminSpells.Para(accountId) ? Everything : Of(characterId);

        /// <summary>The same, for whoever is in session.</summary>
        public static IReadOnlyCollection<int> OfSession()
            => For(SessionContext.State.CharacterId, SessionContext.Current?.AccountId ?? 0);

        /// <summary>Whether a set of learnt spells holds a spell: itself, or the pair it belongs to.</summary>
        public static bool Holds(IReadOnlyCollection<int> learned, int spell)
        {
            if (learned.Contains(spell)) return true;
            var pair = SpellTable.CommonPairOf(spell);
            return pair != null && (learned.Contains(pair.Base) || learned.Contains(pair.Variant));
        }

        /// <summary>A spell learnt, stored. False when it was already.</summary>
        public static bool Learn(long characterId, int spell)
        {
            if (characterId == 0 || spell == 0) return false;
            var set = _known.GetOrAdd(characterId, Read);
            lock (set)
            {
                if (!set.Add(spell)) return false;
            }
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT OR IGNORE INTO CharacterLearnedSpells (CharacterId, SpellId) VALUES ($c, $s);";
                command.Parameters.AddWithValue("$c", characterId);
                command.Parameters.AddWithValue("$s", spell);
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Spells] Could not store spell {spell} learnt by {characterId}: {ex.Message}");
            }
            return true;
        }

        /// <summary>For the tests: forget what was read, so the next read goes to the base.</summary>
        internal static void Forget(long characterId) => _known.TryRemove(characterId, out _);

        private static int SpellOfLevel(long levelId)
        {
            try
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT SpellId FROM SpellLevels WHERE Id = $l;";
                command.Parameters.AddWithValue("$l", levelId);
                return command.ExecuteScalar() is long spell ? (int)spell : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static HashSet<int> Read(long characterId)
        {
            var set = new HashSet<int>();
            try
            {
                EnsureTable();
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT SpellId FROM CharacterLearnedSpells WHERE CharacterId = $c;";
                command.Parameters.AddWithValue("$c", characterId);
                using var reader = command.ExecuteReader();
                while (reader.Read()) set.Add(reader.GetInt32(0));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Spells] Could not read the spells {characterId} has learnt: {ex.Message}");
            }
            return set;
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
                    CREATE TABLE IF NOT EXISTS CharacterLearnedSpells (
                        CharacterId INTEGER NOT NULL,
                        SpellId     INTEGER NOT NULL,
                        PRIMARY KEY (CharacterId, SpellId));";
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

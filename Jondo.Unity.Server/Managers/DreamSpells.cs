using System;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The dream spells: what the fountain sells and the favour gives as "Actif", and what a
    /// dream's people cast from its side bar (InfiniteDreamSpellBar).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A reward teaches one with its effect 3405, whose value is the SPELL LEVEL taught: "Psst
    /// Psst" is 85231, grade 1 of spell 32424. That level is the one cast, and it has to be: the
    /// dream spells carry more grades, and every grade asks for level 1, so the usual lookup --
    /// the highest grade the caster's level allows -- took Psst Psst's grade 3, a lone "removes
    /// the effects of the spell", and Palma Ancestral's grade 2, the threshold's "kills the
    /// target". Those grades are the spells' own sub-casts (792 on themselves), never cast by hand.
    /// </para>
    /// <para>
    /// The client's spell types say which spells are the dream's: 4093 the ones kept the whole
    /// dream, 4094 the ones with charges -- Oblivion and Domi Reversi, "Consume un recurso del
    /// hechizo", their effect 4004 -- and 4105 the passives (see <see cref="Dreams.ApplyTo"/>).
    /// The guide: "Quand vous obtenez un sort, tous les personnages du songe peuvent l'utiliser",
    /// so who may cast one is whoever fights in the dream that holds it, and "tous les sorts ont
    /// un intervalle de relance global": their grade 1 carries a global cooldown of -1.
    /// </para>
    /// </remarks>
    public static class DreamSpells
    {
        /// <summary>The effect a reward teaches a dream spell with: its value is the spell level.</summary>
        public const int LearnEffect = 3405;

        /// <summary>"Consomme #3 ressource de songe": one charge of the spell is spent.</summary>
        public const int ConsumeEffect = 4004;

        /// <summary>The client's spell types of the dream's spells.</summary>
        public const int ActiveType = 4093;
        public const int ChargeType = 4094;
        public const int PassiveType = 4105;

        private static readonly ConcurrentDictionary<int, int> _typeOf = new();
        private static readonly ConcurrentDictionary<int, (int Spell, int Grade)> _level = new();

        /// <summary>Whether a spell is one of the dream's, cast from its bar: types 4093 and 4094.</summary>
        public static bool IsDreamSpell(int spell) => TypeOf(spell) is ActiveType or ChargeType;

        /// <summary>Whether a dream spell is spent by charges: type 4094.</summary>
        public static bool HasCharges(int spell) => TypeOf(spell) == ChargeType;

        /// <summary>
        /// The dream spell a dream holds: the level its reward taught, that level's grade, and how
        /// many it holds -- the charges of a 4094, 1 for any other. Null when it holds none.
        /// </summary>
        public static (int LevelId, int Grade, int Count)? Held(Dreams.Sueno dream, int spell)
        {
            if (dream == null || spell == 0) return null;
            foreach (var bonus in dream.Ganados)
            {
                if (bonus.Efecto != LearnEffect || bonus.Count <= 0) continue;
                var (levelSpell, grade) = LevelOf(bonus.Valor);
                if (levelSpell == spell) return (bonus.Valor, grade, bonus.Count);
            }
            return null;
        }

        /// <summary>
        /// One charge of a dream spell spent: its count down by one, and the spell gone from the
        /// dream with the last. False when the dream did not hold it.
        /// </summary>
        public static bool Consume(Dreams.Sueno dream, int spell)
        {
            for (int i = 0; i < dream.Ganados.Count; i++)
            {
                var bonus = dream.Ganados[i];
                if (bonus.Efecto != LearnEffect || LevelOf(bonus.Valor).Spell != spell) continue;
                if (bonus.Count <= 1) dream.Ganados.RemoveAt(i);
                else dream.Ganados[i] = bonus.WithCount(bonus.Count - 1);
                return true;
            }
            return false;
        }

        /// <summary>The spell and grade of a spell level, from SpellLevels; (0, 0) when unknown.</summary>
        public static (int Spell, int Grade) LevelOf(int levelId)
            => _level.GetOrAdd(levelId, id =>
            {
                try
                {
                    using var connection = new SqliteConnection(DatabaseManager.WorldConnectionString);
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT SpellId, Grade FROM SpellLevels WHERE Id = $id;";
                    command.Parameters.AddWithValue("$id", id);
                    using var reader = command.ExecuteReader();
                    return reader.Read() ? ((int)reader.GetInt64(0), (int)reader.GetInt64(1)) : (0, 0);
                }
                catch (Exception)
                {
                    return (0, 0);
                }
            });

        /// <summary>A spell's type in the client's Spells table; 0 when unknown.</summary>
        public static int TypeOf(int spell)
            => spell <= 0 ? 0 : _typeOf.GetOrAdd(spell, id =>
            {
                try
                {
                    using var connection = new SqliteConnection(DatabaseManager.WorldConnectionString);
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT TypeId FROM Spells WHERE Id = $id;";
                    command.Parameters.AddWithValue("$id", id);
                    return command.ExecuteScalar() is long type ? (int)type : 0;
                }
                catch (Exception)
                {
                    return 0;
                }
            });
    }
}

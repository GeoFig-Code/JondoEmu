using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Jondo.Unity.Server.Network;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// A character mutated: the item in the mutation slot (20) turns him into a monster -- its look,
    /// its spells, its sheet -- on the map and in his fights, until it is taken away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured in «orbalantir de meriana-teleport a todas las peleas epicas», which takes one by one
    /// to the nine epic fights. The mutation is an item of type 27 whose 197 «Se transforma en #1»
    /// carries the monster in its dice number and its grade in its dice side -- «Reflejo de
    /// Prismaradona» (15019) is 3577 at grade 1 -- with 811, its fights, at 1. Arriving in the slot
    /// (iua) it brings, in that order:
    /// </para>
    /// <list type="bullet">
    ///   <item>hms with the monster's spells after the character's, as f3 { f1 grade, f3 spell,
    ///     f4 6 } -- 4312 to 4316 --;</item>
    ///   <item>itg with only those, slot by slot from 0, and its f2 at 1;</item>
    ///   <item>kmb { f1 the monster's look -- {2554|||100} is bones 2554, scale 100 --, f2 the
    ///     character };</item>
    ///   <item>kub with the monster grade's sheet: 10000 life, 4 AP, 4 MP, nothing of his own nor
    ///     of his gear;</item>
    /// </list>
    /// <para>
    /// and the map draws him with the humanoid's identity block as f2 { f1 grade, f2 monster } in
    /// place of its f1. Taken away (ium), the server sends hod { f1 17 }, hms, itg, kmb and kub
    /// again, the character's own.
    /// </para>
    /// </remarks>
    public static class Mutations
    {
        /// <summary>The client's INVENTORY_POSITION_MUTATION.</summary>
        public const int Slot = 20;

        /// <summary>«Se transforma en #1»: the monster in the dice number, its grade in the dice side.</summary>
        public const int TransformsInto = 197;

        /// <summary>The origin of a mutation's spells in the hms: 6, as the summons' in a jyy.</summary>
        public const int SpellOrigin = 6;

        /// <summary>The hod that goes before the character's own spells come back.</summary>
        public const int EndedMark = 17;

        /// <summary>A mutation: the monster, its grade from 1, and its look.</summary>
        public sealed record Mutation(int Monster, int Grade, string Look);

        /// <summary>What a character's mutation slot holds, by character: read once, kept up to date here.</summary>
        private static readonly ConcurrentDictionary<long, Mutation?> _known = new();

        /// <summary>The mutation some effects make, or null.</summary>
        public static Mutation? Of(IEnumerable<Equipment.ItemEffect> effects)
        {
            foreach (var effect in effects)
            {
                if (effect.Effect != TransformsInto || effect.DiceNum <= 0) continue;
                MobSpawnManager.EnsureMonsterData();
                var data = MobSpawnManager.GetMonsterData((int)effect.DiceNum);
                if (data == null) return null;
                return new Mutation((int)effect.DiceNum, (int)Math.Max(1, effect.DiceSide), data.Look ?? "");
            }
            return null;
        }

        /// <summary>The mutation of the character in session, from his slot.</summary>
        public static Mutation? Current()
        {
            long me = SessionContext.State.CharacterId;
            var item = Equipment.InSlot(Slot);
            var mutation = item == null ? null : Of(item.Effects);
            if (me != 0) _known[me] = mutation;
            return mutation;
        }

        /// <summary>Any character's mutation: his session's when it is his, else what was read of his slot.</summary>
        public static Mutation? OfCharacter(long characterId)
        {
            if (characterId == 0) return null;
            if (characterId == SessionContext.State.CharacterId) return Current();
            return _known.GetOrAdd(characterId, Read);
        }

        /// <summary>Forgets what was known of a character's slot: it has changed.</summary>
        public static void Forget(long characterId) => _known.TryRemove(characterId, out _);

        /// <summary>The spells a mutation casts, at the grades of its monster's grade: not the ones it has not at that grade.</summary>
        public static List<(int Spell, int Grade)> SpellsOf(Mutation mutation)
        {
            var stats = DatabaseManager.GetMonsterGradeStats(mutation.Monster, mutation.Grade - 1);
            var spells = new List<(int, int)>();
            if (stats == null) return spells;
            foreach (int spell in stats.SpellIds.Where(s => !stats.SpellsAbsentAtGrade.Contains(s)))
                spells.Add((spell, Math.Max(1, stats.SpellGrades.TryGetValue(spell, out int g) ? g : 1)));
            return spells;
        }

        /// <summary>The sheet of a mutation's monster grade.</summary>
        public static MonsterGradeStats? SheetOf(Mutation mutation)
            => DatabaseManager.GetMonsterGradeStats(mutation.Monster, mutation.Grade - 1);

        private static Mutation? Read(long characterId)
        {
            try
            {
                using var connection = new Microsoft.Data.Sqlite.SqliteConnection(DatabaseManager.WorldConnectionString);
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT Effects FROM CharacterItems WHERE CharacterId = $id AND Position = $slot LIMIT 1;";
                command.Parameters.AddWithValue("$id", characterId);
                command.Parameters.AddWithValue("$slot", Slot);
                return command.ExecuteScalar() is string effects ? Of(Equipment.ParseEffects(effects)) : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Mutations] Could not read {characterId}'s mutation slot: {ex.Message}");
                return null;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jondo.Unity.Launcher;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The pets' growth: from level 0 to 100, fed with resources, their characteristics growing with
    /// the level.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client's data (datos/pets_*.json, tools/extract_pets.py): the pets -- «Mascota» (18) and
    /// «Mascotura» (121) -- are the evolutive item type 1, its levels 0 to 100 with the experience
    /// each starts at, and an experienceBoost of 10. Each pet's evolutive effects give, by level, a
    /// characteristic: the Miaumiau's power is [0, 7] then [100, 0.63], 7 at level 0 and the 70 of
    /// its template at 100.
    /// </para>
    /// <para>
    /// The client's own reckoning, read in Ghidra, is what is given:
    /// </para>
    /// <list type="bullet">
    ///   <item>A characteristic at a level (EvolutiveEffectData.GetEffectValueByLevel): range by range,
    ///     the levels up to the range's (min) less the previous one's, plus one, times the range's
    ///     per-level value in single precision, truncated -- the previous one starting at 1 unless the
    ///     first range starts at 1. A level-up adds the difference (GetEvolutiveEffectInstanceByLevelRange).</item>
    ///   <item>What a food is worth (PetFoodTooltipBuilder.GetPetFoodTooltipXpText): its fixed
    ///     experience (2820, the «Galleta enriquecida»'s 500), or its worth in nuggets (me.ohq, see
    ///     <see cref="Recycling.PetFoodWorth"/>) times the experienceBoost; times the quantity,
    ///     rounded down, and never past what level 100 asks.</item>
    ///   <item>The level the experience reaches: the highest whose experience it has.</item>
    /// </list>
    /// <para>
    /// A pet's level and experience travel in its effects, as a ride's do: 2821 and 2819. A pet
    /// without them is one from before this: full grown, at level 100.
    /// </para>
    /// </remarks>
    public static class Pets
    {
        public const int Level = 2821;
        public const int Experience = 2819;

        /// <summary>«+#3 puntos de experiencia»: a food's own experience.</summary>
        public const int FixedExperience = 2820;

        /// <summary>The most a feeding takes (FeedUI.GetMaxFeedItems).</summary>
        public const int MostPerFeeding = 50;

        public sealed record Growth(int Action, IReadOnlyList<(int Level, float PerLevel)> Ranges);

        private sealed record Document(int MaxLevel, double Boost, IReadOnlyList<long> ExperienceByLevel,
                                       IReadOnlyDictionary<int, IReadOnlyList<Growth>> Pets);

        private static readonly object _lock = new();
        private static Document? _loaded;

        private static Document Loaded
        {
            get
            {
                if (_loaded != null) return _loaded;
                lock (_lock) return _loaded ??= Load();
            }
        }

        /// <summary>Whether an item is a pet that grows: one of the evolutive pets.</summary>
        public static bool IsPet(int gid) => Loaded.Pets.ContainsKey(gid);

        /// <summary>The evolutive type's top level, 101: a legendary pet's.</summary>
        public static int MaxLevel => Loaded.MaxLevel;

        /// <summary>The level a fed pet stops at: the last of its experience table.</summary>
        public static int TopLevel => Math.Max(0, Loaded.ExperienceByLevel.Count - 1);

        /// <summary>The experience a level starts at.</summary>
        public static long ExperienceAt(int level)
        {
            var table = Loaded.ExperienceByLevel;
            return table.Count == 0 ? 0 : table[Math.Clamp(level, 0, table.Count - 1)];
        }

        /// <summary>The level some experience reaches.</summary>
        public static int LevelFor(long experience)
        {
            var table = Loaded.ExperienceByLevel;
            int level = 0;
            for (int i = 0; i < table.Count; i++)
            {
                if (table[i] <= experience) level = i;
                else break;
            }
            return level;
        }

        /// <summary>A characteristic's growth up to a level: see the class remarks.</summary>
        public static int ValueAt(IReadOnlyList<(int Level, float PerLevel)> ranges, int level)
        {
            if (ranges.Count == 0) return 0;
            int previous = ranges[0].Level != 1 ? 1 : 0;
            int total = 0;
            foreach (var (upTo, perLevel) in ranges)
            {
                int reached = Math.Min(level, upTo);
                total += (int)((float)(reached - previous + 1) * perLevel);
                previous = upTo;
            }
            return total;
        }

        /// <summary>
        /// A pet as it comes into the world: level 0, no experience, each characteristic at its
        /// first range's value and what level 0 adds to it.
        /// </summary>
        public static List<Equipment.ItemEffect> Fresh(int gid)
        {
            var effects = new List<Equipment.ItemEffect> { new(Level, 0, 0, 0), new(Experience, 0, 0, 0) };
            if (!Loaded.Pets.TryGetValue(gid, out var growth)) return effects;
            foreach (var g in growth)
            {
                int start = g.Ranges.Count > 0 && g.Ranges[0].Level == 0 ? (int)g.Ranges[0].PerLevel : 0;
                int value = start + ValueAt(g.Ranges, 0);
                if (value != 0) effects.Add(new Equipment.ItemEffect(g.Action, value, 0, 0));
            }
            return effects;
        }

        /// <summary>A pet's level: its 2821, or the top for one from before the growth.</summary>
        public static int LevelOf(IReadOnlyList<Equipment.ItemEffect> effects)
        {
            foreach (var e in effects) if (e.Effect == Level) return (int)e.Value;
            return TopLevel;
        }

        /// <summary>A pet's experience: its 2819, or the top's for one from before the growth.</summary>
        public static long ExperienceOf(IReadOnlyList<Equipment.ItemEffect> effects)
        {
            if (!effects.Any(e => e.Effect == Level)) return ExperienceAt(TopLevel);
            foreach (var e in effects) if (e.Effect == Experience) return e.Value;
            return 0;
        }

        /// <summary>
        /// The experience <paramref name="quantity"/> of a food give a pet: its own (2820) or its
        /// worth in nuggets times the boost. 0 for what is no food.
        /// </summary>
        public static double ExperienceOfFood(int gid, int quantity)
        {
            if (quantity <= 0) return 0;
            long fixedXp = 0;
            foreach (var e in DatabaseManager.GetItemEffectsData(DatabaseManager.GetItemTemplatePossibleEffects(gid)))
                if (e.EffectId == FixedExperience) fixedXp = Math.Max(fixedXp, Math.Max(e.Value, e.DiceNum));
            if (fixedXp > 0) return (double)quantity * fixedXp;
            double worth = Recycling.PetFoodWorth(gid);
            return worth > 0 ? quantity * worth * Loaded.Boost : 0;
        }

        /// <summary>
        /// A pet's effects after <paramref name="gained"/> experience: its experience and level, and
        /// each characteristic grown by what the new level adds. The experience stops at the top level's.
        /// </summary>
        public static List<Equipment.ItemEffect> Fed(int gid, IReadOnlyList<Equipment.ItemEffect> effects, long gained,
                                                     out int levelBefore, out int levelAfter)
        {
            levelBefore = LevelOf(effects);
            long experience = Math.Min(ExperienceOf(effects) + Math.Max(0, gained), ExperienceAt(TopLevel));
            levelAfter = Math.Max(levelBefore, LevelFor(experience));

            var list = Rides.With(Rides.With(effects, Experience, experience), Level, levelAfter);
            if (levelAfter == levelBefore || !Loaded.Pets.TryGetValue(gid, out var growth)) return list;
            foreach (var g in growth)
            {
                int added = ValueAt(g.Ranges, levelAfter) - ValueAt(g.Ranges, levelBefore);
                if (added == 0) continue;
                long had = list.Where(e => e.Effect == g.Action).Select(e => e.Value).FirstOrDefault();
                list = Rides.With(list, g.Action, had + added);
            }
            return list;
        }

        private static Document Load()
        {
            string path = Paths.PetsJson;
            var pets = new Dictionary<int, IReadOnlyList<Growth>>();
            var table = new List<long>();
            int maxLevel = 0;
            double boost = 1;
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Pets] {path} not found: pets do not grow. Generate it with tools/extract_pets.py.");
                return new Document(0, 1, table, pets);
            }
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var type = doc.RootElement.GetProperty("type");
                maxLevel = type.GetProperty("maxLevel").GetInt32();
                boost = type.GetProperty("experienceBoost").GetDouble();
                table.AddRange(type.GetProperty("experienceByLevel").EnumerateArray().Select(x => x.GetInt64()));
                foreach (var pet in doc.RootElement.GetProperty("pets").EnumerateObject())
                {
                    if (!int.TryParse(pet.Name, out int gid)) continue;
                    pets[gid] = pet.Value.EnumerateArray()
                        .Select(g => new Growth(g.GetProperty("action").GetInt32(),
                            g.GetProperty("ranges").EnumerateArray()
                             .Select(r => ((int)r[0].GetDouble(), (float)r[1].GetDouble())).ToList()))
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Pets] Could not read {path}: {ex.Message}");
            }
            return new Document(maxLevel, boost, table, pets);
        }
    }
}

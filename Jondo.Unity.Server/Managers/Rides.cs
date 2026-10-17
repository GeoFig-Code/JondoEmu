using System;
using System.Collections.Generic;
using System.Linq;
using Jondo.Unity.Server.Network;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// A ride is an item of type 331, 332 or 333, and all it is -- its level, its experience, its sex,
    /// its serenity, its three gauges, whether it is fertile or senile, its capacities, its name --
    /// travels in its effects, the ids the client's ActionId enum gives them.
    /// </summary>
    /// <remarks>
    /// THE EFFECTS (Core.DataCenter.Metadata.Effect.ActionId): EvolutiveObjectExperience 2819,
    /// EvolutivePetLevel 2821, RideName 3809, RideCapacityChameleon to RideCapacityGrower 3823-3828,
    /// RideGender 3829, RideFertile 3830, RideMood 3831, RideParents 3832, RideGaugeLove 3833,
    /// RideGaugeStamina 3834, RideGaugeMaturity 3835, RideSenile 3836. The item templates carry the
    /// gender, the gauges, the serenity and the fertility; the client hides all of these from the
    /// item's tooltip (mk::ojx) and reads them from the ride message instead (<see cref="Hqu"/>).
    ///
    /// THE CHARACTERISTICS grow with the level: each ride item's evolutive effects give so much per
    /// level up to a level, and so much more after (the Dragopavo ébano: 3 vitality a level to 100,
    /// then 1, so 400 at 200; the client's guide: "En el nivel máximo, todos los dragopavos dan 400
    /// puntos de vitalidad").
    ///
    /// THE RIDE MESSAGE, hqu, read in Ghidra off mk::ojy: { f1 experience, f2 its effects, f3 its level,
    /// f4 male, f5 its name, f6 its gauges { f1 value, f3 which: 0 love, 1 maturity, 2 stamina },
    /// f7 senile, f8 its capacities (0 chameleon, 1 reproducer, 2 wise, 3 tough, 4 lover, 5 grower),
    /// f10 sterile, f11 serenity, f12 its RidesData id }.
    /// </remarks>
    public static class Rides
    {
        public const int Experience = 2819;
        public const int Level = 2821;
        public const int Name = 3809;
        public const int Gender = 3829;
        public const int Fertile = 3830;
        public const int Mood = 3831;
        public const int ParentsEffect = 3832;
        public const int Love = 3833;
        public const int Stamina = 3834;
        public const int Maturity = 3835;
        public const int Senile = 3836;

        /// <summary>The six capacities, in the client's order (hqe, o): chameleon, reproducer, wise, tough, lover, grower.</summary>
        public const int Chameleon = 3823;
        public const int Reproducer = 3824;
        public const int Wise = 3825;
        public const int Tough = 3826;
        public const int Lover = 3827;
        public const int Grower = 3828;
        public static readonly IReadOnlyList<int> Capacities = new[] { Chameleon, Reproducer, Wise, Tough, Lover, Grower };

        /// <summary>The ride's three gauges by the client's RideGaugesData id: 1 love, 2 maturity, 3 stamina.</summary>
        public static readonly IReadOnlyDictionary<int, int> GaugeEffect = new Dictionary<int, int>
        {
            [1] = Love,
            [2] = Maturity,
            [3] = Stamina,
        };

        /// <summary>The client's hqg order of the gauges in the ride message: love, maturity, stamina.</summary>
        private static readonly int[] GaugeOrder = { Love, Maturity, Stamina };

        /// <summary>The value the gender effect has for a male, the client's "Montura macho".</summary>
        public const int MaleValue = 0;

        /// <summary>The effects that are the ride's and no characteristic of the rider's.</summary>
        public static bool IsRideEffect(int effect)
            => effect is Experience or Level or Name or (>= Chameleon and <= Senile);

        public static bool IsRide(int gid) => Breeding.RideByItem(gid) != null;

        // ─── Reading ─────────────────────────────────────────────────────────────────────

        private static Equipment.ItemEffect? Find(IReadOnlyList<Equipment.ItemEffect> effects, int id)
        {
            foreach (var e in effects) if (e.Effect == id) return e;
            return null;
        }

        public static bool Has(IReadOnlyList<Equipment.ItemEffect> effects, int id) => Find(effects, id) != null;

        public static long ValueOf(IReadOnlyList<Equipment.ItemEffect> effects, int id) => Find(effects, id)?.Value ?? 0;

        public static int LevelOf(IReadOnlyList<Equipment.ItemEffect> effects)
            => Math.Clamp((int)ValueOf(effects, Level), 1, Breeding.MaxLevel);

        public static long ExperienceOf(IReadOnlyList<Equipment.ItemEffect> effects) => Math.Max(0, ValueOf(effects, Experience));

        public static string? NameOf(IReadOnlyList<Equipment.ItemEffect> effects) => Find(effects, Name)?.Text;

        public static bool IsMale(IReadOnlyList<Equipment.ItemEffect> effects) => ValueOf(effects, Gender) == MaleValue;

        public static int MoodOf(IReadOnlyList<Equipment.ItemEffect> effects)
            => (int)Math.Clamp(ValueOf(effects, Mood), Breeding.MinMood, Breeding.MaxMood);

        public static int GaugeOf(IReadOnlyList<Equipment.ItemEffect> effects, int gaugeEffect) => (int)Math.Max(0, ValueOf(effects, gaugeEffect));

        public static bool IsSenile(IReadOnlyList<Equipment.ItemEffect> effects) => Has(effects, Senile);

        /// <summary>Fertile: it may still breed, once its gauges are full ("Fértil").</summary>
        public static bool IsFertile(IReadOnlyList<Equipment.ItemEffect> effects) => Has(effects, Fertile) && !IsSenile(effects);

        /// <summary>Fecund: fertile with its three gauges full, ready to breed ("Fecunda").</summary>
        public static bool IsFecund(IReadOnlyList<Equipment.ItemEffect> effects)
            => IsFertile(effects) && Breeding.RideGauges.All(g => GaugeEffect.TryGetValue(g.Id, out int e) && GaugeOf(effects, e) >= g.Max);

        public static IReadOnlyList<int> CapacitiesOf(IReadOnlyList<Equipment.ItemEffect> effects)
            => Capacities.Where(c => Has(effects, c)).ToList();

        /// <summary>The ride's parents, by RidesData id, as its genealogy keeps them: parents, then grandparents.</summary>
        public static IReadOnlyList<int> AncestorsOf(IReadOnlyList<Equipment.ItemEffect> effects)
        {
            string? text = Find(effects, ParentsEffect)?.Text;
            if (string.IsNullOrEmpty(text)) return Array.Empty<int>();
            return text.Split(',', StringSplitOptions.RemoveEmptyEntries)
                       .Select(t => int.TryParse(t, out int v) ? v : 0).Where(v => v > 0).ToList();
        }

        // ─── Writing ─────────────────────────────────────────────────────────────────────

        /// <summary>The effects with one of them set to a value (added when it is not there).</summary>
        public static List<Equipment.ItemEffect> With(IEnumerable<Equipment.ItemEffect> effects, int id, long value, string? text = null)
        {
            var list = effects.ToList();
            int at = list.FindIndex(e => e.Effect == id);
            var updated = new Equipment.ItemEffect(id, value, 0, 0, text);
            if (at >= 0) list[at] = updated;
            else list.Add(updated);
            return list;
        }

        public static List<Equipment.ItemEffect> Without(IEnumerable<Equipment.ItemEffect> effects, int id)
            => effects.Where(e => e.Effect != id).ToList();

        /// <summary>The characteristics an item gives at a level, from its evolutive effects.</summary>
        public static List<Equipment.ItemEffect> CharacteristicsAt(int gid, int level)
        {
            var list = new List<Equipment.ItemEffect>();
            foreach (var evolution in Breeding.EvolutionsOf(gid))
            {
                double total = 0;
                int from = 0;
                foreach (var (upTo, perLevel) in evolution.Ranges.OrderBy(r => r.UpTo))
                {
                    int levels = Math.Max(0, Math.Min(level, upTo) - from);
                    total += levels * perLevel;
                    from = upTo;
                    if (level <= upTo) break;
                }
                int value = (int)Math.Floor(total + 1e-9);
                if (value != 0) list.Add(new Equipment.ItemEffect(evolution.Action, value, 0, 0));
            }
            return list;
        }

        /// <summary>The ride's effects with its characteristics those of its level, its own kept as they are.</summary>
        public static List<Equipment.ItemEffect> Recomputed(int gid, IEnumerable<Equipment.ItemEffect> effects)
        {
            var own = effects.Where(e => IsRideEffect(e.Effect)).ToList();
            int level = Math.Clamp((int)(own.FirstOrDefault(e => e.Effect == Level).Value), 1, Breeding.MaxLevel);
            own.AddRange(CharacteristicsAt(gid, level));
            return own;
        }

        /// <summary>
        /// A ride as it is born or caught: level 1 and no experience, its template's gender drawn,
        /// its gauges empty, its serenity drawn in the template's range, fertile unless the template
        /// makes it senile, and the characteristics of level 1.
        /// </summary>
        public static List<Equipment.ItemEffect> Fresh(int gid, IReadOnlyList<(int Effect, int DiceNum, int DiceSide, int Value)> template,
                                                       Random dice)
        {
            var list = new List<Equipment.ItemEffect>
            {
                new(Level, 1, 0, 0),
                new(Experience, 0, 0, 0),
            };
            foreach (var (effect, diceNum, diceSide, value) in template)
            {
                if (!IsRideEffect(effect) || effect == Level || effect == Experience) continue;
                long v = effect switch
                {
                    Gender => dice.Next(2),
                    Mood => diceSide > diceNum ? dice.Next(diceNum, diceSide + 1) : 0,
                    _ => 0,
                };
                list.Add(new Equipment.ItemEffect(effect, v, 0, 0));
            }
            if (!list.Any(e => e.Effect == Gender)) list.Add(new Equipment.ItemEffect(Gender, dice.Next(2), 0, 0));
            if (!list.Any(e => e.Effect == Mood)) list.Add(new Equipment.ItemEffect(Mood, 0, 0, 0));
            foreach (int gauge in GaugeOrder)
                if (!list.Any(e => e.Effect == gauge)) list.Add(new Equipment.ItemEffect(gauge, 0, 0, 0));
            list.AddRange(CharacteristicsAt(gid, 1));
            return list;
        }

        /// <summary>A ride item's template effects, with their dice: what <see cref="Fresh"/> draws from.</summary>
        public static List<(int Effect, int DiceNum, int DiceSide, int Value)> TemplateOf(int gid)
            => DatabaseManager.GetItemEffectsData(DatabaseManager.GetItemTemplatePossibleEffects(gid))
                              .Select(e => (e.EffectId, e.DiceNum, e.DiceSide, e.Value)).ToList();

        /// <summary>A ride as it is born or caught, from its item's template.</summary>
        public static List<Equipment.ItemEffect> Fresh(int gid, Random dice) => Fresh(gid, TemplateOf(gid), dice);

        /// <summary>
        /// A ride item's effects made a ride's: those of an item from before breeding (its template's
        /// characteristics at their highest, no level) become a fresh ride of level 1.
        /// </summary>
        public static List<Equipment.ItemEffect> Normalized(int gid, IReadOnlyList<Equipment.ItemEffect> effects, Random dice)
            => Has(effects, Level) ? Recomputed(gid, effects) : Fresh(gid, dice);

        /// <summary>
        /// Experience added: the level follows it, and with it the characteristics. Nothing is added
        /// past the highest level's experience.
        /// </summary>
        public static List<Equipment.ItemEffect> WithExperience(int gid, IReadOnlyList<Equipment.ItemEffect> effects, long gained)
        {
            long top = Breeding.ExperienceAt(Breeding.MaxLevel);
            long xp = Math.Min(top, ExperienceOf(effects) + Math.Max(0, gained));
            int level = Breeding.LevelFor(xp);
            var list = With(With(effects, Experience, xp), Level, level);
            return level != LevelOf(effects) ? Recomputed(gid, list) : list;
        }

        // ─── The ride message ────────────────────────────────────────────────────────────

        /// <summary>The ride message, hqu, of a ride item.</summary>
        public static Pb Hqu(int gid, IReadOnlyList<Equipment.ItemEffect> effects)
        {
            var ride = Breeding.RideByItem(gid);
            var message = Pb.New().Var(1, ExperienceOf(effects));
            Network.ConnectionProtocol.AddEffects(message, 2, effects.Where(e => !IsRideEffect(e.Effect)));
            message.Var(3, LevelOf(effects));
            if (IsMale(effects)) message.Var(4, 1);
            string? name = NameOf(effects);
            if (!string.IsNullOrEmpty(name)) message.Str(5, name);
            for (int i = 0; i < GaugeOrder.Length; i++)
                message.Msg(6, Pb.New().VarIfNotZero(1, GaugeOf(effects, GaugeOrder[i])).VarIfNotZero(3, i));
            if (IsSenile(effects)) message.Var(7, 1);
            foreach (int capacity in CapacitiesOf(effects)) message.Var(8, capacity - Chameleon);
            if (!IsFertile(effects)) message.Var(10, 1);
            int mood = MoodOf(effects);
            if (mood != 0) message.Var(11, mood);
            message.VarIfNotZero(12, ride?.Id ?? 0);
            return message;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// Breeding two rides, cloning two into one and extracting them: the rules the breeding
    /// window's mating, cloning and extraction tabs show, and what comes of them.
    /// </summary>
    /// <remarks>
    /// THE CLIENT'S RULES (ui.breeding.matingRules): a male and a female, not sterile, not senile, of
    /// the same family, with their three gauges full. Cloning (ui.breeding.cloningRules): the same
    /// family, the same generation, not senile; "La montura obtenida será fecunda y conservará el
    /// color, el sexo, el nombre y la genealogía de la original. Los indicadores se reiniciarán y la
    /// capacidad no se conservará." Extraction (ui.breeding.extractionRule): generation 2 or more,
    /// and the ride is destroyed; a senile one gives one resource (the client's guide, text
    /// 1178359), the others their RidesData's extraction quantity, of their species' resource.
    ///
    /// THE CHILD (the client's data): two colours cross into the child their RidesData lists, the
    /// "target generation". A guide (next-stage, 2026) measured its chance: 30 %, plus 0.15 % a
    /// level of each parent, plus the optimákina's 10 %. Failing it, the child is the colour of a
    /// parent or a grandparent, each counted by its genetic weight ("Each repeated colour in the
    /// family trees raises the chance of that colour"). The reproducer capacity gives two children
    /// ("Se obtendrán dos hijos"). The child is born at level 1, its gauges empty, its sex drawn; the
    /// parents are left sterile. The mákinas (type 323) carry what they do in their effects:
    /// RideBreedTarget (3837: the species and the generation they are for), RideBreedQualityBoost
    /// (3838: points of chance), RideBreedChameleonOpportunity (3839) and the capacities'
    /// opportunities (3840-3844, in percent).
    ///
    /// THE REWARDS (the client's data): genetichas (item 33512) as the child's RidesData gives,
    /// when it reaches a generation above every ride of both families (that guide); and breeder
    /// experience, the parents' RidesData's.
    /// </remarks>
    public static class Mating
    {
        /// <summary>The target generation's chance before levels and mákinas, in percent (the guide).</summary>
        public const double BaseTargetPercent = 30;

        /// <summary>What each level of a parent adds to it, in percent (the guide).</summary>
        public const double PercentPerLevel = 0.15;

        /// <summary>The genetichas, item 33512 ("Geneticha").</summary>
        public const int Geneticha = 33512;

        /// <summary>The mákinas' effects (the client's ActionId).</summary>
        public const int BreedTarget = 3837;
        public const int QualityBoost = 3838;
        public const int ChameleonOpportunity = 3839;

        /// <summary>The capacities' opportunities, 3840-3844, by the capacity they give.</summary>
        public static readonly IReadOnlyDictionary<int, int> CapacityOpportunities = new Dictionary<int, int>
        {
            [3839] = Rides.Chameleon,
            [3840] = Rides.Reproducer,
            [3841] = Rides.Wise,
            [3842] = Rides.Tough,
            [3843] = Rides.Lover,
            [3844] = Rides.Grower,
        };

        /// <summary>Why two rides cannot breed, in the client's hsp: what its errors say.</summary>
        public enum Refusal
        {
            None = -1,
            StableFull = 0,
            Unknown = 1,
            SameSex = 4,
            Sterile = 5,
            InterSpecies = 6,
            NeedDistinctParent = 7,
            MissingFillGauges = 9,
        }

        /// <summary>Why two rides cannot be cloned, in the client's hsj.</summary>
        public enum CloneRefusal
        {
            None = -1,
            RideNotFound = 0,
            DifferentGenerations = 1,
            InterSpecies = 2,
            Senile = 3,
            Unknown = 4,
        }

        /// <summary>A mákina, as its effects say: the species and generation it is for, its points, its capacities' chances.</summary>
        public sealed record Makina(int Species, int Generation, int QualityBoost, IReadOnlyDictionary<int, double> Capacities);

        /// <summary>What two rides would give: the target colour and its chance, the others', the children, the capacities.</summary>
        public sealed record Preview(int TargetRide, double TargetPercent, IReadOnlyDictionary<int, double> Others,
                                     int Children, IReadOnlyDictionary<int, double> Capacities, bool MakinaFits,
                                     int Tokens, long Experience);

        public static Breeding.Ride? RideOf(Stable.StoredRide ride) => Breeding.RideByItem(ride.Gid);

        /// <summary>Whether two rides may breed, and if not why.</summary>
        public static Refusal Check(Stable.StoredRide? a, Stable.StoredRide? b)
        {
            if (a == null || b == null || a.Uid == b.Uid) return Refusal.NeedDistinctParent;
            var ra = RideOf(a);
            var rb = RideOf(b);
            if (ra == null || rb == null) return Refusal.Unknown;
            if (ra.Species != rb.Species) return Refusal.InterSpecies;
            if (Rides.IsMale(a.Effects) == Rides.IsMale(b.Effects)) return Refusal.SameSex;
            if (!Rides.IsFertile(a.Effects) || !Rides.IsFertile(b.Effects)) return Refusal.Sterile;
            if (!Rides.IsFecund(a.Effects) || !Rides.IsFecund(b.Effects)) return Refusal.MissingFillGauges;
            return Refusal.None;
        }

        /// <summary>The colour two colours cross into, as the client's RidesData lists it; 0 when they make none.</summary>
        public static int TargetOf(int rideA, int rideB)
        {
            var a = Breeding.RideOf(rideA);
            var b = Breeding.RideOf(rideB);
            int child = a?.Children?.FirstOrDefault(c => c.Parent == rideB).Child ?? 0;
            if (child == 0) child = b?.Children?.FirstOrDefault(c => c.Parent == rideA).Child ?? 0;
            return child;
        }

        /// <summary>A ride's family: its colour, then its parents' and grandparents', as its genealogy keeps them.</summary>
        public static List<int> FamilyOf(Stable.StoredRide ride)
        {
            var family = new List<int>();
            if (RideOf(ride) is { } own) family.Add(own.Id);
            family.AddRange(Rides.AncestorsOf(ride.Effects).Take(6));
            return family;
        }

        /// <summary>The highest generation in both families.</summary>
        public static int HighestGeneration(Stable.StoredRide a, Stable.StoredRide b)
            => FamilyOf(a).Concat(FamilyOf(b)).Select(id => Breeding.RideOf(id)?.Generation ?? 0).DefaultIfEmpty(0).Max();

        /// <summary>A mákina's effects, from its template; null for an item that is none.</summary>
        public static Makina? MakinaOf(int gid)
        {
            var effects = DatabaseManager.GetItemEffectsData(DatabaseManager.GetItemTemplatePossibleEffects(gid));
            var target = effects.FirstOrDefault(e => e.EffectId == BreedTarget);
            if (target == null) return null;
            var capacities = new Dictionary<int, double>();
            foreach (var e in effects)
                if (CapacityOpportunities.TryGetValue(e.EffectId, out int capacity))
                    capacities[capacity] = e.EffectId == ChameleonOpportunity ? 100 : e.Value;
            int boost = effects.FirstOrDefault(e => e.EffectId == QualityBoost)?.Value ?? 0;
            return new Makina(target.DiceSide, target.Value, boost, capacities);
        }

        /// <summary>What two rides would give, with a mákina or none.</summary>
        public static Preview PreviewOf(Stable.StoredRide a, Stable.StoredRide b, Makina? makina)
        {
            var ra = RideOf(a)!;
            var rb = RideOf(b)!;
            int target = TargetOf(ra.Id, rb.Id);
            var targetRide = Breeding.RideOf(target);
            bool fits = makina != null && targetRide != null && makina.Species == ra.Species && makina.Generation == targetRide.Generation;

            double chance = 0;
            if (targetRide != null)
            {
                chance = BaseTargetPercent + PercentPerLevel * (Rides.LevelOf(a.Effects) + Rides.LevelOf(b.Effects));
                if (fits) chance += makina!.QualityBoost;
                // And today's Almanax: «se añade un bonus del 20% a la probabilidad de obtener la
                // generación objetivo» (type 38).
                chance += Almanax.BonusPercent(Almanax.BonusType.TargetGeneration, Almanax.Where.Anywhere);
                chance = Math.Min(100, chance);
            }

            var weights = new Dictionary<int, double>();
            foreach (int id in FamilyOf(a).Concat(FamilyOf(b)))
            {
                var ride = Breeding.RideOf(id);
                if (ride == null || ride.Species != ra.Species) continue;
                weights[id] = (weights.TryGetValue(id, out double w) ? w : 0) + Math.Max(1, ride.GeneticWeight);
            }
            double total = weights.Values.Sum();
            var others = new Dictionary<int, double>();
            foreach (var (id, weight) in weights)
                others[id] = Math.Round((100 - chance) * weight / total, 2);

            int children = Rides.Has(a.Effects, Rides.Reproducer) || Rides.Has(b.Effects, Rides.Reproducer) ? 2 : 1;
            var capacities = fits ? makina!.Capacities : new Dictionary<int, double>();
            int tokens = targetRide != null && targetRide.Generation > HighestGeneration(a, b) ? targetRide.Tokens : 0;
            return new Preview(target, Math.Round(chance, 2), others, children, capacities, fits, tokens, ra.BreedingXp + rb.BreedingXp);
        }

        /// <summary>A colour drawn as the preview's chances say.</summary>
        public static int DrawColour(Preview preview, Random dice)
        {
            double roll = dice.NextDouble() * 100;
            if (preview.TargetRide != 0 && roll < preview.TargetPercent) return preview.TargetRide;
            double left = dice.NextDouble() * Math.Max(0.0001, preview.Others.Values.Sum());
            foreach (var (id, percent) in preview.Others.OrderBy(o => o.Key))
            {
                left -= percent;
                if (left <= 0) return id;
            }
            return preview.Others.Keys.OrderBy(k => k).FirstOrDefault(preview.TargetRide);
        }

        /// <summary>The capacity a mákina gives a child, drawn by its chances; 0 for none.</summary>
        public static int DrawCapacity(IReadOnlyDictionary<int, double> capacities, Random dice)
        {
            if (capacities.Count == 0) return 0;
            double total = capacities.Values.Sum();
            double roll = dice.NextDouble() * Math.Max(100, total);
            foreach (var (capacity, percent) in capacities.OrderBy(c => c.Key))
            {
                roll -= percent;
                if (roll < 0) return capacity;
            }
            return 0;
        }

        /// <summary>
        /// A child of two rides: born at level 1 with its family -- its parents' colours, then their
        /// parents' -- and the capacity its mákina may give.
        /// </summary>
        public static List<Equipment.ItemEffect> ChildOf(int childRide, Stable.StoredRide a, Stable.StoredRide b, int capacity, Random dice)
        {
            var ride = Breeding.RideOf(childRide)!;
            var effects = Rides.Fresh(ride.Item, dice);
            var family = new List<int> { RideOf(a)!.Id, RideOf(b)!.Id };
            family.AddRange(Rides.AncestorsOf(a.Effects).Take(2));
            family.AddRange(Rides.AncestorsOf(b.Effects).Take(2));
            effects = Rides.With(effects, Rides.ParentsEffect, 0, string.Join(",", family));
            if (capacity != 0) effects = Rides.With(effects, capacity, 0);
            return effects;
        }

        /// <summary>A parent after breeding: sterile, its gauges empty.</summary>
        public static List<Equipment.ItemEffect> AfterBreeding(IEnumerable<Equipment.ItemEffect> effects)
        {
            var list = Rides.Without(effects, Rides.Fertile);
            foreach (int gauge in Rides.GaugeEffect.Values) list = Rides.With(list, gauge, 0);
            return list;
        }

        // ─── Cloning ─────────────────────────────────────────────────────────────────────

        public static CloneRefusal CheckClone(Stable.StoredRide? a, Stable.StoredRide? b)
        {
            if (a == null || b == null || a.Uid == b.Uid) return CloneRefusal.RideNotFound;
            var ra = RideOf(a);
            var rb = RideOf(b);
            if (ra == null || rb == null) return CloneRefusal.Unknown;
            if (ra.Species != rb.Species) return CloneRefusal.InterSpecies;
            if (ra.Generation != rb.Generation) return CloneRefusal.DifferentGenerations;
            if (Rides.IsSenile(a.Effects) || Rides.IsSenile(b.Effects)) return CloneRefusal.Senile;
            return CloneRefusal.None;
        }

        /// <summary>The clone of the first: fertile, its gauges empty, its capacities gone, the rest kept.</summary>
        public static List<Equipment.ItemEffect> CloneOf(Stable.StoredRide original)
        {
            var effects = original.Effects.Where(e => !Rides.Capacities.Contains(e.Effect)).ToList();
            foreach (int gauge in Rides.GaugeEffect.Values) effects = Rides.With(effects, gauge, 0);
            return Rides.With(effects, Rides.Fertile, 0);
        }

        // ─── Extraction ──────────────────────────────────────────────────────────────────

        /// <summary>What extracting a ride gives: its species' resource, so many; null when it cannot be extracted.</summary>
        public static (int Item, int Quantity)? ExtractionOf(Stable.StoredRide ride)
        {
            var data = RideOf(ride);
            if (data == null || data.Generation < 2) return null;
            var species = Breeding.SpeciesOf(data.Species);
            if (species == null || species.ExtractionItem <= 0) return null;
            int quantity = Rides.IsSenile(ride.Effects) ? data.SenileExtraction : data.Extraction;
            return quantity > 0 ? (species.ExtractionItem, quantity) : null;
        }
    }
}

using System;
using System.Linq;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// Mating, cloning and extraction as the breeding window's tabs show them, on the client's data:
    /// the colours two rides cross into, the chances, the mákinas, the children and the rewards.
    /// </summary>
    [Collection("MapManager")]
    public class BreedingMatingTests
    {
        private const int Ebony = 3;          // "Dragopavo ébano", generation 3
        private const int Ivory = 16;         // it crosses with the ébano into ride 9, "ébano y marfil", generation 8
        private const int EbonyAndIvory = 9;

        private static bool HasData => Breeding.RideOf(Ebony) != null && Breeding.RideOf(Ivory) != null;

        /// <summary>A fecund ride of a colour: fertile, its three gauges full, of a sex and a level.</summary>
        private static Stable.StoredRide Fecund(int colour, bool male, int level, long uid)
        {
            var ride = Breeding.RideOf(colour)!;
            var effects = Rides.Fresh(ride.Item, new Random((int)uid));
            effects = Rides.With(effects, Rides.Gender, male ? Rides.MaleValue : 1);
            foreach (var gauge in Breeding.RideGauges) effects = Rides.With(effects, Rides.GaugeEffect[gauge.Id], gauge.Max);
            effects = Rides.WithExperience(ride.Item, effects, Breeding.ExperienceAt(level));
            return new Stable.StoredRide { Uid = uid, Gid = ride.Item, Effects = effects };
        }

        /// <summary>The ébano and the ivory cross into the "ébano y marfil", whichever lists the other.</summary>
        [Fact]
        public void Two_colours_cross_into_their_child()
        {
            if (!HasData) return;
            Assert.Equal(EbonyAndIvory, Mating.TargetOf(Ebony, Ivory));
            Assert.Equal(EbonyAndIvory, Mating.TargetOf(Ivory, Ebony));
            Assert.Equal(0, Mating.TargetOf(Ebony, Ebony));
        }

        /// <summary>
        /// The target's chance is 30 % and 0.15 % a level of each parent: 60 % for two of level 100;
        /// the rest goes to the parents' colours, by their genetic weight. A fitting optimákina adds 10.
        /// </summary>
        [Fact]
        public void The_target_s_chance_follows_the_parents_levels()
        {
            if (!HasData) return;
            var father = Fecund(Ebony, true, 100, 1);
            var mother = Fecund(Ivory, false, 100, 2);
            var preview = Mating.PreviewOf(father, mother, null);
            Assert.Equal(EbonyAndIvory, preview.TargetRide);
            Assert.Equal(60, preview.TargetPercent);
            Assert.Equal(40, preview.Others.Values.Sum(), 1);
            Assert.Equal(new[] { Ebony, Ivory }, preview.Others.Keys.OrderBy(k => k));
            Assert.Equal(1, preview.Children);
            Assert.Equal(Breeding.RideOf(EbonyAndIvory)!.Tokens, preview.Tokens);       // generation 8, above both families
            Assert.Equal(Breeding.RideOf(Ebony)!.BreedingXp + Breeding.RideOf(Ivory)!.BreedingXp, preview.Experience);

            var optimakina = new Mating.Makina(1, Breeding.RideOf(EbonyAndIvory)!.Generation, 10, new System.Collections.Generic.Dictionary<int, double>());
            Assert.Equal(70, Mating.PreviewOf(father, mother, optimakina).TargetPercent);
            var wrong = optimakina with { Generation = 2 };
            Assert.False(Mating.PreviewOf(father, mother, wrong).MakinaFits);
        }

        /// <summary>The mákinas' effects read off their templates: the optimákina's 10 points, the animákina's capacities.</summary>
        [Fact]
        public void The_makinas_say_what_they_do()
        {
            if (!HasData) return;
            var optimakina = Mating.MakinaOf(33459);                   // "Optimákina Dragopavo de generación 8"
            if (optimakina == null) return;
            Assert.Equal((1, 8, 10), (optimakina.Species, optimakina.Generation, optimakina.QualityBoost));
            var animakina = Mating.MakinaOf(33324)!;                   // "Animákina Dragopavo de generación 2"
            Assert.Equal(5, animakina.Capacities[Rides.Reproducer]);
            Assert.Equal(14, animakina.Capacities[Rides.Wise]);
            Assert.Equal(100, Mating.MakinaOf(33321)!.Capacities[Rides.Chameleon]);   // the kromákina
            Assert.Null(Mating.MakinaOf(33020));
        }

        /// <summary>A male and a female of a family, fertile and with full gauges; the reproducer gives two children.</summary>
        [Fact]
        public void Who_may_breed()
        {
            if (!HasData) return;
            var father = Fecund(Ebony, true, 1, 1);
            var mother = Fecund(Ivory, false, 1, 2);
            Assert.Equal(Mating.Refusal.None, Mating.Check(father, mother));
            Assert.Equal(Mating.Refusal.SameSex, Mating.Check(father, Fecund(Ivory, true, 1, 3)));
            Assert.Equal(Mating.Refusal.NeedDistinctParent, Mating.Check(father, father));
            var tired = new Stable.StoredRide { Uid = 4, Gid = mother.Gid, Effects = Rides.With(mother.Effects, Rides.Love, 10) };
            Assert.Equal(Mating.Refusal.MissingFillGauges, Mating.Check(father, tired));
            var sterile = new Stable.StoredRide { Uid = 5, Gid = mother.Gid, Effects = Mating.AfterBreeding(mother.Effects) };
            Assert.Equal(Mating.Refusal.Sterile, Mating.Check(father, sterile));

            var reproducer = new Stable.StoredRide { Uid = 6, Gid = father.Gid, Effects = Rides.With(father.Effects, Rides.Reproducer, 0) };
            Assert.Equal(2, Mating.PreviewOf(reproducer, mother, null).Children);
        }

        /// <summary>A child is born at level 1 with its family: its parents' colours first; a kromákina makes it chameleon.</summary>
        [Fact]
        public void A_child_is_born_with_its_family()
        {
            if (!HasData) return;
            var father = Fecund(Ebony, true, 50, 1);
            var mother = Fecund(Ivory, false, 50, 2);
            var child = Mating.ChildOf(EbonyAndIvory, father, mother, Rides.Chameleon, new Random(5));
            Assert.Equal(1, Rides.LevelOf(child));
            Assert.Equal(new[] { Ebony, Ivory }, Rides.AncestorsOf(child));
            Assert.True(Rides.Has(child, Rides.Chameleon));
            Assert.True(Rides.IsFertile(child));

            var preview = Mating.PreviewOf(father, mother, null);
            var colours = Enumerable.Range(0, 400).Select(i => Mating.DrawColour(preview, new Random(i))).ToList();
            Assert.All(colours, c => Assert.Contains(c, new[] { Ebony, Ivory, EbonyAndIvory }));
            Assert.InRange(colours.Count(c => c == EbonyAndIvory), 160, 260);           // 45 %
        }

        /// <summary>Cloning asks for one family, one generation and no senile one; the clone is fertile, its gauges and capacities gone.</summary>
        [Fact]
        public void Cloning_keeps_the_original()
        {
            if (!HasData) return;
            var a = Fecund(Ebony, true, 30, 1);
            var b = Fecund(Ebony, false, 10, 2);
            Assert.Equal(Mating.CloneRefusal.None, Mating.CheckClone(a, b));
            Assert.Equal(Mating.CloneRefusal.DifferentGenerations, Mating.CheckClone(a, Fecund(EbonyAndIvory, false, 1, 3)));
            var sterile = new Stable.StoredRide { Uid = 1, Gid = a.Gid, Effects = Rides.With(Mating.AfterBreeding(a.Effects), Rides.Wise, 0) };
            var clone = Mating.CloneOf(sterile);
            Assert.True(Rides.IsFertile(clone));
            Assert.False(Rides.Has(clone, Rides.Wise));
            Assert.Equal(0, Rides.GaugeOf(clone, Rides.Love));
            Assert.Equal(30, Rides.LevelOf(clone));
        }

        /// <summary>Extraction: generation 2 or more, its species' resource as many as its data says; a senile one gives one.</summary>
        [Fact]
        public void Extraction_gives_the_species_resource()
        {
            if (!HasData) return;
            var ebony = Fecund(Ebony, true, 1, 1);
            var neuron = Breeding.SpeciesOf(1)!.ExtractionItem;
            Assert.Equal((neuron, Breeding.RideOf(Ebony)!.Extraction), Mating.ExtractionOf(ebony));
            var senile = new Stable.StoredRide { Uid = 2, Gid = ebony.Gid, Effects = Rides.With(ebony.Effects, Rides.Senile, 0) };
            Assert.Equal((neuron, Breeding.RideOf(Ebony)!.SenileExtraction), Mating.ExtractionOf(senile));
            var firstGeneration = Breeding.AllRides.First(r => r.Generation == 1 && r.Item > 0);
            Assert.Null(Mating.ExtractionOf(new Stable.StoredRide { Gid = firstGeneration.Item, Effects = ebony.Effects }));
        }

        /// <summary>The preview's shape: f1 { f4 the children, f5 the colours, the target's first with its f1 }.</summary>
        [Fact]
        public void The_preview_message()
        {
            if (!HasData) return;
            var preview = Mating.PreviewOf(Fecund(Ebony, true, 100, 1), Fecund(Ivory, false, 100, 2), null);
            var hrq = ProtoMessage.Parse(ProtoMessage.Parse(BreedingProtocol.BuildMatingPreview(preview, 0)).Fields.Single(f => f.FieldNumber == 1).BytesValue).Fields;
            Assert.Equal(1, (int)hrq.Single(f => f.FieldNumber == 4).VarIntValue);
            var colours = hrq.Where(f => f.FieldNumber == 5).Select(f => ProtoMessage.Parse(f.BytesValue).Fields).ToList();
            Assert.Equal(3, colours.Count);
            Assert.Equal(1, (int)colours[0].Single(f => f.FieldNumber == 1).VarIntValue);
            Assert.Equal(EbonyAndIvory, (int)colours[0].Single(f => f.FieldNumber == 2).VarIntValue);
            Assert.Equal(60f, BitConverter.Int32BitsToSingle((int)colours[0].Single(f => f.FieldNumber == 3).Fixed32Value));
            Assert.NotNull(hrq.Single(f => f.FieldNumber == 1));                         // the breeder's experience

            var hsu = ProtoMessage.Parse(BreedingProtocol.BuildBreedFailed(Mating.Refusal.StableFull)).Fields;
            Assert.Equal(0, (int)hsu.Single(f => f.FieldNumber == 1).VarIntValue);      // present, at 0
        }
    }
}

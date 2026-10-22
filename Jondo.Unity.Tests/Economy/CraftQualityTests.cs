using System;
using System.IO;
using System.Linq;
using Jondo.Unity.Launcher;
using Jondo.Unity.Server.Managers;
using Xunit;

namespace Jondo.Unity.Tests.Economy
{
    /// <summary>
    /// The Almanax's «calidad de los objetos fabricados»: each rolled characteristic comes that
    /// percent of the way from its roll to its best.
    /// </summary>
    [Collection("forgemagic")]
    public class CraftQualityTests
    {
        private static readonly Forgemagic.Template Ring = new()
        {
            Gid = 990_011, Level = 100, Type = 9,
            Lines = new[] { new Forgemagic.TemplateLine(118, 40, 60, 0) },
        };

        [Fact]
        public void A_better_quality_brings_each_roll_towards_its_best()
        {
            if (!File.Exists(Paths.EffectWeightsJson)) return;
            if (Forgemagic.WeightOf(118) == 0) Forgemagic.Initialize();
            for (int seed = 0; seed < 20; seed++)
            {
                long plain = Forgemagic.Roll(Ring, new Random(seed)).Single().Value;
                long better = Forgemagic.Roll(Ring, new Random(seed), 20).Single().Value;
                Assert.Equal(plain + (long)Math.Round((60 - plain) * 0.2, MidpointRounding.AwayFromZero), better);
                Assert.Equal(60L, Forgemagic.Roll(Ring, new Random(seed), 100).Single().Value);
            }
        }
    }
}

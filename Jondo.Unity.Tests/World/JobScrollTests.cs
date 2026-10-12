using System.Linq;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// The job scrolls: what each one trains, and how the item travels with its job in it, against the
    /// baker's scroll the house chest's capture puts in the bag.
    /// </summary>
    public class JobScrollTests
    {
        private static string Hex(byte[] bytes) => string.Concat(bytes.Select(b => b.ToString("x2")));

        private static DatabaseManager.ItemEffectData E(int id, int dice = 0, int side = 0, int value = 0)
            => new() { EffectId = id, DiceNum = dice, DiceSide = side, Value = value };

        /// <summary>
        /// The job is the second number and the points the third: the farmer's scroll is a hundred
        /// points of job 28. A box that gives an item too is a box's business.
        /// </summary>
        [Fact]
        public void A_job_scroll_trains_its_job()
        {
            Assert.Equal(new[] { (28, 100L) }, ExperienceItemHandler.JobScrollOf(new[] { E(614, 0, 28, 100) }));
            Assert.Equal(new[] { (2, 100L) }, ExperienceItemHandler.JobScrollOf(new[] { E(614, 0, 2, 100), E(981) }));
            Assert.Null(ExperienceItemHandler.JobScrollOf(new[] { E(983), E(614, 0, 28, 190), E(222, 1, 0, 110) }));
            Assert.Null(ExperienceItemHandler.JobScrollOf(new[] { E(605, 200) }));
        }

        /// <summary>
        /// Made from its template, the scroll keeps its three numbers -- [614, 100, 0, 28] -- and arrives
        /// in the bag byte for byte as the captured one: «f6 { f1: 100, f3: 28 }». With one number, as
        /// it was, the job was lost.
        /// </summary>
        [Fact]
        public void The_scroll_travels_with_its_job()
        {
            var baker = new HavenBagStore.StoredItem { Uid = 576937855, Gid = 878, Quantity = 1, Effects = "[[614,100,0,28]]" };

            Assert.Equal("1a1a083f2a1608ee06120932040864181c58e604180120ffbe8d9302",
                         Hex(ConnectionProtocol.BuildItemArrived(3, baker)));
        }

        /// <summary>
        /// Which compound effects keep their three numbers: the ones learnt as f6 and not rolled. 193,
        /// rolled, does not -- it keeps its item and the one it gives.
        /// </summary>
        [Fact]
        public void Only_compound_effects_travel_whole()
        {
            Assert.True(EffectFields.TravelsWhole(EffectFields.AsDice, rolled: false));
            Assert.False(EffectFields.TravelsWhole(EffectFields.AsDice, rolled: true));
            Assert.False(EffectFields.TravelsWhole(EffectFields.AsNumber, rolled: false));
            Assert.False(EffectFields.TravelsWhole(EffectFields.NoValue, rolled: false));
        }
    }
}

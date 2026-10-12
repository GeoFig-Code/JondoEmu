using System.Linq;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>The attitude scrolls and potions and the experience scrolls: what counts as one.</summary>
    public class LearningItemTests
    {
        private static DatabaseManager.ItemEffectData Effect(int id, int dice = 0, int side = 0, int value = 0)
            => new() { EffectId = id, DiceNum = dice, DiceSide = side, Value = value };

        /// <summary>
        /// An attitude scroll (type 173) teaches its first number, an attitude potion (2823) its
        /// third; the flute, a tool whose effect 10 is worn, and a Dofus teach nothing.
        /// </summary>
        [Fact]
        public void The_attitude_an_item_teaches()
        {
            Assert.Equal(8, AttitudeItemHandler.AttitudeOf(173, new[] { Effect(10, 8) }.ToList()));
            Assert.Equal(15, AttitudeItemHandler.AttitudeOf(173, new[] { Effect(10, 15), Effect(983) }.ToList()));
            Assert.Equal(199, AttitudeItemHandler.AttitudeOf(214, new[] { Effect(2823, value: 199), Effect(983, value: 180) }.ToList()));

            Assert.Equal(0, AttitudeItemHandler.AttitudeOf(20, new[] { Effect(10, 7), Effect(983) }.ToList()));
            Assert.Equal(0, AttitudeItemHandler.AttitudeOf(23, new[] { Effect(138, 80), Effect(10, 186) }.ToList()));
        }

        /// <summary>The experience scrolls, and the Gatrool that gives energy besides; not one that does more.</summary>
        [Fact]
        public void The_experience_scrolls()
        {
            Assert.True(ExperienceItemHandler.GivesExperience(new[] { Effect(605, 2000) }.ToList()));
            Assert.True(ExperienceItemHandler.GivesExperience(new[] { Effect(139, 10), Effect(605, 10) }.ToList()));

            Assert.False(ExperienceItemHandler.GivesExperience(new[] { Effect(605, 10), Effect(193, 1) }.ToList()));
            Assert.False(ExperienceItemHandler.GivesExperience(new[] { Effect(2820, value: 500) }.ToList()));
        }
    }
}

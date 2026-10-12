using System;
using System.Collections.Generic;
using System.Linq;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.World.Fights;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// The items that give back life or energy: what counts as one, how much they give, and the
    /// refusal the real server answers with a full gauge.
    /// </summary>
    public class RecoveryItemTests
    {
        private static DatabaseManager.ItemEffectData Effect(int id, int dice = 0, int side = 0)
            => new() { EffectId = id, DiceNum = dice, DiceSide = side };

        private static RecoveryItemHandler.Recovery Of(params DatabaseManager.ItemEffectData[] effects)
            => RecoveryItemHandler.RecoveryOf(effects.ToList());

        /// <summary>
        /// Life, energy or both, bound or not, is one; a bread that also raises a characteristic for
        /// good is a scroll's business, and one that gives an item somebody else's.
        /// </summary>
        [Fact]
        public void Only_what_just_gives_back_life_or_energy()
        {
            Assert.NotNull(Of(Effect(110, 30))?.Life);
            Assert.NotNull(Of(Effect(139, 390))?.Energy);
            var both = Of(Effect(139, 16, 30), Effect(110, 16, 30));
            Assert.NotNull(both?.Life);
            Assert.NotNull(both?.Energy);
            Assert.NotNull(Of(Effect(110, 10), Effect(981)));

            Assert.Null(Of(Effect(110, 20), Effect(610, 1)));
            Assert.Null(Of(Effect(110, 130), Effect(209, 0, 1)));
            Assert.Null(Of(Effect(600, 0, 0)));
        }

        /// <summary>One figure, or a roll between the two of «#1 a #2».</summary>
        [Fact]
        public void The_amount_is_the_figure_or_a_roll_between_the_two()
        {
            var dice = new Random(7);
            Assert.Equal(30, RecoveryItemHandler.Roll(Effect(110, 30), dice));
            var rolls = Enumerable.Range(0, 400).Select(_ => RecoveryItemHandler.Roll(Effect(110, 16, 30), dice)).ToList();
            Assert.Equal(16, rolls.Min());
            Assert.Equal(30, rolls.Max());
        }

        /// <summary>Never more than what is missing, and nothing at all with both gauges full.</summary>
        [Fact]
        public void It_gives_up_to_what_is_missing()
        {
            var dice = new Random(1);
            var bread = Of(Effect(110, 30));
            Assert.Equal((30, 0), RecoveryItemHandler.WouldGive(bread, 100, DefeatPenalty.MaxEnergy, dice));
            Assert.Equal((10, 0), RecoveryItemHandler.WouldGive(bread, 10, DefeatPenalty.MaxEnergy, dice));
            Assert.Equal((0, 0), RecoveryItemHandler.WouldGive(bread, 0, 0, dice));

            var energy = Of(Effect(139, 390));
            Assert.Equal((0, 10), RecoveryItemHandler.WouldGive(energy, 50, DefeatPenalty.MaxEnergy - 10, dice));
            Assert.Equal((0, 0), RecoveryItemHandler.WouldGive(energy, 50, DefeatPenalty.MaxEnergy, dice));

            var beer = Of(Effect(139, 20), Effect(110, 20));
            Assert.Equal((20, 0), RecoveryItemHandler.WouldGive(beer, 50, DefeatPenalty.MaxEnergy, dice));
        }

        /// <summary>«Los puntos de energía ya están al máximo.», byte for byte «ruta muy larga».</summary>
        [Fact]
        public void The_full_gauge_refusal_is_the_captures()
        {
            Assert.Equal("10fd01", Convert.ToHexString(
                ConnectionProtocol.BuildSystemMessage(InfoMessages.EnergyAlreadyFull)).ToLowerInvariant());
        }

        /// <summary>
        /// Energy comes back a point a second from the moment it was written, never past the gauge,
        /// and not before a whole second has gone.
        /// </summary>
        [Fact]
        public void Energy_comes_back_a_point_a_second()
        {
            var then = new DateTime(2026, 10, 12, 21, 0, 0, DateTimeKind.Utc);
            var stored = new Energy.Stored(8000, then);

            Assert.Equal(8000, Energy.At(stored, then.AddMilliseconds(900)));
            Assert.Equal(8010, Energy.At(stored, then.AddSeconds(10)));
            Assert.Equal(DefeatPenalty.MaxEnergy, Energy.At(stored, then.AddHours(1)));
            Assert.Equal(8000, Energy.At(stored, then.AddSeconds(-5)));
        }

        /// <summary>
        /// What is healed comes off the life missing, and the regeneration counts on from the heal:
        /// the kub's hitPointLoss is read at the counter's start, which the heal moves to now.
        /// </summary>
        [Fact]
        public void A_heal_leaves_less_missing_from_now_on()
        {
            const long character = 7_401;
            var then = new DateTime(2026, 10, 12, 20, 0, 0, DateTimeKind.Utc);
            RestingLife.Set(character, 500, then);

            var now = then.AddSeconds(10);   // twenty ticks of half a second
            int missing = RestingLife.MissingAt(character, now);
            Assert.Equal(480, missing);

            RestingLife.Set(character, missing - 100, now);
            Assert.Equal(380, RestingLife.MissingAt(character, now));
            Assert.Equal(370, RestingLife.MissingAt(character, now.AddSeconds(5)));
            RestingLife.Clear(character);
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using Jondo.Unity.Server.Managers;
using Xunit;
using Line = Jondo.Unity.Server.Managers.WeaponHit.Line;

namespace Jondo.Unity.Tests.Combat
{
    /// <summary>
    /// Which of a weapon's lines a hit deals, against the captured hits: lines with the same dice
    /// are alternatives -- damage before steal, then the wielder's best element --, and lines
    /// with dice of their own all hit.
    /// </summary>
    public class WeaponHitTests
    {
        private const int Neutral = 0, Earth = 1, Fire = 2, Water = 3, Air = 4;

        private static System.Func<int, int> Points(int earth, int fire, int water, int air)
            => element => element switch
            {
                Neutral or Earth => earth,
                Fire => fire,
                Water => water,
                _ => air,
            };

        private static int[] Hit(IReadOnlyList<Line> lines, System.Func<int, int> points)
            => WeaponHit.LinesThatHit(lines, points).Select(l => l.Effect).ToArray();

        /// <summary>Arco de vueloceronte: 11-15 in all four elements; 1,382 strength fires earth.</summary>
        [Fact]
        public void The_bow_hits_in_its_wielders_best_element()
        {
            var bow = new[] { new Line(97, Earth, 11, 15), new Line(98, Air, 11, 15),
                              new Line(96, Water, 11, 15), new Line(99, Fire, 11, 15) };

            Assert.Equal(new[] { 97 }, Hit(bow, Points(1382, 345, 100, 100)));
            Assert.Equal(new[] { 99 }, Hit(bow, Points(100, 900, 100, 100)));
        }

        /// <summary>
        /// With the four characteristics equal the client draws the bow's damage on air, so the
        /// tie goes to the later element.
        /// </summary>
        [Fact]
        public void A_tie_goes_to_the_later_element()
        {
            var bow = new[] { new Line(97, Earth, 11, 15), new Line(98, Air, 11, 15),
                              new Line(96, Water, 11, 15), new Line(99, Fire, 11, 15) };

            Assert.Equal(new[] { 98 }, Hit(bow, Points(1415, 1415, 1415, 1415)));
        }

        /// <summary>
        /// Garras de la Despedazadora: 11-13 water damage and fire steal; the water damage, though
        /// its wielder has more intelligence than chance. Lavacha: earth damage over earth steal.
        /// </summary>
        [Fact]
        public void A_damage_line_goes_before_a_steal_with_the_same_dice()
        {
            var claws = new[] { new Line(96, Water, 11, 13), new Line(94, Fire, 11, 13) };
            var lavacha = new[] { new Line(92, Earth, 12, 16), new Line(97, Earth, 12, 16) };

            Assert.Equal(new[] { 96 }, Hit(claws, Points(961, 519, 325, 100)));
            Assert.Equal(new[] { 97 }, Hit(lavacha, Points(500, 100, 100, 100)));
        }

        /// <summary>Cocobur: its best-element blow, and one of its two 6-9 steals, the water one.</summary>
        [Fact]
        public void Steals_alone_go_by_the_best_element()
        {
            var cocobur = new[] { new Line(2822, -1, 41, 60), new Line(93, Air, 6, 9), new Line(91, Water, 6, 9) };

            Assert.Equal(new[] { 2822, 91 }, Hit(cocobur, Points(961, 449, 257, 100)));
        }

        /// <summary>Espada diablina: 45-50 fire damage and 8-10 fire steal, both, every time.</summary>
        [Fact]
        public void Lines_with_their_own_dice_all_hit()
        {
            var sword = new[] { new Line(99, Fire, 45, 50), new Line(94, Fire, 8, 10) };

            Assert.Equal(new[] { 99, 94 }, Hit(sword, Points(100, 600, 100, 100)));
        }
    }
}

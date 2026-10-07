using System.Collections.Generic;
using Jondo.Unity.World.Maps;
using Xunit;

namespace Jondo.Unity.Tests.Combat
{
    /// <summary>
    /// A pull stops on the centre when it is free, and does not overshoot.
    /// </summary>
    /// <remarks>
    /// Without this the Rogue's Imantación —which pulls his bombs SIX cells— crossed the point
    /// and left them on the other side. And since the spell pulls twice, the second brought them back:
    /// in the log the dance is seen, bomb -5 from 272 to 185 and from 185 back to 272.
    /// It then stopped a cell short of the centre, which left Congregación's Puch diagonal to
    /// the Yopuka; the captures land on it (Cruce, Colapso, Cencerro, Timón...).
    /// </remarks>
    public class PullTests
    {
        private static int Desde(int celda, int dx, int dy)
        {
            var (x, y) = MapGeometry.CellToPoint(celda);
            return MapGeometry.PointToCell(x + dx, y + dy);
        }

        [Fact]
        public void A_long_pull_stops_on_the_centre()
        {
            int centro = 270;
            int quienTira = Desde(centro, 0, -3);
            int bomba = Desde(centro, 0, 4);

            var r = Zone.Push(centro, quienTira, bomba, casillas: -6,
                              pisables: null, ocupadas: null);

            Assert.Equal(centro, r.ToCell);
            Assert.Equal(0, r.BlockedCells);
        }

        /// <summary>Somebody on the centre stops the pull beside him.</summary>
        [Fact]
        public void A_pull_stops_beside_whoever_stands_on_the_centre()
        {
            int centro = 270;
            int bomba = Desde(centro, 0, 4);

            var r = Zone.Push(centro, centro, bomba, casillas: -6,
                              pisables: null, ocupadas: new HashSet<int> { centro });

            Assert.Equal(Desde(centro, 0, 1), r.ToCell);
            Assert.Equal(Zone.PushStop.Fighter, r.Stop);
        }

        /// <summary>
        /// Off the eight lines a pull walks the axis that separates the two the most: Amenaza on a
        /// Puch two cells along one axis and one along the other leaves it in front of the Yopuka,
        /// not past him on the diagonal.
        /// </summary>
        [Fact]
        public void Off_the_lines_a_pull_walks_the_longer_axis()
        {
            int yopuka = 300;
            int puch = Desde(yopuka, 2, 1);

            var r = Zone.Push(puch, yopuka, puch, casillas: -2,
                              pisables: null, ocupadas: new HashSet<int> { yopuka });

            Assert.Equal(Desde(yopuka, 0, 1), r.ToCell);
        }

        /// <summary>On a line, diagonals included, the push keeps to it.</summary>
        [Theory]
        [InlineData(2, 0, 1, 0)]
        [InlineData(0, -3, 0, -1)]
        [InlineData(2, 2, 1, 1)]
        [InlineData(-1, 3, 0, 1)]
        [InlineData(-3, 1, -1, 0)]
        public void The_direction_of_a_displacement(int dx, int dy, int ux, int uy)
        {
            Assert.Equal((ux, uy), Zone.DisplacementDirection(300, Desde(300, dx, dy)));
        }

        [Fact]
        public void Un_tiron_corto_llega_donde_le_toca()
        {
            int centro = 270;
            int bomba = Desde(centro, 0, 5);

            var r = Zone.Push(centro, centro, bomba, casillas: -2,
                              pisables: null, ocupadas: null);

            Assert.Equal(Desde(centro, 0, 3), r.ToCell);
        }

        [Fact]
        public void Un_empujon_no_mira_el_centro_para_nada()
        {
            int centro = 270;
            int bicho = Desde(centro, 0, 1);

            var r = Zone.Push(centro, centro, bicho, casillas: 3,
                              pisables: null, ocupadas: null);

            Assert.Equal(Desde(centro, 0, 4), r.ToCell);
        }
    }
}

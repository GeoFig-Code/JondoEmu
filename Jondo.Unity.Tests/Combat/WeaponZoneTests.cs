using System.Linq;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.World.Maps;
using Xunit;

namespace Jondo.Unity.Tests.Combat
{
    /// <summary>
    /// A weapon hits in its type's zone, read from the client's item types as it reads them: a
    /// hammer's cross, a staff's bar, a shovel's cone, a scythe's half circle, a lance's line.
    /// </summary>
    public class WeaponZoneTests
    {
        [Theory]
        [InlineData("P", 'P', 0, 0, 0, 0)]
        [InlineData("X1,0,10,1", 'X', 1, 0, 10, 1)]
        [InlineData("T1,10,1", 'T', 1, 0, 10, 1)]
        [InlineData("V1,10,2", 'V', 1, 0, 10, 2)]
        [InlineData("U1,10,1", 'U', 1, 0, 10, 1)]
        [InlineData("L3,10,3", 'L', 3, 0, 10, 3)]
        [InlineData("C2,1", 'C', 2, 1, 0, 0)]
        public void A_raw_zone_reads_as_the_client_reads_it(string raw, char shape, int size, int min,
                                                            int step, int steps)
        {
            Assert.Equal(new WeaponZones.WeaponZone(shape, size, min, step, steps), WeaponZones.Parse(raw));
        }

        /// <summary>The types the client names: bows hit a cell, hammers a cross, lances a line.</summary>
        [Fact]
        public void The_weapon_types_carry_their_zones()
        {
            Assert.Equal('P', WeaponZones.Of(2).Shape);
            Assert.Equal(new WeaponZones.WeaponZone('X', 1, 0, 10, 1), WeaponZones.Of(7));
            Assert.Equal(new WeaponZones.WeaponZone('L', 3, 0, 10, 3), WeaponZones.Of(271));
            Assert.Equal(WeaponZones.WeaponZone.Point, WeaponZones.Of(99999));
        }

        /// <summary>
        /// A hammer swung at the cell beside its wielder takes in his own cell -- the cross has
        /// him in it --, and the Martillo Martirio capture shows he is not hit: GolpeDelArma
        /// leaves the wielder out of whoever stands in it.
        /// </summary>
        [Fact]
        public void A_hammer_cross_takes_in_its_wielders_cell()
        {
            var (x, y) = MapGeometry.CellToPoint(300);
            int aimed = MapGeometry.PointToCell(x + 1, y);
            var zone = WeaponZones.Of(7);

            var cells = Zone.Casillas(zone.Shape, zone.Size, 300, aimed, zone.MinSize);

            Assert.Equal(5, cells.Count);
            Assert.Contains(300, cells);
            Assert.Contains(aimed, cells);
        }
    }
}

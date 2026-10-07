using System.Collections.Generic;
using System.Linq;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Managers;
using Xunit;

namespace Jondo.Unity.Tests.Economy
{
    /// <summary>
    /// A weapon's damage is a range, also on an item made straight from its template: the Arco de
    /// vueloceronte (19986) given by a command came out as four lone 15s, and the client drew
    /// three of its lines as "0 de daños".
    /// </summary>
    public class WeaponDamageRangeTests
    {
        private const int ArcoDeVueloceronte = 19986;

        [Fact]
        public void An_item_from_its_template_keeps_its_weapon_damage_range()
        {
            Assert.True(DatabaseManager.TryGetItemTemplateEffects(ArcoDeVueloceronte, out string effects));
            var lines = Equipment.ParseEffects(effects);

            foreach (int element in new[] { 96, 97, 98, 99 })
            {
                var line = Assert.Single(lines, e => e.Effect == element);
                Assert.Equal((0L, 11L, 15L), (line.Value, line.DiceNum, line.DiceSide));
            }

            // The rest still rolls to its top: 350 vitality.
            Assert.Equal(350, Assert.Single(lines, e => e.Effect == 125).Value);
        }

        [Fact]
        public void A_lone_weapon_damage_gets_its_range_back()
        {
            var ranges = new Dictionary<int, (long Min, long Max)> { [97] = (11, 15), [98] = (11, 15) };

            string repaired = DatabaseManager.WithWeaponDamageRanges(
                "[[97,15,0,0],[98,0,11,15],[125,350,0,0],[988,0,0,0,\"Tester\"]]", ranges);

            Assert.Equal("[[97,0,11,15],[98,0,11,15],[125,350,0,0],[988,0,0,0,\"Tester\"]]", repaired);
        }

        [Fact]
        public void Nothing_to_repair_gives_nothing()
        {
            var ranges = new Dictionary<int, (long Min, long Max)> { [97] = (11, 15) };

            Assert.Null(DatabaseManager.WithWeaponDamageRanges("[[97,0,11,15],[125,350,0,0]]", ranges));
            Assert.Null(DatabaseManager.WithWeaponDamageRanges("[[125,350,0,0]]", ranges));
            Assert.Null(DatabaseManager.WithWeaponDamageRanges("not json", ranges));
        }
    }
}

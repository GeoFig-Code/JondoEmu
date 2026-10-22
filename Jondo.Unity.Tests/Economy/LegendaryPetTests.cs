using System.Linq;
using Jondo.Unity.Server.Managers;
using Xunit;

namespace Jondo.Unity.Tests.Economy
{
    /// <summary>
    /// The legendary pets as the client's data makes them: «Reflejo de bebemot» gives the status,
    /// a «Croqueta impregnada» the power -- its category (2864) and its spell (1175) -- in place of
    /// the one the pet had, and only a legendary pet takes one.
    /// </summary>
    public class LegendaryPetTests
    {
        private const int Reflection = 20968, Scarafeuille = 30122, Thomondor = 30123;
        private const int Pet = 1711, Hat = 629;

        private static Equipment.Item Worn(int template, params Equipment.ItemEffect[] effects)
        {
            var item = new Equipment.Item { Uid = 1, Template = template, Position = LegendaryPets.PetSlot };
            item.Effects.AddRange(effects);
            return item;
        }

        [Fact]
        public void The_foods_are_the_client_s()
        {
            Assert.True(LegendaryPets.FoodOf(Reflection)!.GivesStatus);
            var croquette = LegendaryPets.FoodOf(Scarafeuille)!;
            Assert.Equal((30, 29819, 1), (croquette.Category, croquette.Spell, croquette.Grade));
            Assert.Null(LegendaryPets.FoodOf(Hat));
        }

        [Fact]
        public void Only_a_worn_pet_is_fed_and_only_a_legendary_one_takes_a_power()
        {
            var status = LegendaryPets.FoodOf(Reflection)!;
            var power = LegendaryPets.FoodOf(Scarafeuille)!;

            Assert.Equal("no pet worn", LegendaryPets.WhyNotFeed(null, status));
            Assert.Equal("no pet worn", LegendaryPets.WhyNotFeed(Worn(Hat), status));
            Assert.Equal("the pet is not legendary", LegendaryPets.WhyNotFeed(Worn(Pet), power));
            Assert.Null(LegendaryPets.WhyNotFeed(Worn(Pet), status));

            var legendary = Worn(Pet, new Equipment.ItemEffect(LegendaryPets.LegendaryStatus, 0, 0, 0));
            Assert.Equal("the pet is legendary already", LegendaryPets.WhyNotFeed(legendary, status));
            Assert.Null(LegendaryPets.WhyNotFeed(legendary, power));
        }

        [Fact]
        public void A_new_power_replaces_the_old_one_and_is_lent_in_the_fight()
        {
            var pet = Worn(Pet);
            Assert.Equal((0, 0), LegendaryPets.PowerOf(pet));

            var fed = LegendaryPets.Fed(pet.Effects, LegendaryPets.FoodOf(Reflection)!);
            fed = LegendaryPets.Fed(fed, LegendaryPets.FoodOf(Scarafeuille)!);
            fed = LegendaryPets.Fed(fed, LegendaryPets.FoodOf(Thomondor)!);
            pet.Effects.AddRange(fed);

            Assert.Single(pet.Effects, e => e.Effect == LegendaryPets.PowerSpell);
            Assert.Equal(31, pet.Effects.Single(e => e.Effect == LegendaryPets.PowerCategory).Value);
            Assert.Equal((29820, 1), LegendaryPets.PowerOf(pet));
        }
    }
}

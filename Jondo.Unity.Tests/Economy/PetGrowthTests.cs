using System;
using System.Linq;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Xunit;

namespace Jondo.Unity.Tests.Economy
{
    /// <summary>
    /// The pets' growth with the client's own reckoning: a pet comes in at level 0 with its first
    /// values, grows level by level to its template's at 100, is fed from its «Alimentar» window
    /// (itw), and only at level 100 does the bebemot's reflection make it legendary.
    /// </summary>
    [Collection("forgemagic")]
    public class PetGrowthTests
    {
        private const int Miaumiau = 1728, Power = 138, Escape = 752, EnrichedBiscuit = 20125, Reflection = 20968;
        private const long Character = 8_940_100_001, Account = 8_940_000_001;

        private static long Value(System.Collections.Generic.IReadOnlyList<Equipment.ItemEffect> effects, int id)
            => effects.Where(e => e.Effect == id).Select(e => e.Value).FirstOrDefault();

        [Fact]
        public void A_characteristic_grows_as_the_client_reckons_it()
        {
            var ranges = new[] { (0, 7.0f), (100, 0.63f) };
            Assert.Equal(0, Pets.ValueAt(ranges, 0));
            Assert.Equal(62, Pets.ValueAt(ranges, 98));     // 99 × 0.63f, in single precision
            Assert.Equal(63, Pets.ValueAt(ranges, 99));     // 100 × 0.63f rounds to 63.0f
            Assert.Equal(63, Pets.ValueAt(ranges, 100));
            Assert.Equal(63, Pets.ValueAt(ranges, 101));
        }

        [Fact]
        public void A_pet_comes_in_at_level_zero_and_grows_to_its_template_at_one_hundred()
        {
            if (!Pets.IsPet(Miaumiau)) return;   // no pets data in this base
            var fresh = Pets.Fresh(Miaumiau);
            Assert.Equal((0L, 0L, 7L, 4L), (Value(fresh, Pets.Level), Value(fresh, Pets.Experience), Value(fresh, Power), Value(fresh, Escape)));

            var grown = Pets.Fed(Miaumiau, fresh, Pets.ExperienceAt(Pets.TopLevel) + 1000, out int before, out int after);
            Assert.Equal((0, 100), (before, after));
            Assert.Equal((70L, 40L), (Value(grown, Power), Value(grown, Escape)));
            Assert.Equal(Pets.ExperienceAt(100), Value(grown, Pets.Experience));

            var halfway = Pets.Fed(Miaumiau, fresh, Pets.ExperienceAt(50), out _, out int fifty);
            Assert.Equal(50, fifty);
            Assert.Equal(7 + Pets.ValueAt(new[] { (0, 7.0f), (100, 0.63f) }, 50), Value(halfway, Power));
        }

        [Fact]
        public void A_food_is_worth_its_own_experience_or_its_nuggets_times_ten()
        {
            if (!Pets.IsPet(Miaumiau)) return;
            Assert.Equal(1500, Pets.ExperienceOfFood(EnrichedBiscuit, 3));
            double worth = Recycling.PetFoodWorth(RecyclingTests.Resource);
            if (worth > 0) Assert.Equal(2 * worth * 10, Pets.ExperienceOfFood(RecyclingTests.Resource, 2), 6);
        }

        [Fact]
        public void Only_a_pet_at_one_hundred_becomes_legendary()
        {
            if (!Pets.IsPet(Miaumiau)) return;
            var status = LegendaryPets.FoodOf(Reflection)!;
            var young = new Equipment.Item { Uid = 1, Template = Miaumiau, Position = LegendaryPets.PetSlot };
            young.Effects.AddRange(Pets.Fresh(Miaumiau));
            Assert.Equal("the pet is not at level 100", LegendaryPets.WhyNotFeed(young, status));

            var grown = new Equipment.Item { Uid = 2, Template = Miaumiau, Position = LegendaryPets.PetSlot };
            grown.Effects.AddRange(Pets.Fed(Miaumiau, young.Effects, Pets.ExperienceAt(100), out _, out _));
            Assert.Null(LegendaryPets.WhyNotFeed(grown, status));
            Assert.Equal(101L, Value(LegendaryPets.Fed(grown.Effects, status), Pets.Level));
        }

        [Fact]
        public async Task Fed_from_its_window_a_pet_eats_and_grows()
        {
            if (!Pets.IsPet(Miaumiau)) return;
            RecyclingTests.ClearBag(Character);
            try
            {
                await using var pipe = await ClientPipe.OpenAsync(Account, Character, 191105026, "Criador");
                using (SessionContext.Push(pipe.Session))
                {
                    Equipment.LoadFrom(Character);
                    var pet = Equipment.Create(Miaumiau, 1, Pets.Fresh(Miaumiau))!;
                    var biscuits = Equipment.Create(EnrichedBiscuit, 5, Array.Empty<Equipment.ItemEffect>())!;

                    var itw = Pb.New().Var(1, pet.Uid).Msg(2, Pb.New().Var(2, biscuits.Uid).Var(3, 3)).Build();
                    await PetFeedHandler.FeedAsync(pipe.ToClient, ConnectionProtocol.Push(Op.Itw, itw));

                    Assert.Equal(2, Equipment.ByUid(biscuits.Uid)!.Quantity);
                    Assert.Equal(1500L, Value(pet.Effects, Pets.Experience));
                    Assert.Equal(Pets.LevelFor(1500), (int)Value(pet.Effects, Pets.Level));
                    Assert.True(Value(pet.Effects, Power) > 7);
                    Assert.Equal(Op.Ivj, (await pipe.NextAny()).Opcode);
                    Assert.Equal(Op.Iuj, (await pipe.NextAny()).Opcode);

                    // Fifty-one at once is more than the window takes: nothing is eaten.
                    var many = Equipment.Create(EnrichedBiscuit, 60, Array.Empty<Equipment.ItemEffect>())!;
                    itw = Pb.New().Var(1, pet.Uid).Msg(2, Pb.New().Var(2, many.Uid).Var(3, 51)).Build();
                    await PetFeedHandler.FeedAsync(pipe.ToClient, ConnectionProtocol.Push(Op.Itw, itw));
                    Assert.Equal(60, Equipment.ByUid(many.Uid)!.Quantity);
                }
            }
            finally
            {
                RecyclingTests.ClearBag(Character);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// The rides as the breeding window reads them: their levels and characteristics, the ride
    /// message, the stable and the paddocks, what the gauges do to them every ten seconds, and fuel.
    /// </summary>
    [Collection("MapManager")]
    public class BreedingRideTests
    {
        private const int Ebony = 33020;               // "Dragopavo ébano", ride 3
        private const int SlapperExtract = 33326;      // "Extracto de aporreadora": +3000, up to 40,000
        private const int SlapperElixir = 33498;       // "Elixir gigantesco de aporreadora": +5000, up to the top

        private static bool HasData => Breeding.RideByItem(Ebony) != null && Breeding.EvolutionsOf(Ebony).Count > 0;

        /// <summary>
        /// The client's formula 111, roundToNearest(3.795 · lvl^2.329): 172,668 at level 100 and
        /// 867,582 at 200, what a guide measured.
        /// </summary>
        [Fact]
        public void A_ride_s_experience_is_the_client_s_formula()
        {
            if (!HasData) return;
            Assert.Equal(200, Breeding.MaxLevel);
            Assert.Equal(4, Breeding.ExperienceAt(1));
            Assert.Equal(172_668, Breeding.ExperienceAt(100));
            Assert.Equal(867_582, Breeding.ExperienceAt(200));
            Assert.Equal(99, Breeding.LevelFor(172_667));
            Assert.Equal(100, Breeding.LevelFor(172_668));
            Assert.Equal(200, Breeding.LevelFor(10_000_000));
        }

        /// <summary>The ébano's vitality grows 3 a level to 100 and 1 after, its agility 1 and then 0.2: 400 and 120 at 200.</summary>
        [Fact]
        public void A_ride_s_characteristics_grow_with_its_level()
        {
            if (!HasData) return;
            int At(int level, int action) => (int)(Rides.CharacteristicsAt(Ebony, level).FirstOrDefault(e => e.Effect == action).Value);
            Assert.Equal((3, 1), (At(1, 125), At(1, 119)));
            Assert.Equal((300, 100), (At(100, 125), At(100, 119)));
            Assert.Equal((400, 120), (At(200, 125), At(200, 119)));
        }

        /// <summary>
        /// A new ride is level 1 with no experience, fertile, its gauges empty, its sex drawn and its
        /// serenity drawn in its template's range, with level 1's characteristics.
        /// </summary>
        [Fact]
        public void A_new_ride_starts_at_level_one()
        {
            if (!HasData) return;
            var ride = Rides.Fresh(Ebony, new Random(3));
            Assert.Equal(1, Rides.LevelOf(ride));
            Assert.Equal(0, Rides.ExperienceOf(ride));
            Assert.True(Rides.IsFertile(ride));
            Assert.False(Rides.IsFecund(ride));
            Assert.InRange(Rides.MoodOf(ride), 1, 5000);
            Assert.All(new[] { Rides.Love, Rides.Maturity, Rides.Stamina }, g => Assert.Equal(0, Rides.GaugeOf(ride, g)));
            Assert.Equal(3, ride.Single(e => e.Effect == 125).Value);

            var grown = Rides.WithExperience(Ebony, ride, Breeding.ExperienceAt(100));
            Assert.Equal(100, Rides.LevelOf(grown));
            Assert.Equal(300, grown.Single(e => e.Effect == 125).Value);
        }

        /// <summary>
        /// The ride message: f1 experience, f2 the characteristics only, f3 level, f4 male, f5 name,
        /// f6 the three gauges, f8 capacities, f10 sterile, f11 serenity, f12 the RidesData id.
        /// </summary>
        [Fact]
        public void The_ride_message()
        {
            if (!HasData) return;
            var effects = Rides.Fresh(Ebony, new Random(1));
            effects = Rides.With(effects, Rides.Gender, Rides.MaleValue);
            effects = Rides.With(effects, Rides.Mood, -1200);
            effects = Rides.With(effects, Rides.Love, 700);
            effects = Rides.With(effects, Rides.Name, 0, "Pepe");
            effects = Rides.With(effects, Rides.Wise, 0);
            var hqu = ProtoMessage.Parse(Rides.Hqu(Ebony, effects).Build()).Fields;

            Assert.Equal(1, (int)hqu.Single(f => f.FieldNumber == 3).VarIntValue);
            Assert.Equal(1, (int)hqu.Single(f => f.FieldNumber == 4).VarIntValue);
            Assert.Equal("Pepe", System.Text.Encoding.UTF8.GetString(hqu.Single(f => f.FieldNumber == 5).BytesValue));
            Assert.Equal(3, hqu.Count(f => f.FieldNumber == 6));
            Assert.Equal(2, (int)hqu.Single(f => f.FieldNumber == 8).VarIntValue);           // wise
            Assert.DoesNotContain(hqu, f => f.FieldNumber == 10);                           // fertile
            Assert.Equal(-1200, (int)hqu.Single(f => f.FieldNumber == 11).VarIntValue);
            Assert.Equal(3, (int)hqu.Single(f => f.FieldNumber == 12).VarIntValue);
            var shown = hqu.Where(f => f.FieldNumber == 2).Select(f => (int)ProtoMessage.Parse(f.BytesValue).Fields.Single(g => g.FieldNumber == 11).VarIntValue);
            Assert.All(shown, e => Assert.False(Rides.IsRideEffect(e)));
            var love = hqu.Where(f => f.FieldNumber == 6).Select(f => ProtoMessage.Parse(f.BytesValue).Fields).First();
            Assert.Equal(700, (int)love.Single(f => f.FieldNumber == 1).VarIntValue);
        }

        /// <summary>
        /// Every ten seconds an active gauge spends its tier's consumption and gives it to the
        /// paddock's rides: the feeder's 10 in the first tier is 10 experience, twice for a wise one;
        /// the dragobutt's love only goes in while the serenity is positive.
        /// </summary>
        [Fact]
        public void The_gauges_work_every_ten_seconds()
        {
            if (!HasData) return;
            const long character = 990_771;
            foreach (var old in Stable.Of(character)) Stable.Remove(character, old.Uid);
            try
            {
                var calm = new Stable.StoredRide { Uid = 990_771_001, CharacterId = character, Place = 1, Gid = Ebony,
                                                   Effects = Rides.With(Rides.Fresh(Ebony, new Random(1)), Rides.Mood, 100) };
                var angry = new Stable.StoredRide { Uid = 990_771_002, CharacterId = character, Place = 1, Gid = Ebony,
                                                    Effects = Rides.With(Rides.With(Rides.Fresh(Ebony, new Random(2)), Rides.Mood, -100), Rides.Wise, 0) };
                Stable.Save(calm);
                Stable.Save(angry);

                long now = 1_000_000_000;
                var gauges = new Stable.Gauges { TickedMs = now };
                gauges.Values[Stable.Feeder] = 1_000;
                gauges.Values[Stable.DragoButt] = 95_000;
                gauges.Active.AddRange(new[] { Stable.Feeder, Stable.DragoButt });
                Stable.SaveGauges(character, 1, gauges);

                Assert.True(Stable.Advance(character, 1, now + 3 * 10_000 + 4_000));
                var after = Stable.GaugesOf(character, 1);
                Assert.Equal(1_000 - 30, after.ValueOf(Stable.Feeder));
                Assert.Equal(95_000 - 3 * 40, after.ValueOf(Stable.DragoButt));          // the last tier spends 40
                Assert.Equal(now + 30_000, after.TickedMs);                              // the 4 s left wait

                var rides = Stable.In(character, 1).ToDictionary(r => r.Uid);
                Assert.Equal(30, Rides.ExperienceOf(rides[calm.Uid].Effects));
                Assert.Equal(60, Rides.ExperienceOf(rides[angry.Uid].Effects));          // wise
                Assert.Equal(120, Rides.GaugeOf(rides[calm.Uid].Effects, Rides.Love));
                Assert.Equal(0, Rides.GaugeOf(rides[angry.Uid].Effects, Rides.Love));    // its serenity is negative
            }
            finally
            {
                foreach (var old in Stable.Of(character)) Stable.Remove(character, old.Uid);
                Stable.SaveGauges(character, 1, new Stable.Gauges());
            }
        }

        /// <summary>The slapper takes serenity off and stops at -5000; the patter adds it.</summary>
        [Fact]
        public void The_slapper_and_the_patter_move_the_serenity()
        {
            if (!HasData) return;
            var ride = new Stable.StoredRide { Gid = Ebony, Effects = Rides.With(Rides.Fresh(Ebony, new Random(1)), Rides.Mood, -4990) };
            Assert.Equal(-5000, Rides.MoodOf(Stable.Worked(ride, Stable.Slapper, 40)));
            Assert.Equal(-4950, Rides.MoodOf(Stable.Worked(ride, Stable.Patter, 40)));
            Assert.Equal(40, Rides.GaugeOf(Stable.Worked(ride, Stable.Blaster, 40), Rides.Stamina));
            Assert.Equal(0, Rides.GaugeOf(Stable.Worked(ride, Stable.DrinkingTrough, 40), Rides.Maturity));
        }

        /// <summary>Two gauges at most, and the slapper and the patter never together ("no ambas cosas a la vez").</summary>
        [Fact]
        public void Two_gauges_at_most_and_never_both_serenity_ones()
        {
            Assert.True(BreedingHandler.CanEnable(new List<int>(), Stable.Slapper));
            Assert.False(BreedingHandler.CanEnable(new List<int> { Stable.Patter }, Stable.Slapper));
            Assert.True(BreedingHandler.CanEnable(new List<int> { Stable.Patter }, Stable.Feeder));
            Assert.False(BreedingHandler.CanEnable(new List<int> { Stable.Patter, Stable.Feeder }, Stable.DragoButt));
            Assert.False(BreedingHandler.CanEnable(new List<int> { Stable.Feeder }, Stable.Feeder));
        }

        /// <summary>An extract adds 3,000 up to 40,000; an elixir up to the gauge's top, 100,000.</summary>
        [Fact]
        public void Fuel_fills_its_gauge_up_to_its_tier()
        {
            if (!HasData) return;
            Assert.Equal((Stable.Slapper, 3_000, 40_000), BreedingHandler.FuelOf(SlapperExtract));
            Assert.Equal((Stable.Slapper, 5_000, 100_000), BreedingHandler.FuelOf(SlapperElixir));
            Assert.Null(BreedingHandler.FuelOf(Ebony));
        }

        /// <summary>The client's rules for a ride's name: twenty characters, unaccented letters, spaces and hyphens.</summary>
        [Fact]
        public void A_ride_s_name_follows_the_client_s_rules()
        {
            Assert.True(BreedingHandler.IsValidName("Rocinante"));
            Assert.True(BreedingHandler.IsValidName("Jolly Jumper-Dos"));
            Assert.True(BreedingHandler.IsValidName(""));
            Assert.False(BreedingHandler.IsValidName("Ñandú"));
            Assert.False(BreedingHandler.IsValidName("R2D2"));
            Assert.False(BreedingHandler.IsValidName(new string('a', 21)));
        }

        /// <summary>The requests as eqa writes them: hse's item uids and line, hsn's line and ids, huh's ids and line, hub's fuel.</summary>
        [Fact]
        public void The_requests_are_read()
        {
            var hse = Pb.New().Packed(1, new long[] { 11, 12 }).Msg(2, Pb.New()).Var(3, BreedingProtocol.StableLine).Build();
            var (items, into) = BreedingProtocol.ReadTransferFromBag(hse);
            Assert.Equal(new long[] { 11, 12 }, items);
            Assert.Equal(BreedingProtocol.StableLine, into);

            var hsn = Pb.New().Var(1, BreedingProtocol.StableLine).Str(2, "7").Str(2, "8").Build();
            var (line, ids) = BreedingProtocol.ReadSendToBag(hsn);
            Assert.Equal(BreedingProtocol.StableLine, line);
            Assert.Equal(new[] { "7", "8" }, ids);

            var huh = Pb.New().Str(1, "9").Build();
            Assert.Equal(BreedingProtocol.PaddockLine, BreedingProtocol.ReadMove(huh).Line);

            var hub = Pb.New().Msg(1, Pb.New().Var(1, 44).Var(2, 3)).Build();
            Assert.Equal(3, BreedingProtocol.ReadFuel(hub)[44]);
            Assert.Equal(Stable.Feeder, BreedingProtocol.ReadGauge(Pb.New().Var(1, 5).Build()));
            Assert.Equal(Stable.Slapper, BreedingProtocol.ReadGauge(Array.Empty<byte>()));
        }
    }
}

using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.World.Fights;
using Xunit;

namespace Jondo.Unity.Tests.Combat
{
    /// <summary>
    /// The two rules the Rogue's bombs do not share with ordinary summons.
    /// </summary>
    /// <remarks>
    /// Both are measured, not assumed, and both come from the same place: the 22 Tymador captures
    /// and the class captures around them.
    ///
    /// <b>They cost no capacity.</b> Explobomba (3112), Tornabomba (3113) and Bomba de agua (3114)
    /// all read <c>summonCost = 0</c> in MonsterTemplates, so the summon cap never sees them. The
    /// server used to count bodies, which is why a player with a capacity of one could place a
    /// single bomb and no more.
    ///
    /// <b>They are not in the carousel.</b> Across 52 summoned templates and 219 summons, being
    /// listed in jzu matches ever receiving a jzc with no counterexample in either direction. The
    /// three bombs are 66 summons, zero jzu, zero turns.
    ///
    /// The cap of three is measured too: 62 bombs summoned across those captures, three on the
    /// board at once in seven separate fights, never four.
    /// </remarks>
    public class BombRulesTests
    {
        private static FightInstance FightWith(params Fighter[] players)
        {
            var fight = new FightInstance(1, 1);
            foreach (var p in players) fight.AddPlayer(p);
            return fight;
        }

        private static Fighter Bomb(long id, Fighter owner, int template = 3112) => new()
        {
            Id = id,
            CurrentHP = 90,
            MaxHP = 90,
            IsMonster = true,
            MonsterId = template,
            Invocador = owner.Id,
            SummonCost = 0,
            JuegaTurno = false,
        };

        [Fact]
        public void Three_bombs_take_up_no_capacity_at_all()
        {
            var rogue = new Fighter { Id = 10, CurrentHP = 100 };
            var fight = FightWith(rogue);
            for (long id = -2; id >= -4; id--) fight.AddPlayer(Bomb(id, rogue));

            Assert.Equal(0, FightHandler.UsedSummonCapacity(fight, rogue));
            Assert.Equal(3, FightHandler.ActiveBombCount(fight, rogue));
        }

        [Fact]
        public void A_real_summon_still_fills_the_slot_next_to_the_bombs()
        {
            var rogue = new Fighter { Id = 10, CurrentHP = 100 };
            var fight = FightWith(rogue);
            for (long id = -2; id >= -4; id--) fight.AddPlayer(Bomb(id, rogue));
            fight.AddPlayer(new Fighter
            {
                Id = -5, CurrentHP = 100, IsMonster = true, MonsterId = 8070,
                Invocador = rogue.Id, SummonCost = 1,
            });

            Assert.Equal(1, FightHandler.UsedSummonCapacity(fight, rogue));
            Assert.Equal(1, FightHandler.SummonLimitFor(rogue, round: 0));
        }

        [Fact]
        public void A_summon_that_costs_three_fills_a_capacity_of_three_on_its_own()
        {
            // Crujintesco, template 8078, summonCost 3. Counting bodies read it as one.
            var osa = new Fighter { Id = 10, CurrentHP = 100 };
            var fight = FightWith(osa);
            fight.AddPlayer(new Fighter
            {
                Id = -2, CurrentHP = 100, IsMonster = true, MonsterId = 8078,
                Invocador = osa.Id, SummonCost = 3,
            });

            Assert.Equal(3, FightHandler.UsedSummonCapacity(fight, osa));
        }

        [Fact]
        public void A_dead_bomb_stops_counting()
        {
            var rogue = new Fighter { Id = 10, CurrentHP = 100 };
            var fight = FightWith(rogue);
            var muerta = Bomb(-2, rogue);
            fight.AddPlayer(muerta);
            fight.AddPlayer(Bomb(-3, rogue));
            muerta.CurrentHP = 0;

            Assert.Equal(1, FightHandler.ActiveBombCount(fight, rogue));
        }

        [Fact]
        public void The_cap_is_per_rogue_and_not_per_fight()
        {
            // Two Rogues in the same fight get three bombs each, not three between them.
            var uno = new Fighter { Id = 10, CurrentHP = 100 };
            var otro = new Fighter { Id = 11, CurrentHP = 100 };
            var fight = FightWith(uno, otro);

            for (long id = -2; id >= -4; id--) fight.AddPlayer(Bomb(id, uno));
            fight.AddPlayer(Bomb(-5, otro, template: 3113));

            Assert.Equal(FightHandler.MaxBombsOnBoard, FightHandler.ActiveBombCount(fight, uno));
            Assert.Equal(1, FightHandler.ActiveBombCount(fight, otro));
        }

        [Fact]
        public void Tymobot_is_a_summon_and_not_a_bomb()
        {
            // It shares race 220 with the bombs, but no bomb spell names it in its target mask
            // and it plays turns like any summon: 7 summoned in the captures, 7 in the jzu, 7
            // with a turn. Taking the list from the race instead of from the masks would cap it.
            Assert.False(Bombs.Is(3120));
            Assert.True(Bombs.Is(3112));
            Assert.True(Bombs.Is(3113));
            Assert.True(Bombs.Is(3114));
            Assert.True(Bombs.Is(5161));
        }

        [Fact]
        public void A_summon_that_never_plays_stays_out_of_the_carousel()
        {
            var rogue = new Fighter { Id = 10, CurrentHP = 100 };

            Assert.True(FightHandler.EntraEnElCarrusel(rogue));
            Assert.False(FightHandler.EntraEnElCarrusel(Bomb(-2, rogue)));

            // The Survival Beacon does play, and stays in.
            Assert.True(FightHandler.EntraEnElCarrusel(new Fighter
            {
                Id = -3, CurrentHP = 100, IsMonster = true, MonsterId = 8348,
                Invocador = rogue.Id, SummonCost = 0, JuegaTurno = true,
            }));
        }

        [Fact]
        public async Task A_fourth_bomb_is_refused_before_it_costs_any_AP()
        {
            var rogue = new Fighter { Id = 10, CurrentHP = 100, CurrentAP = 6 };
            var fight = FightWith(rogue);
            for (long id = -2; id >= -4; id--) fight.AddPlayer(Bomb(id, rogue));

            // Explobomba: effect 1008, not 181, with the template in the die.
            var bombEffect = new Jondo.Unity.Server.Managers.SpellEffect
            {
                EffectId = 1008,
                DiceNum = 3112,
                Triggers = Jondo.Unity.Server.Managers.EffectEngine.AlLanzar,
            };

            bool paid = await FightHandler.TryPayCastCostAsync(
                fight, rogue, new[] { bombEffect }, cost: 2, _ => Task.CompletedTask);

            Assert.False(paid);
            Assert.Equal(6, rogue.CurrentAP);
        }

        [Fact]
        public async Task A_third_bomb_goes_through_and_pays()
        {
            var rogue = new Fighter { Id = 10, CurrentHP = 100, CurrentAP = 6 };
            var fight = FightWith(rogue);
            fight.AddPlayer(Bomb(-2, rogue));
            fight.AddPlayer(Bomb(-3, rogue, template: 3113));

            var bombEffect = new Jondo.Unity.Server.Managers.SpellEffect
            {
                EffectId = 1008,
                DiceNum = 3114,
                Triggers = Jondo.Unity.Server.Managers.EffectEngine.AlLanzar,
            };

            bool paid = await FightHandler.TryPayCastCostAsync(
                fight, rogue, new[] { bombEffect }, cost: 2, _ => Task.CompletedTask);

            Assert.True(paid);
            Assert.Equal(4, rogue.CurrentAP);
        }

        [Fact]
        public void A_dead_fighter_leaves_the_carousel()
        {
            var rogue = new Fighter { Id = 10, CurrentHP = 0 };
            Assert.False(FightHandler.EntraEnElCarrusel(rogue));
        }
    }
}

using System;
using System.IO;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.World.Fights;
using Xunit;

namespace Jondo.Unity.Tests.Combat
{
    [Collection("guild raids")]
    public sealed class CombatExperienceTests : IDisposable
    {
        private readonly string file = Path.Combine(Path.GetTempPath(), $"jondo-combat-xp-{Guid.NewGuid():N}.db");

        public CombatExperienceTests() => GuildStore.ConnectionStringOverride = $"Data Source={file}";

        public void Dispose()
        {
            GuildStore.ConnectionStringOverride = null;
            TestDatabases.ReleasePool(file);
            File.Delete(file);
        }

        [Theory]
        [InlineData(0, 5500)]
        [InlineData(50, 8250)]
        [InlineData(150, 13750)]
        [InlineData(300, 22000)]
        [InlineData(-100, 5500)]
        public void Wisdom_is_personal_and_has_no_hundred_point_steps(int wisdom, long expected)
            => Assert.Equal(expected, CombatExperience.Share(10000, 100, new[] { 100, 100 }, 100, 200, wisdom));

        [Fact]
        public void Too_strong_monsters_reduce_solo_XP_as_well()
            => Assert.Equal(2750, CombatExperience.Share(10000, 100, new[] { 100 }, 400, 400));

        [Fact]
        public void Omega_levels_do_not_change_the_share_or_the_group_threshold()
        {
            Assert.Equal(3437, CombatExperience.Share(10000, 500, new[] { 500, 354 }, 50, 400));
            Assert.Equal(CombatExperience.Share(10000, 200, new[] { 200, 70 }, 200, 270),
                         CombatExperience.Share(10000, 500, new[] { 500, 70 }, 200, 270));
        }

        [Fact]
        public void The_third_threshold_is_not_rounded_down()
        {
            Assert.Equal(7518, CombatExperience.Share(10000, 100, new[] { 100, 33 }, 100, 133));
            Assert.Equal(0, CombatExperience.Share(0, 100, new[] { 100 }, 0, 0));
        }

        [Fact]
        public void Donations_take_mount_first_then_guild_and_credit_only_the_converted_amount()
        {
            var split = CombatExperience.Allocate(100000, 20, 20, 20, 50, 20);
            Assert.Equal(40000, split.Character);
            Assert.Equal(20000, split.MountTaken);
            Assert.Equal(2000, split.Mount);
            Assert.Equal(40000, split.GuildTaken);
            Assert.Equal(4000, split.Guild); // No custom x10.
            Assert.Equal(100000, split.Character + split.MountTaken + split.GuildTaken);
        }

        [Theory]
        [InlineData(10, 1000)]
        [InlineData(11, 800)]
        [InlineData(60, 200)]
        [InlineData(61, 150)]
        [InlineData(70, 150)]
        [InlineData(71, 100)]
        public void Stump_conversion_keeps_its_strict_gap_boundaries(int gap, long expected)
            => Assert.Equal(expected, CombatExperience.ConvertedDonation(10000, 1 + gap, 1));

        [Fact]
        public void Mount_and_guild_credits_persist_and_update_the_members_contribution()
        {
            var guild = GuildStore.Create(1, "XP", 1, 1, 1, 1);
            ExperienceDonations.SetRates(1, 20, guild.Id, 50);
            ExperienceDonations.SetMountLevel(55, 20);
            var split = ExperienceDonations.Credit(1, 100000, 20, 55);
            Assert.Equal(40000, split.Character);
            Assert.Equal((20, 2000L), ExperienceDonations.MountOf(55));
            Assert.Equal(3200, GuildStore.GuildOf(1).Experience); // Guild level 1: gap 19 => 8% of 40000.
            Assert.Equal(3200, GuildStore.MemberOf(1).Experience);
            Assert.Equal(GuildLevels.MaxLevel, GuildStore.GuildOf(1).Level);
            ExperienceDonations.SetMountLevel(55, 21);
            Assert.Equal((21, 2000L), ExperienceDonations.MountOf(55));
        }

        [Fact]
        public void Missing_mount_or_membership_does_not_take_XP()
        {
            ExperienceDonations.SetRates(2, 90, 999, 90);
            var split = ExperienceDonations.Credit(2, 10000, 100, 0);
            Assert.Equal(10000, split.Character);
            Assert.Equal(0, split.MountTaken);
            Assert.Equal(0, split.GuildTaken);
        }

        [Theory]
        [InlineData(false, 0)]
        [InlineData(true, 5000)]
        public void A_defeat_pays_twenty_percent_on_kills_with_no_loot_or_kamas(bool killed, long expected)
        {
            const long player = 8_999_001;
            var session = GameSession.SinSocket();
            session.BindAccount(player, 1);
            session.State.CharacterId = player;
            session.State.CharacterLevel = 100;
            session.State.StatWisdom = 150;
            session.EnterWorld();
            Assert.True(SessionRegistry.Register(session));
            try
            {
                var fight = new FightInstance(killed ? 8_999_001 : 8_999_002, 1);
                fight.AddPlayer(new Fighter { Id = player, Level = 100, MaxHP = 100, CurrentHP = 0 });
                fight.AddMonster(new Fighter { Id = -1, Level = 100, MaxHP = 100, CurrentHP = killed ? 0 : 100,
                    IsMonster = true, XpReward = 10000 });
                fight.AddMonster(new Fighter { Id = -2, Level = 100, MaxHP = 100, CurrentHP = 100,
                    IsMonster = true, XpReward = 90000 });
                fight.AddMonster(new Fighter { Id = -3, Level = 100, MaxHP = 100, CurrentHP = 0,
                    IsMonster = true, Invocador = -2, XpReward = 90000 });
                fight.ChallengesFixed.Add((1, 100)); // Defeat cannot earn a challenge bonus.
                FightHandler.PlanRewards(fight);
                var reward = Assert.IsType<FightHandler.Reward>(FightHandler.RewardOf(fight, player));
                Assert.Equal(expected, reward.Xp); // A kill pays 10000 * 2.5 wisdom * 20%.
                Assert.Equal(0, reward.Kamas);
                Assert.Empty(reward.Loot);
                FightHandler.PlanRewards(fight);
                Assert.Same(reward, FightHandler.RewardOf(fight, player));
            }
            finally { SessionRegistry.Unregister(session); }
        }

        [Fact]
        public void A_shared_challenge_keeps_personal_wisdom_and_donations_are_credited_once()
        {
            const long first = 8_999_011, second = 8_999_012;
            var guild = GuildStore.Create(first, "Shared XP", 1, 1, 1, 1);
            ExperienceDonations.SetRates(first, 0, guild.Id, 50);
            var a = GameSession.SinSocket();
            var b = GameSession.SinSocket();
            foreach (var (session, id, wisdom) in new[] { (a, first, 100), (b, second, 300) })
            {
                session.BindAccount(id, 1);
                session.State.CharacterId = id;
                session.State.CharacterLevel = 100;
                session.State.StatWisdom = wisdom;
                session.EnterWorld();
                Assert.True(SessionRegistry.Register(session));
            }
            try
            {
                var fight = new FightInstance(8_999_011, 1);
                fight.AddPlayer(new Fighter { Id = first, Level = 100, MaxHP = 100, CurrentHP = 100 });
                fight.AddPlayer(new Fighter { Id = second, Level = 100, MaxHP = 100, CurrentHP = 0 });
                for (int i = 1; i <= 2; i++)
                    fight.AddMonster(new Fighter { Id = -i, Level = 100, MaxHP = 100, CurrentHP = 0,
                        IsMonster = true, XpReward = 5000 });
                fight.ChallengesFixed.Add((1, 100));
                FightHandler.PlanRewards(fight);
                Assert.Equal(11000, FightHandler.RewardOf(fight, first).Xp);
                Assert.Equal(44000, FightHandler.RewardOf(fight, second).Xp);
                Assert.Equal(110, GuildStore.GuildOf(first).Experience);
                FightHandler.PlanRewards(fight);
                Assert.Equal(110, GuildStore.GuildOf(first).Experience);
            }
            finally
            {
                SessionRegistry.Unregister(a);
                SessionRegistry.Unregister(b);
            }
        }
    }
}

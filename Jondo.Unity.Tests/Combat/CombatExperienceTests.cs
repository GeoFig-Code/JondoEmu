using System;
using System.IO;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.World.Fights;
using Xunit;

namespace Jondo.Unity.Tests.Combat
{
    /// <summary>
    /// The experience of a fight against monsters, as the client works it out for the tooltip of
    /// a monster group (see <see cref="CombatExperience"/>).
    /// </summary>
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

        /// <summary>
        /// The client counts for the group those at the highest level divided by three in whole
        /// numbers or more: with a 100, a 33 counts (1.1) and a 32 does not.
        /// </summary>
        [Fact]
        public void The_third_is_rounded_down_as_the_client_does()
        {
            Assert.Equal(8270, CombatExperience.Share(10000, 100, new[] { 100, 33 }, 100, 133));
            Assert.Equal(7575, CombatExperience.Share(10000, 100, new[] { 100, 32 }, 100, 132));
            Assert.Equal(0, CombatExperience.Share(0, 100, new[] { 100 }, 0, 0));
        }

        /// <summary>
        /// Two real fights. The solo one of «entrar a combate-desconectarse-reconectar…»: eight
        /// monsters, 13,894 gradeXp, levels adding up to 109, the strongest at 15, a 354 with wisdom 100 and its
        /// challenge failed. The four monsters of the follow capture for the 354 with wisdom 352, beside a 447. The client's sum
        /// gives 2,800 and 339 (559 with the challenge's 65 %); the real server paid 2,980 and 589.
        /// </summary>
        [Fact]
        public void The_captured_fights_by_the_client_s_sum()
        {
            Assert.Equal(2800, CombatExperience.Share(13894, 354, new[] { 354 }, 15, 109, 100));
            Assert.Equal(339, CombatExperience.Share(6260, 354, new[] { 447, 354 }, 14, 50, 352));
        }

        [Fact]
        public void A_defeat_pays_nothing()
        {
            const long player = 8_999_001;
            var session = GameSession.SinSocket();
            session.BindAccount(player, 1);
            session.State.CharacterId = player;
            session.State.CharacterLevel = 100;
            session.EnterWorld();
            Assert.True(SessionRegistry.Register(session));
            try
            {
                var fight = new FightInstance(8_999_001, 1);
                fight.AddPlayer(new Fighter { Id = player, Level = 100, MaxHP = 100, CurrentHP = 0 });
                fight.AddMonster(new Fighter { Id = -1, Level = 100, MaxHP = 100, CurrentHP = 0,
                    IsMonster = true, XpReward = 10000 });
                fight.AddMonster(new Fighter { Id = -2, Level = 100, MaxHP = 100, CurrentHP = 100,
                    IsMonster = true, XpReward = 90000 });
                FightHandler.PlanRewards(fight);
                Assert.Null(FightHandler.RewardOf(fight, player));
            }
            finally { SessionRegistry.Unregister(session); }
        }

        [Fact]
        public void A_shared_challenge_keeps_personal_wisdom()
        {
            const long first = 8_999_011, second = 8_999_012;
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
                Assert.Equal(22000, FightHandler.RewardOf(fight, first).Xp);
                Assert.Equal(44000, FightHandler.RewardOf(fight, second).Xp);
                var planned = FightHandler.RewardOf(fight, first);
                FightHandler.PlanRewards(fight);
                Assert.Same(planned, FightHandler.RewardOf(fight, first));
            }
            finally
            {
                SessionRegistry.Unregister(a);
                SessionRegistry.Unregister(b);
            }
        }

        /// <summary>
        /// One who walked out of the fight is no longer of the group: the other is paid alone, and
        /// the one who left, back on the map, takes nothing of a win he gave up.
        /// </summary>
        [Fact]
        public void Who_left_does_not_count_for_the_group()
        {
            const long stays = 8_999_021, left = 8_999_022;
            var a = GameSession.SinSocket();
            var b = GameSession.SinSocket();
            foreach (var (session, id) in new[] { (a, stays), (b, left) })
            {
                session.BindAccount(id, 1);
                session.State.CharacterId = id;
                session.State.CharacterLevel = 100;
                session.EnterWorld();
                Assert.True(SessionRegistry.Register(session));
            }
            try
            {
                var fight = new FightInstance(8_999_021, 1);
                fight.AddPlayer(new Fighter { Id = stays, Level = 100, MaxHP = 100, CurrentHP = 100 });
                fight.AddPlayer(new Fighter { Id = left, Level = 100, MaxHP = 100, CurrentHP = 0 });
                fight.Gone.Add(left);
                fight.AddMonster(new Fighter { Id = -1, Level = 100, MaxHP = 100, CurrentHP = 0,
                    IsMonster = true, XpReward = 10000 });
                FightHandler.PlanRewards(fight);
                Assert.Equal(CombatExperience.Share(10000, 100, new[] { 100 }, 100, 100),
                             FightHandler.RewardOf(fight, stays).Xp);
                Assert.Null(FightHandler.RewardOf(fight, left));
            }
            finally
            {
                SessionRegistry.Unregister(a);
                SessionRegistry.Unregister(b);
            }
        }
    }
}

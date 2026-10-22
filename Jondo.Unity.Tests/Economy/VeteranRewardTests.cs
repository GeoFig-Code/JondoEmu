using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jondo.Unity.Launcher;
using Jondo.Unity.Server.Managers;
using Xunit;

namespace Jondo.Unity.Tests.Economy
{
    /// <summary>The veteran rewards: earned by the days of subscription, each once.</summary>
    public class VeteranRewardTests
    {
        private static bool Available => File.Exists(Paths.VeteranRewardsJson);

        [Fact]
        public void The_client_s_seventy_six_rewards_are_read()
        {
            if (!Available) return;
            Assert.Equal(76, VeteranRewards.All.Count);
            var first = VeteranRewards.All[0];
            Assert.Equal((7, 15884, 5), (first.Days, first.Item, first.Quantity));   // five «Silbar»
        }

        [Fact]
        public void An_account_gets_what_its_days_earned_and_not_twice()
        {
            if (!Available) return;
            Assert.Empty(VeteranRewards.Due(6, new HashSet<int>()));
            Assert.Equal(new[] { 1, 2 }, VeteranRewards.Due(45, new HashSet<int>()).Select(r => r.Id));
            Assert.Equal(new[] { 2 }, VeteranRewards.Due(45, new HashSet<int> { 1 }).Select(r => r.Id));
        }

        [Fact]
        public void The_days_count_from_the_subscription_s_start()
        {
            // An account made on 1 October 2026 was given until 1 October 2027.
            var now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.FromHours(2));
            Assert.Equal(21, VeteranRewards.SubscribedDays("2027-10-01T12:00:00+02:00", now), 1);
            Assert.Equal(0, VeteranRewards.SubscribedDays("", now));
        }
    }
}

using System;
using Jondo.Unity.Server.Managers;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// The resources' protectors: each resource finds its own through the sack it drops (effect 209),
    /// they come out from job level 20, and the grade is the highest the job's level reaches.
    /// </summary>
    public class ResourceProtectorTests
    {
        private static readonly int[] Levels = { 10, 30, 50, 70, 90, 110, 130, 150, 170, 190 };

        [Fact]
        public void A_resource_finds_its_protector_through_its_sack()
        {
            int wheat = ResourceProtectors.ProtectorOf(289);
            if (wheat == 0) return;   // no monster data in this base
            Assert.Equal(684, wheat);                                   // Espantapájaros de trigo
            Assert.Equal(693, ResourceProtectors.ProtectorOf(312));     // Crujibola de hierro
            Assert.Equal(0, ResourceProtectors.ProtectorOf(1));
        }

        [Fact]
        public void The_grade_is_the_highest_the_job_reaches()
        {
            Assert.Equal(0, ResourceProtectors.GradeFor(Levels, 20));
            Assert.Equal(1, ResourceProtectors.GradeFor(Levels, 30));
            Assert.Equal(4, ResourceProtectors.GradeFor(Levels, 100));
            Assert.Equal(9, ResourceProtectors.GradeFor(Levels, 200));
        }

        [Fact]
        public void None_comes_out_before_job_level_twenty()
        {
            var dice = new Random(1);
            for (int i = 0; i < 2000; i++) Assert.False(ResourceProtectors.Comes(19, dice));
            int came = 0;
            for (int i = 0; i < 20000; i++) if (ResourceProtectors.Comes(100, dice)) came++;
            Assert.InRange(came, 100, 1100);   // one in a hundred, five times that on the Almanax's day
        }
    }
}

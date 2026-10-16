using System.Collections.Concurrent;
using System.Reflection;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.World.Fights;
using Xunit;

namespace Jondo.Unity.Tests.Combat
{
    /// <summary>
    /// Giving up a fight that goes on without you -- a prism's defender beside it --: once gone,
    /// the fight is no longer yours, and it is still the others'.
    /// </summary>
    public class GivingUpTests
    {
        [Fact]
        public void One_who_gave_up_is_no_longer_in_the_fight()
        {
            const long quitter = 990_000_031, stays = 990_000_032;
            var fight = new FightInstance(7_100_001, 1);
            fight.Azul.Add(new Fighter { Id = stays, CurrentHP = 100 });
            fight.Rojo.Add(new Fighter { Id = quitter, CurrentHP = 0 });
            fight.Rojo.Add(new Fighter { Id = -1, CurrentHP = 100, IsMonster = true });

            var active = (ConcurrentDictionary<long, FightInstance>)typeof(FightHandler)
                .GetField("_activeFights", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            active[fight.FightId] = fight;
            try
            {
                Assert.Same(fight, FightHandler.FightOf(quitter));
                fight.Gone.Add(quitter);
                Assert.Null(FightHandler.FightOf(quitter));
                Assert.Same(fight, FightHandler.FightOf(stays));
            }
            finally { active.TryRemove(fight.FightId, out _); }
        }
    }
}

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.World.Fights;
using Xunit;

namespace Jondo.Unity.Tests.Combat
{
    /// <summary>
    /// A fight's own turn (FightInstance.Turno): what several connections and the fight's clocks
    /// do to one fight goes one at a time.
    /// </summary>
    public class FightTurnLockTests
    {
        [Fact]
        public async Task Two_actions_on_one_fight_never_overlap()
        {
            var fight = new FightInstance(8_960_001, 1, 1);
            int inside = 0, most = 0;

            async Task Act()
            {
                await FightHandler.InTurnAsync(fight, async () =>
                {
                    int now = Interlocked.Increment(ref inside);
                    InterlockedMax(ref most, now);
                    await Task.Delay(5);
                    Interlocked.Decrement(ref inside);
                });
            }

            await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(Act)));
            Assert.Equal(1, most);
        }

        /// <summary>Two of a party asking for one placement cell at once: one gets it.</summary>
        [Fact]
        public async Task Two_asking_for_one_cell_at_once_do_not_share_it()
        {
            for (int round = 0; round < 50; round++)
            {
                var fight = new FightInstance(8_960_002 + round, 1, 1);
                var one = new Fighter { Id = 8_960_000_001, CellId = 300, MaxHP = 100, CurrentHP = 100 };
                var two = new Fighter { Id = 8_960_000_002, CellId = 301, MaxHP = 100, CurrentHP = 100 };
                fight.AddPlayer(one);
                fight.AddPlayer(two);
                fight.BluePlacementCells.AddRange(new[] { 300, 301, 302 });

                using var go = new ManualResetEventSlim();
                var a = Task.Run(async () => { go.Wait(); await FightHandler.InTurnAsync(fight, () => { fight.ChangePlacementCell(one.Id, 302); return Task.CompletedTask; }); });
                var b = Task.Run(async () => { go.Wait(); await FightHandler.InTurnAsync(fight, () => { fight.ChangePlacementCell(two.Id, 302); return Task.CompletedTask; }); });
                go.Set();
                await Task.WhenAll(a, b);

                Assert.Equal(1, fight.Todos.Count(f => f.CellId == 302));
            }
        }

        /// <summary>The burst that opens the fight goes out once, whoever asks for it second.</summary>
        [Fact]
        public void A_fight_is_started_once()
        {
            var fight = new FightInstance(8_960_100, 1, 1);
            var claims = Enumerable.Range(0, 8).AsParallel().Select(_ => fight.TryClaimStart()).ToList();
            Assert.Equal(1, claims.Count(c => c));
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int seen;
            while (value > (seen = Volatile.Read(ref target)) &&
                   Interlocked.CompareExchange(ref target, value, seen) != seen) { }
        }
    }
}

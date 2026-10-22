using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Tasks;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.Tests.Economy;
using Jondo.Unity.World.Fights;
using Xunit;

namespace Jondo.Unity.Tests.Combat
{
    /// <summary>A cell marked for one's side, as in «combate contra poutch…-marcadores permanentes».</summary>
    public sealed class FightMarkerTests
    {
        [Fact]
        public async Task A_marked_cell_reaches_the_side_and_not_the_enemy()
        {
            const long me = 990_000_041, friend = 990_000_042, enemy = 990_000_043;
            var fight = new FightInstance(7_100_041, 1);
            fight.Azul.Add(new Fighter { Id = me, CurrentHP = 100, TeamId = 0 });
            fight.Azul.Add(new Fighter { Id = friend, CurrentHP = 100, TeamId = 0 });
            fight.Rojo.Add(new Fighter { Id = enemy, CurrentHP = 100, TeamId = 1 });

            var active = (ConcurrentDictionary<long, FightInstance>)typeof(FightHandler)
                .GetField("_activeFights", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            active[fight.FightId] = fight;
            await using var mine = await ClientPipe.OpenAsync(8_900_000_091, me, 191105026, "Marca-Uno");
            await using var theirs = await ClientPipe.OpenAsync(8_900_000_092, friend, 191105026, "Marca-Dos");
            await using var other = await ClientPipe.OpenAsync(8_900_000_093, enemy, 191105026, "Marca-Tres");
            try
            {
                using (SessionContext.Push(mine.Session))
                {
                    // kmh 10 a0 02, cell 288: kmj { 1 who, 2 288 }.
                    await FightHandler.MarkCellAsync(mine.ToClient, ConnectionProtocol.Push("kmh", Convert.FromHexString("10a002")));
                }
                byte[] expected = Pb.New().Var(1, me).Var(2, 288).Build();
                Assert.Equal(expected, await mine.Next("kmj"));
                Assert.Equal(expected, await theirs.Next("kmj"));
                await other.Quiet();
            }
            finally { active.TryRemove(fight.FightId, out _); }
        }
    }
}

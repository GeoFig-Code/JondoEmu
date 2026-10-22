using System;
using System.Linq;
using System.Threading.Tasks;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.Tests.Economy;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>A caught criminal follows his captor, «en personnage suiveur», until he is handed over.</summary>
    [Collection("WantedPosters")]
    public sealed class WantedFollowerTests
    {
        private const long Account = 8_900_000_111;

        [Fact]
        public async Task The_caught_criminal_walks_behind_and_is_let_go_at_the_jailer()
        {
            WantedPosters.Load();
            var gein = WantedPosters.ByQuest(1563);
            Assert.NotNull(gein);
            long character = DatabaseManager.CreateCharacter(Account, 1, "Captor-Uno", 4, 0, 1,
                new long[] { -1, -1, -1, -1, -1 }, 191105026, 200, 0, 0, Array.Empty<(int, int)>());
            Assert.True(character > 0);
            try
            {
                await using var pipe = await ClientPipe.OpenAsync(Account, character, 990_100_111, "Captor-Uno");
                using (SessionContext.Push(pipe.Session))
                {
                    await WantedHandler.TakeAlongAsync(pipe.ToClient, gein!);
                    var follower = Equipment.All.Single(i => i.Template == gein!.Follower);
                    Assert.Equal(SlotLooks.Follower, follower.Position);
                    // «148»: the criminal's own monster is what follows.
                    Assert.Contains(follower.Effects, e => e.Effect == SlotLooks.Follows && e.Value == gein!.Monster);

                    // Caught twice is still one.
                    await WantedHandler.TakeAlongAsync(pipe.ToClient, gein!);
                    Assert.Single(Equipment.All, i => i.Template == gein!.Follower);

                    await WantedHandler.LetGoAsync(pipe.ToClient, gein!);
                    Assert.DoesNotContain(Equipment.All, i => i.Template == gein!.Follower);
                }
            }
            finally
            {
                DatabaseManager.DeleteCharacter(character, Account);
            }
        }
    }
}

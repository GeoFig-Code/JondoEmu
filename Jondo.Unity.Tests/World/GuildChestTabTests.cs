using System.Linq;
using Jondo.Unity.Server.Managers;
using Xunit;
namespace Jondo.Unity.Tests.World
{
    /// <summary>The guild chest's tabs come with the guild's level, as the client's rewards say.</summary>
    public class GuildChestTabTests
    {
        [Fact]
        public void A_tab_comes_at_levels_3_7_and_11()
        {
            if (!System.IO.File.Exists(Jondo.Unity.Launcher.Paths.Resolve("guild_level_rewards_3.6.10.11.json"))) return;
            Assert.Equal(new[] { 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4 },
                         Enumerable.Range(1, 13).Select(GuildChests.TabsAtLevel));
        }
    }
}

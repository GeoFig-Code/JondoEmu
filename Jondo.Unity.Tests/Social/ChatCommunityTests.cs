using System;
using System.Threading.Tasks;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.Tests.Economy;
using Xunit;

namespace Jondo.Unity.Tests.Social
{
    /// <summary>The chat's community, acknowledged as on entering the world in the capture.</summary>
    public sealed class ChatCommunityTests
    {
        [Fact]
        public async Task The_community_is_acknowledged_with_its_value()
        {
            await using var pipe = await ClientPipe.OpenAsync(8_900_000_061, 8_900_100_061, 191105026, "Comunidad");
            using (SessionContext.Push(pipe.Session))
            {
                // «crear personaje - borrar personaje», frames 121 and 123: hos 10 02, hou 08 02.
                await ChatHandler.CommunityAsync(pipe.ToClient, ConnectionProtocol.Push("hos", Convert.FromHexString("1002")));
                Assert.Equal(Convert.FromHexString("0802"), await pipe.Next("hou"));
            }
        }
    }
}

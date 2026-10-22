using System;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.Tests.Economy;
using Xunit;

namespace Jondo.Unity.Tests.Social
{
    /// <summary>A ping on an entity of the map (ijg), answered ijq and ijf as the dream capture's frames 65-67.</summary>
    public sealed class EntityPingTests
    {
        private const long AccountA = 8_873_000_001, AccountB = 8_873_000_002, AccountC = 8_873_000_003;
        private const long CharacterA = 8_873_100_001, CharacterB = 8_873_100_002, CharacterC = 8_873_100_003;

        /// <summary>The capture's two characters, with the ids its sanitised copy carries.</summary>
        private const long Pinger = 300000000001, Pinged = 290000000003;
        private const long Map = 8_873_555_001;

        private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

        [Fact]
        public void The_answers_have_the_capture_s_shape()
        {
            // ijq 1a09 { 08 <entity> 10 04 } and ijf 08 <entity> 18 <who pinged>.
            Assert.Equal("1a090883a8e3aab8081004", Hex(EntityPingHandler.BuildStanding(Pinged, 4)));
            Assert.Equal("0883a8e3aab8081881f092cbdd08", Hex(EntityPingHandler.BuildPinged(Pinged, Pinger)));
        }

        [Fact]
        public async Task The_one_who_pings_and_his_party_on_the_map_see_it_and_nobody_else()
        {
            await using var a = await ClientPipe.OpenAsync(AccountA, CharacterA, Map, "Ping-Uno");
            await using var b = await ClientPipe.OpenAsync(AccountB, CharacterB, Map, "Ping-Dos");
            await using var c = await ClientPipe.OpenAsync(AccountC, CharacterC, Map, "Ping-Tres");
            var party = Parties.Create(CharacterA);
            try
            {
                Assert.True(Parties.Invite(party, CharacterB, CharacterA));
                Assert.True(Parties.Accept(party, CharacterB));

                using (SessionContext.Push(a.Session))
                {
                    await EntityPingHandler.PingAsync(a.ToClient,
                        ConnectionProtocol.Push(Op.Ijg, Pb.New().Var(1, 4).Var(2, CharacterB).Build()));
                }

                byte[] standing = EntityPingHandler.BuildStanding(CharacterB, 4);
                byte[] pinged = EntityPingHandler.BuildPinged(CharacterB, CharacterA);
                Assert.Equal(standing, await a.Next(Op.Ijq));
                Assert.Equal(pinged, await a.Next(Op.Ijf));
                Assert.Equal(standing, await b.Next(Op.Ijq));
                Assert.Equal(pinged, await b.Next(Op.Ijf));
                await c.Quiet();

                // A kind the client does not draw is not heard.
                using (SessionContext.Push(a.Session))
                {
                    await EntityPingHandler.PingAsync(a.ToClient,
                        ConnectionProtocol.Push(Op.Ijg, Pb.New().Var(1, 9).Var(2, CharacterB).Build()));
                }
                await a.Quiet();
            }
            finally
            {
                Parties.Dissolve(party);
            }
        }
    }
}

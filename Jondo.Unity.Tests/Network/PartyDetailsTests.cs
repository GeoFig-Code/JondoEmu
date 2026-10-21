using System.Linq;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Xunit;
using Wire = Jondo.Unity.Tests.Combat.PortalTests.Wire;

namespace Jondo.Unity.Tests.Network
{
    /// <summary>
    /// The invitation's «Detalles» (imd → ilb), as «recibir invitacion de grupo y aceptar» has it:
    /// f1 1, the members, who invites, the party, the invited, the leader.
    /// </summary>
    public sealed class PartyDetailsTests
    {
        [Fact]
        public async Task The_invited_player_sees_the_party_s_details()
        {
            const long leader = 9_300_000_001, guest = 9_300_000_002, stranger = 9_300_000_003;
            await using var invited = await Wire.Open(guest);
            await using var other = await Wire.Open(stranger);
            var party = Parties.Create(leader);
            Assert.True(Parties.Invite(party, guest, leader));
            try
            {
                byte[] imd = ConnectionProtocol.Push(Op.Imd, Pb.New().Var(2, party.Id).Build());
                using (SessionContext.Push(invited.Session))
                    await PartyHandler.DetailsAsync(invited.Session.Stream!, imd);
                var ilb = (await invited.Drain()).Single();
                Assert.Equal("ilb", ilb.Op);
                var fields = ProtoMessage.Parse(ilb.Payload).Fields;
                long Var(int number) => fields.Single(f => f.FieldNumber == number).VarIntValue;
                Assert.Equal(1, Var(1));
                Assert.Equal(leader, Var(4));
                Assert.Equal(party.Id, Var(5));
                Assert.Equal(leader, Var(7));
                var member = ProtoMessage.Parse(fields.Single(f => f.FieldNumber == 3).BytesValue).Fields;
                Assert.Equal(leader, member.Single(f => f.FieldNumber == 2).VarIntValue);

                // Somebody neither in it nor invited is told nothing.
                using (SessionContext.Push(other.Session))
                    await PartyHandler.DetailsAsync(other.Session.Stream!, imd);
                Assert.Empty(await other.Drain());
            }
            finally
            {
                Parties.Dissolve(party);
            }
        }
    }
}

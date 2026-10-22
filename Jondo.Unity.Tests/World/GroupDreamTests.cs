using System.Linq;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.Tests.Economy;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// A dream in a group: the owner's «Invitar al grupo» (iwt) brings each party member the
    /// invitation (iyd { f1 his name, f2 -300 }, «recibir invitacion a sueños»), the yes (ixk) takes
    /// him into the owner's room with the owner's state, only the owner starts the fights, and the
    /// owner leaving takes his guests out.
    /// </summary>
    [Collection("MapManager")]
    public class GroupDreamTests
    {
        [Fact]
        public async Task An_invited_member_follows_the_owner_and_leaves_with_him()
        {
            const long owner = 900_000_000_011, guest = 900_000_000_012, street = 191105026;
            if (!MapManager.WalkableCells.ContainsKey(Jail.MapId)) MapManager.Initialize();
            Interactives.Initialize();
            Dreams.OlvidarTodo();
            DreamHandler.ForgetGroups();
            var dream = Dreams.Crear(owner, "Dueno", 200, 2, street, 300);
            Dreams.Enter(dream, 0, out _);
            var room = dream.SalaActual;
            if (room == null || room.MapaDeLaSala == 0 || MapManager.GetMapInfo(room.MapaDeLaSala) == null) return;   // no client data

            var party = Parties.Create(owner);
            Parties.Invite(party, guest, owner);
            Parties.Accept(party, guest);
            await using var host = await ClientPipe.OpenAsync(990_000_301, owner, room.MapaDeLaSala, "Dueno");
            await using var visitor = await ClientPipe.OpenAsync(990_000_302, guest, street, "Invitado");
            try
            {
                using (SessionContext.Push(host.Session)) await DreamHandler.InviteGroupAsync(host.ToClient);
                var iyd = ProtoMessage.Parse(await visitor.Next(Op.Iyd));
                Assert.Equal("Dueno", System.Text.Encoding.UTF8.GetString(iyd.Fields.Single(f => f.FieldNumber == 1).BytesValue));
                Assert.Equal(-300, (int)iyd.Fields.Single(f => f.FieldNumber == 2).VarIntValue);

                using (SessionContext.Push(visitor.Session))
                    await DreamHandler.AnswerInvitationAsync(visitor.ToClient, ConnectionProtocol.Push(Op.Ixk, Pb.New().Var(1, 1).Build()));
                Assert.Equal(owner, DreamHandler.HostOf(guest));
                Assert.Equal(Op.Izg, (await visitor.NextAny()).Opcode);
                Assert.Equal(room.MapaDeLaSala, visitor.Session.State.MapId);

                using (SessionContext.Push(visitor.Session))
                    Assert.True(await DreamHandler.GuestMayNotFightAsync(visitor.ToClient));
                using (SessionContext.Push(host.Session))
                    Assert.False(await DreamHandler.GuestMayNotFightAsync(host.ToClient));

                using (SessionContext.Push(host.Session)) await DreamHandler.LeaveAsync(host.ToClient, ConnectionProtocol.Push(Op.Iyx));
                Assert.False(DreamHandler.IsGuest(guest));
                Assert.Equal(street, visitor.Session.State.MapId);
            }
            finally
            {
                Parties.Dissolve(party);
                Dreams.OlvidarTodo();
                DreamHandler.ForgetGroups();
            }
        }

        [Fact]
        public async Task A_no_leaves_the_member_where_he_is()
        {
            const long owner = 900_000_000_021, guest = 900_000_000_022, street = 191105026;
            Interactives.Initialize();
            Dreams.OlvidarTodo();
            DreamHandler.ForgetGroups();
            var dream = Dreams.Crear(owner, "Dueno", 200, 2, street, 300);
            Dreams.Enter(dream, 0, out _);
            var room = dream.SalaActual;
            if (room == null || room.MapaDeLaSala == 0) return;

            var party = Parties.Create(owner);
            Parties.Invite(party, guest, owner);
            Parties.Accept(party, guest);
            await using var host = await ClientPipe.OpenAsync(990_000_311, owner, room.MapaDeLaSala, "Dueno");
            await using var visitor = await ClientPipe.OpenAsync(990_000_312, guest, street, "Invitado");
            try
            {
                using (SessionContext.Push(host.Session)) await DreamHandler.InviteGroupAsync(host.ToClient);
                await visitor.Next(Op.Iyd);
                using (SessionContext.Push(visitor.Session))
                    await DreamHandler.AnswerInvitationAsync(visitor.ToClient, ConnectionProtocol.Push(Op.Ixk));
                Assert.False(DreamHandler.IsGuest(guest));
                Assert.Equal(street, visitor.Session.State.MapId);
            }
            finally
            {
                Parties.Dissolve(party);
                Dreams.OlvidarTodo();
                DreamHandler.ForgetGroups();
            }
        }
    }
}

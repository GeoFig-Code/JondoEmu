using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Xunit;
using Wire = Jondo.Unity.Tests.Combat.PortalTests.Wire;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// The party search through two players' sessions: an announcement posted, an application, the
    /// owner's acceptance, the member leaving and the announcement taken down -- each frame to whom
    /// the client expects it.
    /// </summary>
    [Collection("lobbies")]
    public sealed class LobbyFlowTests
    {
        private const long Account = 8_900_000_077;

        private static long Create(string name)
            => DatabaseManager.CreateCharacter(Account, 1, name, 8, 0, 1, new long[] { -1, -1, -1, -1, -1 },
                                               191105026, 1, 0, 0, Array.Empty<(int, int)>());

        private static string Text(byte[] payload, int field)
            => Encoding.UTF8.GetString(ProtoMessage.Parse(payload).Fields.First(f => f.FieldNumber == field).BytesValue);

        private static long Number(byte[] payload, int field)
            => ProtoMessage.Parse(payload).Fields.FirstOrDefault(f => f.FieldNumber == field)?.VarIntValue ?? 0;

        [Fact]
        public async Task An_application_goes_from_the_board_to_the_party()
        {
            Lobbies.Clear();
            long ownerId = Create("Anunciante-Uno"), applicantId = Create("Candidato-Dos");
            Assert.True(ownerId > 0 && applicantId > 0);
            try
            {
                await using var owner = await Wire.Open(ownerId);
                await using var applicant = await Wire.Open(applicantId);

                // Posting a closed announcement for two: kind 1, dungeon 92, level 1.
                using (SessionContext.Push(owner.Session))
                    await LobbyHandler.PostAsync(owner.Session.Stream!, ConnectionProtocol.Push(Op.Jnv,
                        Pb.New().Var(3, 1).Var(5, 2).Var(7, 92).Var(9, 1).Str(4, "¿vamos?").Build()));
                var posted = (await owner.Drain()).Single();
                Assert.Equal("joq", posted.Op);
                Assert.Equal(0, Number(posted.Payload, 1));
                string uid = Text(posted.Payload, 2);

                // The board, and the application: the owner hears of it.
                using (SessionContext.Push(applicant.Session))
                {
                    await LobbyHandler.BoardAsync(applicant.Session.Stream!, ConnectionProtocol.Push(Op.Jmu, Array.Empty<byte>()));
                    await LobbyHandler.ApplyAsync(applicant.Session.Stream!, ConnectionProtocol.Push(Op.Joc, Pb.New().Str(1, uid).Build()));
                }
                var seen = await applicant.Drain();
                Assert.Equal(new[] { "jpx", "jnn" }, seen.Select(f => f.Op));
                var board = ProtoMessage.Parse(ProtoMessage.Parse(seen[0].Payload).Fields.Single().BytesValue);
                Assert.Equal(uid, Text(board.Fields.Single(f => f.FieldNumber == 1).BytesValue, 6));
                Assert.Equal(Lobbies.ApplyCode.Applied, Number(seen[1].Payload, 1));
                var heard = (await owner.Drain()).Single();
                Assert.Equal("jpq", heard.Op);
                string applicantUid = Text(ProtoMessage.Parse(heard.Payload).Fields.Single(f => f.FieldNumber == 1).BytesValue, 5);
                Assert.Equal(Lobbies.UidOf(applicantId), applicantUid);

                // The owner's own announcement lists him; he accepts, and both see the new member.
                using (SessionContext.Push(owner.Session))
                {
                    await LobbyHandler.MineAsync(owner.Session.Stream!, ConnectionProtocol.Push(Op.Jpr, Array.Empty<byte>()));
                    await LobbyHandler.AnswerAsync(owner.Session.Stream!, ConnectionProtocol.Push(Op.Jom,
                        Pb.New().Str(1, applicantUid).Var(2, 1).Build()));
                }
                var ownerSees = await owner.Drain();
                Assert.Equal(new[] { "jng", "jpe", "jpk" }, ownerSees.Select(f => f.Op));
                Assert.Equal(Lobbies.AnswerCode.Accepted, Number(ownerSees[1].Payload, 1));
                var applicantSees = await applicant.Drain();
                Assert.Equal(new[] { "jmv", "jpk" }, applicantSees.Select(f => f.Op));
                Assert.Equal(1, Number(applicantSees[0].Payload, 2));
                Assert.Equal(new long[] { ownerId, applicantId }, Lobbies.ByUid(uid).Members.Select(m => m.CharacterId));

                // The member leaves: both hear him go.
                using (SessionContext.Push(applicant.Session))
                    await LobbyHandler.LeaveAsync(applicant.Session.Stream!, ConnectionProtocol.Push(Op.Jny, Pb.New().Str(1, uid).Build()));
                Assert.Equal(new[] { "jor", "jol" }, (await applicant.Drain()).Select(f => f.Op));
                Assert.Equal(new[] { "jor" }, (await owner.Drain()).Select(f => f.Op));

                // Taken down: the board's watcher hears it, the owner gets the jnz before the jnk.
                using (SessionContext.Push(owner.Session))
                    await LobbyHandler.DeleteAsync(owner.Session.Stream!, ConnectionProtocol.Push(Op.Jog, Pb.New().Str(1, uid).Build()));
                Assert.Equal(new[] { "jnz", "jnk" }, (await owner.Drain()).Select(f => f.Op));
                Assert.Equal(new[] { "jnz" }, (await applicant.Drain()).Select(f => f.Op));
                Assert.Null(Lobbies.ByUid(uid));
            }
            finally
            {
                Lobbies.Clear();
                DatabaseManager.DeleteCharacter(ownerId, Account);
                DatabaseManager.DeleteCharacter(applicantId, Account);
            }
        }
    }
}

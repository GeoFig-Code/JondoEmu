using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Xunit;
using Wire = Jondo.Unity.Tests.Combat.PortalTests.Wire;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// Who comes into a guild and who goes, as the client asks for it: the jlt answers an
    /// application, the jjn invites, the jlv withdraws one's own, the jho leaves or expels. Read off
    /// the client and measured in «comprar gremialogema-crear gremio-valorar candidatura-rechazarla-invitar a gremio».
    /// </summary>
    [Collection("guild raids")]
    public class GuildApplicationTests : IDisposable
    {
        private const long Leader = 7201, Applicant = 7202, Member = 7203;
        private readonly string _file;

        public GuildApplicationTests()
        {
            _file = Path.Combine(Path.GetTempPath(), $"jondo-candidaturas-{Guid.NewGuid():N}.db");
            GuildStore.ConnectionStringOverride = $"Data Source={_file}";
        }

        public void Dispose()
        {
            GuildStore.ConnectionStringOverride = null;
            TestDatabases.ReleasePool(_file);
            try { File.Delete(_file); } catch (IOException) { }
        }

        private static string Hex(byte[] bytes) => string.Concat(bytes.Select(b => b.ToString("x2")));

        private static long Field(byte[] payload, int number)
            => (long)(ProtoMessage.Parse(payload).Fields.FirstOrDefault(f => f.FieldNumber == number && f.WireType == 0)?.VarIntValue ?? 0);

        /// <summary>The jly the real server answers a refusal with, byte for byte.</summary>
        [Fact]
        public void The_refusals_jly_is_the_captures()
        {
            var application = new GuildStore.Application
            {
                GuildId = 42043, CharacterId = 182801072418, Message = "Hola dame gremio o te baneo", WhenMs = 1786568006510,
            };
            Assert.Equal("0801125a08eeb681beff331a1b486f6c612064616d65206772656d696f206f2074652062616e656f2234080a180120aba8a11a2a0c4869696572626974612d587832043532303738a282acfea805420746656c697039374a02080150c60118a28280c8e708",
                         Hex(GuildProtocol.BuildApplicationGone(application, "Hiierbita-Xx", 198, 55071787, "Felip97", "5207",
                                                                302677754146)));
        }

        [Fact]
        public async Task A_refused_application_leaves_and_nobody_joins()
        {
            var guild = GuildStore.Create(Leader, "Jondo", 165, 8, 16744448, 9476018);
            GuildStore.Apply(Applicant, guild.Id, "hola");

            await using var leader = await Wire.Open(Leader);
            using (SessionContext.Push(leader.Session))
                await GuildHandler.AnswerApplicationAsync(leader.Session.Stream!,
                    ConnectionProtocol.Push(Op.Jlt, Pb.New().Var(2, Applicant).Build()));

            Assert.Null(GuildStore.ApplicationOf(guild.Id, Applicant));
            Assert.Null(GuildStore.GuildOf(Applicant));
        }

        [Fact]
        public async Task An_accepted_application_joins_the_applicant()
        {
            var guild = GuildStore.Create(Leader, "Jondo", 165, 8, 16744448, 9476018);
            GuildStore.Apply(Applicant, guild.Id, "hola");

            await using var leader = await Wire.Open(Leader);
            using (SessionContext.Push(leader.Session))
                await GuildHandler.AnswerApplicationAsync(leader.Session.Stream!,
                    ConnectionProtocol.Push(Op.Jlt, Pb.New().Var(1, 1).Var(2, Applicant).Build()));

            Assert.Null(GuildStore.ApplicationOf(guild.Id, Applicant));
            Assert.Equal(guild.Id, GuildStore.GuildOf(Applicant)?.Id);
            Assert.Equal(GuildStore.RankNewcomer, GuildStore.RankOf(Applicant));
        }

        /// <summary>Right 6, "Gestionar las candidaturas e invitaciones": a newcomer does not have it.</summary>
        [Fact]
        public async Task Without_the_right_an_application_stays()
        {
            var guild = GuildStore.Create(Leader, "Jondo", 165, 8, 16744448, 9476018);
            GuildStore.Join(Member, guild.Id);
            GuildStore.Apply(Applicant, guild.Id, "hola");

            await using var member = await Wire.Open(Member);
            using (SessionContext.Push(member.Session))
                await GuildHandler.AnswerApplicationAsync(member.Session.Stream!,
                    ConnectionProtocol.Push(Op.Jlt, Pb.New().Var(1, 1).Var(2, Applicant).Build()));

            Assert.NotNull(GuildStore.ApplicationOf(guild.Id, Applicant));
            Assert.Null(GuildStore.GuildOf(Applicant));
        }

        /// <summary>
        /// The jjn invites: the invited gets the jiq, the inviter a jin at 1 at once; on accepting,
        /// the inviter gets the lqn 1423 and a jin at 3.
        /// </summary>
        [Fact]
        public async Task The_jjn_invites_and_the_inviter_is_told()
        {
            var guild = GuildStore.Create(Leader, "Jondo", 165, 8, 16744448, 9476018);
            await using var leader = await Wire.Open(Leader);
            await using var invited = await Wire.Open(Applicant);

            using (SessionContext.Push(leader.Session))
                await GuildHandler.InviteFromClientAsync(leader.Session.Stream!,
                    ConnectionProtocol.Push(Op.Jjn, Pb.New().Var(2, Applicant).Build()));

            var told = await leader.Drain();
            Assert.Equal(GuildProtocol.StatusInvited, Field(Assert.Single(told, f => f.Op == Op.Jin).Payload, 3));
            Assert.Contains(await invited.Drain(), f => f.Op == Op.Jiq);

            using (SessionContext.Push(invited.Session))
                await GuildHandler.AnswerInvitationAsync(invited.Session.Stream!,
                    ConnectionProtocol.Push(Op.Jiz, Pb.New().Var(1, 1).Build()));

            Assert.Equal(guild.Id, GuildStore.GuildOf(Applicant)?.Id);
            var after = await leader.Drain();
            Assert.Equal(InfoMessages.GuildMemberJoined, Field(Assert.Single(after, f => f.Op == Op.Lqn).Payload, 2));
            Assert.Equal(GuildProtocol.StatusJoined, Field(after.Last(f => f.Op == Op.Jin).Payload, 3));
        }

        /// <summary>
        /// The jho with somebody else's id expels him -- the members window's kick popup sends it --,
        /// and with one's own it leaves: expelling used to make the leader leave instead.
        /// </summary>
        [Fact]
        public async Task The_jho_expels_with_another_id_and_leaves_with_ones_own()
        {
            var guild = GuildStore.Create(Leader, "Jondo", 165, 8, 16744448, 9476018);
            GuildStore.Join(Member, guild.Id);
            GuildStore.Join(Applicant, guild.Id);

            await using var leader = await Wire.Open(Leader);
            using (SessionContext.Push(leader.Session))
                await GuildHandler.LeaveAsync(leader.Session.Stream!, ConnectionProtocol.Push(Op.Jho, Pb.New().Var(1, Member).Build()));
            Assert.Null(GuildStore.GuildOf(Member));
            Assert.Equal(guild.Id, GuildStore.GuildOf(Leader)?.Id);

            // A newcomer has not right 13, "Excluir a un miembro".
            await using var newcomer = await Wire.Open(Applicant);
            using (SessionContext.Push(newcomer.Session))
                await GuildHandler.LeaveAsync(newcomer.Session.Stream!, ConnectionProtocol.Push(Op.Jho, Pb.New().Var(1, Leader).Build()));
            Assert.Equal(guild.Id, GuildStore.GuildOf(Leader)?.Id);
            Assert.Equal(guild.Id, GuildStore.GuildOf(Applicant)?.Id);

            using (SessionContext.Push(newcomer.Session))
                await GuildHandler.LeaveAsync(newcomer.Session.Stream!, ConnectionProtocol.Push(Op.Jho, Pb.New().Var(1, Applicant).Build()));
            Assert.Null(GuildStore.GuildOf(Applicant));
        }

        /// <summary>
        /// The directory's jlw applies: f11 the text, f13 the guild. The guild's members get the jma
        /// with the applicant, and the applicant his pending application.
        /// </summary>
        [Fact]
        public async Task The_jlw_applies_and_the_guild_is_told()
        {
            var guild = GuildStore.Create(Leader, "Jondo", 165, 8, 16744448, 9476018);
            await using var leader = await Wire.Open(Leader);
            await using var applicant = await Wire.Open(Applicant);

            using (SessionContext.Push(applicant.Session))
                await GuildHandler.ApplyFromClientAsync(applicant.Session.Stream!, ConnectionProtocol.Push(Op.Jlw,
                    Pb.New().Str(1, "Name ASC").Str(11, "Hola, ¿me dejáis entrar?").Var(13, guild.Id).Var(14, 12).Build()));

            Assert.Equal("Hola, ¿me dejáis entrar?", GuildStore.ApplicationOf(guild.Id, Applicant)?.Message);
            Assert.Equal(Applicant, Field(Assert.Single(await leader.Drain(), f => f.Op == Op.Jma).Payload, 1));
            Assert.Contains(await applicant.Drain(), f => f.Op == Op.Jme);

            // Rewritten from the directory (jmo), with the text in f3.
            using (SessionContext.Push(applicant.Session))
                await GuildHandler.RewriteApplicationAsync(applicant.Session.Stream!, ConnectionProtocol.Push(Op.Jmo,
                    Pb.New().Var(2, guild.Id).Str(3, new string('a', 300)).Build()));
            Assert.Equal(new string('a', 200), GuildStore.ApplicationOf(guild.Id, Applicant)?.Message);

            // And answered: the applicant is told (jmp, f2 accepted).
            using (SessionContext.Push(leader.Session))
                await GuildHandler.AnswerApplicationAsync(leader.Session.Stream!,
                    ConnectionProtocol.Push(Op.Jlt, Pb.New().Var(1, 1).Var(2, Applicant).Build()));
            Assert.Equal(1, Field(Assert.Single(await applicant.Drain(), f => f.Op == Op.Jmp).Payload, 2));
        }

        /// <summary>A second application takes the first one back: one pending at a time.</summary>
        [Fact]
        public async Task Applying_elsewhere_takes_the_first_application_back()
        {
            var first = GuildStore.Create(Leader, "Jondo", 165, 8, 16744448, 9476018);
            var second = GuildStore.Create(Member, "Otro", 165, 8, 16744448, 9476018);
            await using var applicant = await Wire.Open(Applicant);

            using (SessionContext.Push(applicant.Session))
            {
                await GuildHandler.ApplyFromClientAsync(applicant.Session.Stream!,
                    ConnectionProtocol.Push(Op.Jlw, Pb.New().Str(11, "uno").Var(13, first.Id).Build()));
                await GuildHandler.ApplyFromClientAsync(applicant.Session.Stream!,
                    ConnectionProtocol.Push(Op.Jlw, Pb.New().Str(11, "dos").Var(13, second.Id).Build()));
            }

            Assert.Null(GuildStore.ApplicationOf(first.Id, Applicant));
            Assert.Equal("dos", GuildStore.ApplicationOf(second.Id, Applicant)?.Message);
        }

        /// <summary>
        /// The jlu, on opening the directory, is answered with the jme: empty with no application,
        /// as in every capture.
        /// </summary>
        [Fact]
        public async Task The_jlu_is_answered_with_an_empty_jme_when_there_is_nothing()
        {
            await using var applicant = await Wire.Open(Applicant);
            using (SessionContext.Push(applicant.Session))
                await GuildHandler.OwnApplicationAsync(applicant.Session.Stream!);
            Assert.Empty(Assert.Single(await applicant.Drain(), f => f.Op == Op.Jme).Payload);
        }

        /// <summary>The jme's shape, which is how the client reads it: f1 { f1 the guild, f2 the application }.</summary>
        [Fact]
        public void The_own_application_carries_the_guild_and_the_application()
        {
            var guild = new GuildStore.Guild { Id = 42043, Name = "Jondo", Level = 1 };
            var application = new GuildStore.Application { GuildId = 42043, CharacterId = 182801072418, Message = "hola", WhenMs = 1786568006510 };
            var jme = ProtoMessage.Parse(GuildProtocol.BuildOwnApplication(guild, application, "Hiierbita-Xx", 198, 55071787, "Felip97", "5207"));

            var pair = ProtoMessage.Parse(Assert.Single(jme.Fields).BytesValue).Fields.ToList();
            var block = ProtoMessage.Parse(pair.Single(f => f.FieldNumber == 1).BytesValue).Fields.ToList();
            Assert.Equal(42043, (long)block.Single(f => f.FieldNumber == 2).VarIntValue);
            Assert.Equal("Jondo", System.Text.Encoding.UTF8.GetString(block.Single(f => f.FieldNumber == 3).BytesValue));
            var request = ProtoMessage.Parse(pair.Single(f => f.FieldNumber == 2).BytesValue).Fields.ToList();
            Assert.Equal("hola", System.Text.Encoding.UTF8.GetString(request.Single(f => f.FieldNumber == 3).BytesValue));
        }

        /// <summary>The jlv withdraws one's own application and the jmi says whether there was one.</summary>
        [Fact]
        public async Task Withdrawing_takes_the_application_away()
        {
            var guild = GuildStore.Create(Leader, "Jondo", 165, 8, 16744448, 9476018);
            GuildStore.Apply(Applicant, guild.Id, "hola");

            await using var applicant = await Wire.Open(Applicant);
            using (SessionContext.Push(applicant.Session))
                await GuildHandler.WithdrawApplicationAsync(applicant.Session.Stream!);

            Assert.Null(GuildStore.ApplicationOf(guild.Id, Applicant));
            Assert.Equal(1, Field(Assert.Single(await applicant.Drain(), f => f.Op == Op.Jmi).Payload, 2));

            using (SessionContext.Push(applicant.Session))
                await GuildHandler.WithdrawApplicationAsync(applicant.Session.Stream!);
            Assert.Equal(0, Field(Assert.Single(await applicant.Drain(), f => f.Op == Op.Jmi).Payload, 2));
        }
    }
}

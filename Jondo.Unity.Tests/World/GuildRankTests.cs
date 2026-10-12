using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Microsoft.Data.Sqlite;
using Xunit;
using Wire = Jondo.Unity.Tests.Combat.PortalTests.Wire;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// A member's rank from the members window (jgv {2 member, 4 rank}): who may give which, the day
    /// a member has to wait, and handing the guild over. Measured in
    /// «...-intentar asignar rango a otro».
    /// </summary>
    [Collection("guild raids")]
    public class GuildRankTests : IDisposable
    {
        private const long Leader = 7301, Second = 7302, Newcomer = 7303, Other = 7304;
        private readonly string _file;

        public GuildRankTests()
        {
            _file = Path.Combine(Path.GetTempPath(), $"jondo-rangos-{Guid.NewGuid():N}.db");
            GuildStore.ConnectionStringOverride = $"Data Source={_file}";
        }

        public void Dispose()
        {
            GuildStore.ConnectionStringOverride = null;
            TestDatabases.ReleasePool(_file);
            try { File.Delete(_file); } catch (IOException) { }
        }

        private static string Hex(byte[] bytes) => string.Concat(bytes.Select(b => b.ToString("x2")));

        /// <summary>Everybody in as if a day ago, so the rule of the day does not get in the way.</summary>
        private void JoinedLongAgo()
        {
            using var connection = new SqliteConnection(GuildStore.ConnectionStringOverride);
            connection.Open();
            using var update = connection.CreateCommand();
            update.CommandText = "UPDATE GuildMembers SET JoinedUtcMs = 0;";
            update.ExecuteNonQuery();
        }

        private static async Task Give(Wire who, long member, int rank)
        {
            using (SessionContext.Push(who.Session))
                await GuildHandler.AssignRankAsync(who.Session.Stream!,
                    ConnectionProtocol.Push(Op.Jgv, Pb.New().Var(2, member).Var(4, rank).Build()));
        }

        /// <summary>The refusal, byte for byte: Warning, message 740, "24".</summary>
        [Fact]
        public void The_refusal_is_the_captures()
        {
            Assert.Equal("080110e40522023234", Hex(ConnectionProtocol.BuildInfoMessage(
                InfoMessages.Warning, InfoMessages.RankChangeTooSoon, GuildStore.RankChangeMinimumHours.ToString())));
        }

        /// <summary>A member who joined less than a day ago keeps his rank, and the leader is told why.</summary>
        [Fact]
        public async Task A_member_of_less_than_a_day_keeps_his_rank()
        {
            var guild = GuildStore.Create(Leader, "Jondo", 165, 8, 16744448, 9476018);
            GuildStore.Join(Newcomer, guild.Id);
            await using var leader = await Wire.Open(Leader);

            await Give(leader, Newcomer, 1);

            Assert.Equal(GuildStore.RankNewcomer, GuildStore.RankOf(Newcomer));
            Assert.Equal(GuildStore.RankLeader, GuildStore.RankOf(Leader));
            var lqn = Assert.Single(await leader.Drain(), f => f.Op == Op.Lqn).Payload;
            Assert.Equal("080110e40522023234", Hex(lqn));
        }

        [Fact]
        public async Task The_leader_gives_a_rank()
        {
            var guild = GuildStore.Create(Leader, "Jondo", 165, 8, 16744448, 9476018);
            GuildStore.Join(Newcomer, guild.Id);
            JoinedLongAgo();
            await using var leader = await Wire.Open(Leader);

            await Give(leader, Newcomer, 2);

            Assert.Equal(2, GuildStore.RankOf(Newcomer));
        }

        /// <summary>
        /// Right 26 gives ranks below one's own to members below oneself: rank 2 carries it, so it
        /// makes a newcomer a 3, but neither a 2 nor the leader, and does not touch the leader.
        /// </summary>
        [Fact]
        public async Task Right_26_gives_only_ranks_below_ones_own()
        {
            var guild = GuildStore.Create(Leader, "Jondo", 165, 8, 16744448, 9476018);
            GuildStore.Join(Second, guild.Id, rank: 2);
            GuildStore.Join(Newcomer, guild.Id);
            JoinedLongAgo();
            await using var second = await Wire.Open(Second);

            await Give(second, Newcomer, 2);
            Assert.Equal(GuildStore.RankNewcomer, GuildStore.RankOf(Newcomer));
            await Give(second, Leader, 4);
            Assert.Equal(GuildStore.RankLeader, GuildStore.RankOf(Leader));
            await Give(second, Newcomer, 3);
            Assert.Equal(3, GuildStore.RankOf(Newcomer));
        }

        [Fact]
        public async Task Without_the_right_nothing_changes()
        {
            var guild = GuildStore.Create(Leader, "Jondo", 165, 8, 16744448, 9476018);
            GuildStore.Join(Newcomer, guild.Id);
            GuildStore.Join(Other, guild.Id);
            JoinedLongAgo();
            await using var newcomer = await Wire.Open(Newcomer);

            await Give(newcomer, Other, 3);

            Assert.Equal(GuildStore.RankNewcomer, GuildStore.RankOf(Other));
        }

        /// <summary>Giving the leader's rank hands the guild over: the old leader takes the other's rank.</summary>
        [Fact]
        public async Task Giving_the_leaders_rank_hands_the_guild_over()
        {
            var guild = GuildStore.Create(Leader, "Jondo", 165, 8, 16744448, 9476018);
            GuildStore.Join(Second, guild.Id, rank: 2);
            JoinedLongAgo();
            await using var leader = await Wire.Open(Leader);

            await Give(leader, Second, GuildStore.RankLeader);

            Assert.Equal(GuildStore.RankLeader, GuildStore.RankOf(Second));
            Assert.Equal(2, GuildStore.RankOf(Leader));
        }
    }
}

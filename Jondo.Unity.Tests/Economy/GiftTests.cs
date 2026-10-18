using System;
using System.Linq;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Jondo.Unity.Tests.Economy
{
    /// <summary>
    /// The shop's gifts: listed as jtg, handed to the character playing with the capture's jwj,
    /// iua and weight, and to one who is not straight into his bag.
    /// </summary>
    [Collection("forgemagic")]
    public sealed class GiftTests
    {
        private const long Account = 8_900_000_001;
        private const int Rune = 1523;

        private static long Create(string name)
            => DatabaseManager.CreateCharacter(Account, 1, name, 8, 0, 1, new long[] { -1, -1, -1, -1, -1 },
                                               191105026, 1, 0, 0, Array.Empty<(int, int)>());

        private static int ItemsOf(long character)
        {
            using var connection = new SqliteConnection(DatabaseManager.WorldConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT IFNULL(SUM(Quantity), 0) FROM CharacterItems WHERE CharacterId = $c AND Gid = $g;";
            command.Parameters.AddWithValue("$c", character);
            command.Parameters.AddWithValue("$g", Rune);
            return Convert.ToInt32(command.ExecuteScalar());
        }

        [Fact]
        public async Task A_gift_is_listed_and_handed_to_whoever_takes_it()
        {
            foreach (var old in Gifts.Of(Account)) Gifts.Take(Account, old.Id);
            long playing = Create("Regalado-Uno"), resting = Create("Regalado-Dos");
            Assert.True(playing > 0 && resting > 0);
            try
            {
                int first = Gifts.Grant(Account, Rune, 3, "Un regalo", "Para probar");
                int second = Gifts.Grant(Account, Rune, 2, "Otro regalo", "");

                var list = ProtoMessage.Parse(GiftHandler.BuildList(Account)).Fields.Where(f => f.FieldNumber == 3).ToList();
                Assert.Equal(2, list.Count);
                var entry = ProtoMessage.Parse(list[0].BytesValue);
                var item = ProtoMessage.Parse(ProtoMessage.Parse(entry.Fields.Single(f => f.FieldNumber == 1).BytesValue)
                    .Fields.Single(f => f.FieldNumber == 3).BytesValue);
                Assert.Equal(Rune, item.Fields.Single(f => f.FieldNumber == 3).VarIntValue);
                Assert.Equal(3, item.Fields.Single(f => f.FieldNumber == 2).VarIntValue);

                await using var pipe = await ClientPipe.OpenAsync(Account, playing, 191105026, "Regalado-Uno");
                using (SessionContext.Push(pipe.Session))
                {
                    // A gift granted while playing shows at once: the list again.
                    await GiftHandler.TellAsync(Account);
                    Assert.Equal(GiftHandler.BuildList(Account), await pipe.Next(Op.Jtg));

                    // To the one playing: jwj, the item and the weight, as the shop capture has it.
                    await GiftHandler.ClaimAsync(pipe.ToClient, ConnectionProtocol.Push("jwm",
                        Pb.New().Var(1, first).Var(2, playing).Build()));
                    var jwj = ProtoMessage.Parse(await pipe.Next("jwj")).Fields;
                    Assert.Equal(first, jwj.Single(f => f.FieldNumber == 2).VarIntValue);
                    Assert.Equal(1, jwj.Single(f => f.FieldNumber == 3).VarIntValue);
                    await pipe.Next(Op.Iua);
                    await pipe.Next(Op.Iun);
                    Assert.Equal(3, ItemsOf(playing));

                    // Taken already: refused, nothing given.
                    await GiftHandler.ClaimAsync(pipe.ToClient, ConnectionProtocol.Push("jwm",
                        Pb.New().Var(1, first).Var(2, playing).Build()));
                    Assert.DoesNotContain(ProtoMessage.Parse(await pipe.Next("jwj")).Fields, f => f.FieldNumber == 3);

                    // All of them to the other character, who is not playing: into his bag.
                    await GiftHandler.ClaimAllAsync(pipe.ToClient, ConnectionProtocol.Push("jth", Pb.New().Var(2, resting).Build()));
                    Assert.Equal(second, ProtoMessage.Parse(await pipe.Next("jwj")).Fields.Single(f => f.FieldNumber == 2).VarIntValue);
                    Assert.Equal(2, ItemsOf(resting));
                    Assert.Empty(Gifts.Of(Account));
                }
            }
            finally
            {
                foreach (var gift in Gifts.Of(Account)) Gifts.Take(Account, gift.Id);
                DatabaseManager.DeleteCharacter(playing, Account);
                DatabaseManager.DeleteCharacter(resting, Account);
            }
        }
    }
}

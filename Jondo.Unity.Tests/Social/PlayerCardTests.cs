using System;
using System.Linq;
using System.Threading.Tasks;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.Tests.Economy;
using Xunit;

namespace Jondo.Unity.Tests.Social
{
    /// <summary>A player's card, in the shape of «Busqueda grupo/…postularse a otro…»'s lad.</summary>
    public sealed class PlayerCardTests
    {
        private const long Account = 8_900_000_071;

        [Fact]
        public async Task The_card_carries_the_character_and_his_looks()
        {
            Wardrobe.Initialize();
            long character = DatabaseManager.CreateCharacter(Account, 1, "Ficha-Uno", 11, 0, 1,
                new long[] { -1, -1, -1, -1, -1 }, 191105026, 120, 0, 0, Array.Empty<(int, int)>());
            Assert.True(character > 0);
            try
            {
                await using var pipe = await ClientPipe.OpenAsync(Account, character, 990_100_071, "Ficha-Uno");
                using (SessionContext.Push(pipe.Session))
                {
                    await PlayerCardHandler.ShowAsync(pipe.ToClient, ConnectionProtocol.Push("kxh", Pb.New().Var(2, character).Build()));
                    var lad = ProtoMessage.Parse(await pipe.Next("lad")).Fields;
                    var body = ProtoMessage.Parse(lad.Single(f => f.FieldNumber == 2).BytesValue).Fields;
                    Assert.True(DateTime.TryParse(System.Text.Encoding.UTF8.GetString(body.Single(f => f.FieldNumber == 3).BytesValue), out _));
                    var card = ProtoMessage.Parse(body.Single(f => f.FieldNumber == 4).BytesValue).Fields;
                    long Number(int n) => card.Single(f => f.FieldNumber == n).VarIntValue;
                    Assert.Equal(Account, Number(2));
                    Assert.Equal(11, Number(3));
                    Assert.Equal(character, Number(4));
                    Assert.Equal(1, Number(6));       // online: he is the one asking
                    Assert.Equal(120, Number(7));
                    Assert.Equal("Ficha-Uno", System.Text.Encoding.UTF8.GetString(card.Single(f => f.FieldNumber == 11).BytesValue));
                    Assert.Contains(card, f => f.FieldNumber == 8);
                    Assert.Contains(card, f => f.FieldNumber == 14);
                    Assert.DoesNotContain(card, f => f.FieldNumber == 10);   // male
                    Assert.DoesNotContain(card, f => f.FieldNumber == 9);    // no background picked

                    // The background window's kye { 2 background }: the card carries it from then on.
                    await PlayerCardHandler.SetBackgroundAsync(ConnectionProtocol.Push("kye", Pb.New().Var(2, 5).Build()));
                    await PlayerCardHandler.ShowAsync(pipe.ToClient, ConnectionProtocol.Push("kxh", Pb.New().Var(2, character).Build()));
                    var again = ProtoMessage.Parse(await pipe.Next("lad")).Fields;
                    var card2 = ProtoMessage.Parse(ProtoMessage.Parse(again.Single(f => f.FieldNumber == 2).BytesValue)
                        .Fields.Single(f => f.FieldNumber == 4).BytesValue).Fields;
                    Assert.Equal(5, card2.Single(f => f.FieldNumber == 9).VarIntValue);

                    // A character that does not exist: nothing.
                    await PlayerCardHandler.ShowAsync(pipe.ToClient, ConnectionProtocol.Push("kxh", Pb.New().Var(2, 1).Build()));
                    await pipe.Quiet();
                }
            }
            finally
            {
                DatabaseManager.DeleteCharacter(character, Account);
            }
        }
    }
}

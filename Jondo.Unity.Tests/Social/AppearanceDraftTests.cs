using System;
using System.Linq;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.Tests.Economy;
using Xunit;

namespace Jondo.Unity.Tests.Social
{
    /// <summary>The cosmetics window's draft switch: the preview, then the outfit being drafted.</summary>
    public sealed class AppearanceDraftTests
    {
        private const long Account = 8_900_000_072;

        private static ProtoField Field(byte[] message, int number)
            => ProtoMessage.Parse(message).Fields.Single(f => f.FieldNumber == number);

        [Fact]
        public async Task The_draft_switch_sends_the_preview_and_the_outfit_the_window_shows()
        {
            long character = DatabaseManager.CreateCharacter(Account, 1, "Borrador-Uno", 11, 0, 1,
                new long[] { -1, -1, -1, -1, -1 }, 191105026, 120, 0, 0, Array.Empty<(int, int)>());
            Assert.True(character > 0);
            try
            {
                await using var pipe = await ClientPipe.OpenAsync(Account, character, 990_100_072, "Borrador-Uno");
                using (SessionContext.Push(pipe.Session))
                {
                    await AppearanceHandler.SendStateAsync(pipe.ToClient, ConnectionProtocol.Push("lyy", Array.Empty<byte>()));
                    await pipe.Next(Op.Lxc);
                    byte[] state = Field(await pipe.Next(Op.Lxo), 3).BytesValue;

                    await AppearanceHandler.DraftAsync(pipe.ToClient);
                    await pipe.Next(Op.Lxc);
                    byte[] draft = Field(await pipe.Next(Op.Lwq), 1).BytesValue;

                    // The same outfit as the window's state: its draft id and its look.
                    Assert.Equal(Field(state, 7).BytesValue, Field(draft, 7).BytesValue);
                    Assert.Equal(Field(state, 12).BytesValue, Field(draft, 12).BytesValue);
                }
            }
            finally
            {
                DatabaseManager.DeleteCharacter(character, Account);
            }
        }
    }
}

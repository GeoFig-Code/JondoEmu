using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.Tests.Economy;
using Xunit;

namespace Jondo.Unity.Tests.Protocol
{
    /// <summary>
    /// The character creation's dice: asked with kwd, answered with a kvk whose name the client's
    /// own first naming rule accepts.
    /// </summary>
    public sealed class NameDiceTests
    {
        private static readonly Regex NamingRule = new(@"^([A-Z][a-z]+(\-[a-zA-Z][a-z]*){0,2})$");

        [Fact]
        public async Task The_dice_answers_a_name_the_client_takes()
        {
            await using var pipe = await ClientPipe.OpenAsync(8_910_000_001, 8_910_100_001, 0, "");
            for (int i = 0; i < 20; i++)
            {
                await CharacterCreationHandler.SuggestNameAsync(pipe.ToClient);
                var kvk = ProtoMessage.Parse(await pipe.Next(Op.Kvk)).Fields;
                string name = Encoding.UTF8.GetString(Assert.Single(kvk).BytesValue);
                Assert.Matches(NamingRule, name);
            }
        }
    }
}

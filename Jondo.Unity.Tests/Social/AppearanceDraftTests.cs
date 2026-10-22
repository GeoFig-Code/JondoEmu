using System;
using System.Linq;
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

        /// <summary>
        /// The bodies tab (lyo): the Sacrier's «retro» body (110, skin 5881) is taken and goes first
        /// in the look; its payable one (66) and a Feca's (2) are answered lyz without f1.
        /// </summary>
        [Fact]
        public async Task The_bodies_tab_puts_the_body_first_in_the_look()
        {
            const int Retro = 110, RetroSkin = 5881, Payable = 66, Feca = 2;
            Wardrobe.Initialize();
            long character = DatabaseManager.CreateCharacter(Account + 2, 1, "Cuerpo-Uno", 11, 0, 1,
                new long[] { -1, -1, -1, -1, -1 }, 191105026, 50, 0, 0, Array.Empty<(int, int)>());
            Assert.True(character > 0);
            try
            {
                await using var pipe = await ClientPipe.OpenAsync(Account + 2, character, 990_100_074, "Cuerpo-Uno");
                using (SessionContext.Push(pipe.Session))
                {
                    byte[] Lyo(int body) => ConnectionProtocol.Push(Op.Lyo, Pb.New().Var(2, body).Build());

                    await AppearanceHandler.BodyAsync(pipe.ToClient, Lyo(Retro));
                    await pipe.Next(Op.Lxc);
                    Assert.Equal(1, Field(await pipe.Next(Op.Lyz), 1).VarIntValue);
                    Assert.Equal(Retro, Wardrobe.BodyOf(character));
                    string look = BreedLookTable.Drawable(DatabaseManager.GetCharacterById(character)!);
                    Assert.StartsWith(RetroSkin + ",", look.Split('|')[1]);

                    foreach (int refused in new[] { Payable, Feca })
                    {
                        await AppearanceHandler.BodyAsync(pipe.ToClient, Lyo(refused));
                        Assert.DoesNotContain(ProtoMessage.Parse(await pipe.Next(Op.Lyz)).Fields, f => f.FieldNumber == 1);
                        Assert.Equal(Retro, Wardrobe.BodyOf(character));
                    }
                }
            }
            finally
            {
                DatabaseManager.DeleteCharacter(character, Account + 2);
            }
        }

        /// <summary>
        /// The faces tab (lwl): a creation head of one's breed and sex is taken (lxc and lye f2 1);
        /// a payable one, or another breed's, is answered lye without f2 and leaves the face as it was.
        /// </summary>
        [Fact]
        public async Task The_faces_tab_takes_a_creation_head_of_ones_breed_and_sex()
        {
            const int Creation = 162, Payable = 620, OtherBreed = 1;
            long character = DatabaseManager.CreateCharacter(Account + 1, 1, "Cara-Uno", 11, 0, 1,
                new long[] { -1, -1, -1, -1, -1 }, 191105026, 50, 0, 0, Array.Empty<(int, int)>());
            Assert.True(character > 0);
            try
            {
                await using var pipe = await ClientPipe.OpenAsync(Account + 1, character, 990_100_073, "Cara-Uno");
                using (SessionContext.Push(pipe.Session))
                {
                    byte[] Lwl(int head) => ConnectionProtocol.Push(Op.Lwl, Pb.New().Var(1, head).Build());

                    await AppearanceHandler.FaceAsync(pipe.ToClient, Lwl(Creation));
                    await pipe.Next(Op.Lxc);
                    Assert.Equal(1, Field(await pipe.Next(Op.Lye), 2).VarIntValue);
                    Assert.Equal(Creation, DatabaseManager.GetCharacterById(character)!.HeadId);

                    foreach (int refused in new[] { Payable, OtherBreed })
                    {
                        await AppearanceHandler.FaceAsync(pipe.ToClient, Lwl(refused));
                        Assert.DoesNotContain(ProtoMessage.Parse(await pipe.Next(Op.Lye)).Fields, f => f.FieldNumber == 2);
                        Assert.Equal(Creation, DatabaseManager.GetCharacterById(character)!.HeadId);
                    }
                }
            }
            finally
            {
                DatabaseManager.DeleteCharacter(character, Account + 1);
            }
        }
    }
}

using System;
using System.Linq;
using System.Text;
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
    /// <summary>The appearance window's outfits list: keeping, renaming, putting on, copying and throwing away.</summary>
    public sealed class OutfitTests
    {
        private const long Account = 8_900_000_080;
        private const int Base = 26, Retro = 110, RetroIdle = 24;

        private static ProtoField? Field(byte[] message, int number)
            => ProtoMessage.Parse(message).Fields.FirstOrDefault(f => f.FieldNumber == number);

        private static string Text(byte[] message, int number) => Encoding.UTF8.GetString(Field(message, number)!.BytesValue);

        /// <summary>The next frame of that kind, past the redraws that come before it.</summary>
        private static async Task<byte[]> Until(ClientPipe pipe, string opcode)
        {
            for (int i = 0; i < 20; i++)
            {
                var (got, payload) = await pipe.NextAny();
                if (got == opcode) return payload;
            }
            throw new Xunit.Sdk.XunitException($"{opcode} never came");
        }

        [Fact]
        public async Task An_outfit_kept_is_renamed_put_on_copied_and_thrown_away()
        {
            Wardrobe.Initialize();
            Outfits.Initialize();
            long character = DatabaseManager.CreateCharacter(Account, 1, "Atuendo-Uno", 11, 0, 1,
                new long[] { -1, -1, -1, -1, -1 }, 191105026, 50, 0, 0, Array.Empty<(int, int)>());
            Assert.True(character > 0);
            try
            {
                await using var pipe = await ClientPipe.OpenAsync(Account, character, 990_100_080, "Atuendo-Uno");
                using (SessionContext.Push(pipe.Session))
                {
                    // The first outfit, the worn one: the breed's base body and the plain idle.
                    var me = DatabaseManager.GetCharacterById(character)!;
                    var lyt = ProtoMessage.Parse(ConnectionProtocol.BuildOutfits(me)).Fields;
                    byte[] first = Assert.Single(lyt, f => f.FieldNumber == 1).BytesValue;
                    string firstId = Text(first, 7);
                    Assert.Equal(ConnectionProtocol.OutfitIdOf(character), firstId);
                    Assert.Equal(Base, (int)Field(first, 5)!.VarIntValue);
                    Assert.Equal(IdleTable.Plain, (int)Field(first, 8)!.VarIntValue);

                    // Dressed «retro», kept as a new outfit with its name and icon.
                    await AppearanceHandler.BodyAsync(pipe.ToClient, ConnectionProtocol.Push(Op.Lyo, Pb.New().Var(2, Retro).Build()));
                    await Until(pipe, Op.Lyz);
                    await AppearanceHandler.IdleAsync(pipe.ToClient, ConnectionProtocol.Push(Op.Lwu, Pb.New().Var(1, RetroIdle).Build()));
                    await Until(pipe, Op.Lwn);

                    await AppearanceHandler.CreateOutfitAsync(pipe.ToClient, ConnectionProtocol.Push(Op.Lwk,
                        Pb.New().Var(1, 1).Var(2, 7).Str(4, "Ceremonia").Build()));
                    byte[] lxb = await pipe.Next(Op.Lxb);
                    Assert.Equal(1, (int)Field(lxb, 1)!.VarIntValue);
                    string kept = Text(lxb, 2);
                    byte[] outfit = Field(lxb, 3)!.BytesValue;
                    Assert.Equal(kept, Text(outfit, 7));
                    Assert.Equal("Ceremonia", Text(outfit, 13));
                    Assert.Equal(7, (int)Field(outfit, 15)!.VarIntValue);
                    Assert.Equal(Retro, (int)Field(outfit, 5)!.VarIntValue);
                    Assert.Equal(RetroIdle, (int)Field(outfit, 8)!.VarIntValue);

                    // Back to the base body; the kept outfit stays as it was kept.
                    await AppearanceHandler.BodyAsync(pipe.ToClient, ConnectionProtocol.Push(Op.Lyo, Pb.New().Var(2, Base).Build()));
                    await Until(pipe, Op.Lyz);
                    await AppearanceHandler.IdleAsync(pipe.ToClient, ConnectionProtocol.Push(Op.Lwu, Pb.New().Var(1, IdleTable.Plain).Build()));
                    await Until(pipe, Op.Lwn);

                    // Renamed, starred and given another icon.
                    await AppearanceHandler.UpdateOutfitAsync(pipe.ToClient, ConnectionProtocol.Push(Op.Lxt,
                        Pb.New().Str(1, "Fiesta").Var(2, 1).Str(3, kept).Var(5, 9).Build()));
                    Assert.Equal(1, (int)Field(await pipe.Next(Op.Lxr), 1)!.VarIntValue);
                    var stored = Outfits.Of(character).Single(o => o.Uuid == kept);
                    Assert.Equal(("Fiesta", 9, true), (stored.Name, stored.Icon, stored.Favorite));
                    Assert.Equal(Retro, stored.Kept!.Body);

                    // Put on: the live tables take the kept look, the first outfit keeps what was worn.
                    await AppearanceHandler.EquipAsync(pipe.ToClient, ConnectionProtocol.Push(Op.Lyy, Pb.New().Str(1, kept).Build()), Account);
                    Assert.Equal(1, (int)Field(await Until(pipe, Op.Lxo), 1)!.VarIntValue);
                    Assert.Equal(kept, Outfits.EquippedOf(character));
                    Assert.Equal(Retro, Wardrobe.BodyOf(character));
                    Assert.Equal(RetroIdle, Wardrobe.IdleOf(character));
                    var before = Outfits.Of(character).Single(o => o.Uuid == firstId);
                    Assert.Equal(Base, before.Kept!.Body);
                    Assert.StartsWith("5881,", BreedLookTable.Drawable(DatabaseManager.GetCharacterById(character)!).Split('|')[1]);

                    // The worn one is not thrown away; the other one is.
                    await AppearanceHandler.DeleteOutfitAsync(pipe.ToClient, ConnectionProtocol.Push(Op.Lyr, Pb.New().Str(2, kept).Build()));
                    Assert.Null(Field(await pipe.Next(Op.Lxl), 1));
                    await AppearanceHandler.DeleteOutfitAsync(pipe.ToClient, ConnectionProtocol.Push(Op.Lyr, Pb.New().Str(2, firstId).Build()));
                    Assert.Equal(1, (int)Field(await pipe.Next(Op.Lxl), 1)!.VarIntValue);
                    Assert.Single(Outfits.Of(character));

                    // A copy of the worn one, under its own name.
                    await AppearanceHandler.DuplicateOutfitAsync(pipe.ToClient, ConnectionProtocol.Push(Op.Lzd,
                        Pb.New().Var(1, 3).Str(2, kept).Str(3, "Copia").Build()));
                    byte[] copy = Field(await pipe.Next(Op.Lxb), 3)!.BytesValue;
                    Assert.Equal("Copia", Text(copy, 13));
                    Assert.Equal(Retro, (int)Field(copy, 5)!.VarIntValue);
                    Assert.Equal(2, Outfits.Of(character).Count);

                    // An outfit that is not one's own cannot be put on.
                    await AppearanceHandler.EquipAsync(pipe.ToClient, ConnectionProtocol.Push(Op.Lyy,
                        Pb.New().Str(1, Guid.NewGuid().ToString()).Build()), Account);
                    Assert.Null(Field(await pipe.Next(Op.Lxo), 1));
                    Assert.Equal(kept, Outfits.EquippedOf(character));
                }
            }
            finally
            {
                DatabaseManager.DeleteCharacter(character, Account);
            }
        }
    }
}

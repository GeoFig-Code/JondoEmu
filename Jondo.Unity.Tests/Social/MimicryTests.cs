using System;
using System.Collections.Generic;
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
    /// <summary>
    /// The mimisymbic (PR #73): a real item spent into the cosmetics collection, worn from there and
    /// never given back; only what the collection holds is worn; and a hidden empty slot kept.
    /// </summary>
    public sealed class MimicryTests
    {
        private const long Account = 8_900_000_075;
        private const int Mimobionte = 14485, RealHat = 629;

        private static long Number(byte[] payload, int field)
            => ProtoMessage.Parse(payload).Fields.FirstOrDefault(f => f.FieldNumber == field && f.WireType == 0)?.VarIntValue ?? 0;

        [Fact]
        public async Task A_mimisymbic_keeps_a_real_item_s_look_for_good()
        {
            Wardrobe.Initialize();
            CosmeticCollection.Initialize();
            CosmeticCollection.Take(Account, RealHat);
            long me = DatabaseManager.CreateCharacter(Account, 1, "Mimo-Uno", 5, 0, 1,
                new long[] { -1, -1, -1, -1, -1 }, 191105026, 100, 0, 0, Array.Empty<(int, int)>());
            Assert.True(me > 0);
            try
            {
                var mimic = DatabaseManager.AddItemToInventory(me, Mimobionte, 1);
                var hat = DatabaseManager.AddItemToInventory(me, RealHat, 1);
                await using var pipe = await ClientPipe.OpenAsync(Account, me, 191105026, "Mimo-Uno");
                using (SessionContext.Push(pipe.Session))
                {
                    pipe.Session.State.SetInventory(DatabaseManager.LoadInventory(me));
                    Equipment.LoadFrom(me);

                    // A real hat is worn by nobody before the mimisymbic: it is not in the collection.
                    await AppearanceHandler.WearAsync(pipe.ToClient, ConnectionProtocol.Push(Op.Lys, Pb.New().Var(1, RealHat).Build()));

                    await MimicryHandler.CreateAsync(pipe.ToClient,
                        ConnectionProtocol.Push(Op.Hmj, Pb.New().Var(1, mimic.Uid).Var(2, hat.Uid).Build()));
                    var frames = new List<(string Op, byte[] Payload)>();
                    do frames.Add(await pipe.NextAny()); while (frames[^1].Op != "hmh");

                    Assert.Equal(5, Number(frames[^1].Payload, 2));
                    Assert.DoesNotContain(frames, f => f.Op == Op.Lwz);   // the first try was not answered
                    Assert.Contains(frames, f => f.Op == "lyb");
                    Assert.True(CosmeticCollection.Has(Account, RealHat));
                    Assert.Null(Equipment.ByUid(mimic.Uid));
                    Assert.Null(Equipment.ByUid(hat.Uid));

                    // Now it is worn, in the hat's slot.
                    await AppearanceHandler.WearAsync(pipe.ToClient, ConnectionProtocol.Push(Op.Lys, Pb.New().Var(1, RealHat).Build()));
                    await pipe.Next(Op.Lxc);
                    Assert.Equal(Cosmetics.SlotHat, Number(await pipe.Next(Op.Lwz), 3));

                    // And it stays in the collection: taking it out is not heard.
                    await CosmeticCollectionHandler.RemoveAsync(pipe.ToClient,
                        ConnectionProtocol.Push("lxh", Pb.New().Var(1, RealHat).Build()), Account);
                    Assert.True(CosmeticCollection.Has(Account, RealHat));
                }
            }
            finally
            {
                CosmeticCollection.Take(Account, RealHat);
                DatabaseManager.DeleteCharacter(me, Account);
            }
        }

        [Fact]
        public void A_hidden_empty_slot_is_kept_until_it_is_shown_again()
        {
            Wardrobe.Initialize();
            const long character = 8_900_100_075;
            Wardrobe.SetHidden(character, Cosmetics.SlotShield, true);
            Assert.Contains(Wardrobe.AppearanceOf(character), w => w.Slot == Cosmetics.SlotShield && w.Gid == 0 && w.Hidden);

            Wardrobe.SetHidden(character, Cosmetics.SlotShield, false);
            Assert.DoesNotContain(Wardrobe.AppearanceOf(character), w => w.Slot == Cosmetics.SlotShield);
        }
    }
}

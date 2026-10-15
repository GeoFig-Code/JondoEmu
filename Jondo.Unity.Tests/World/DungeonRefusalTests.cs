using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.Tests.Economy;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    [Collection("MapManager")]
    public class DungeonRefusalTests
    {
        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, true, true)]
        [InlineData(true, false, true)]
        [InlineData(true, false, false)]
        public async Task An_unavailable_selected_item_sends_a_red_bold_notice_and_keeps_the_connection(
            bool useKeyring, bool hasKeyring, bool hasKey)
        {
            const int dungeonId = int.MaxValue, npcId = int.MaxValue;
            const long entrance = -990000001, keyReply = 24970;
            var dungeons = (Dictionary<int, DungeonManager.Dungeon>)DungeonManager.All;
            var guardians = (Dictionary<int, DungeonDoor.Options>)typeof(DungeonDoor)
                .GetField("_byNpc", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var dungeon = new DungeonManager.Dungeon
            {
                Id = 0, Name = "Cripta de Chadalid", EntranceMapId = entrance,
                OnKeyring = true,
            };
            dungeon.Rooms.Add(entrance - 1);
            dungeon.Required.Add((999999, 1));
            dungeons.Add(dungeonId, dungeon);
            guardians.Add(npcId, new DungeonDoor.Options { Key = keyReply, Keyring = keyReply + 1 });

            try
            {
                // Dungeon 0 avoids database access. The other entry item, if present, must never
                // be substituted for the one selected by the player.
                await using var pipe = await ClientPipe.OpenAsync(990000001, 990000002, entrance);
                using (SessionContext.Push(pipe.Session))
                {
                    pipe.Session.State.Language = "fr";
                    pipe.Session.State.CharacterLevel = 200;
                    pipe.Session.State.OpenDialogueNpcId = npcId;
                    if (hasKeyring) Equipment.Add(1, DungeonHandler.Keyring, 1, Equipment.Bag, null);
                    if (hasKey) Equipment.Add(2, 999999, 3, Equipment.Bag, null);
                    long selectedReply = useKeyring ? keyReply + 1 : keyReply;
                    Assert.Contains(keyReply, DungeonHandler.DoorReplies(npcId, entrance));

                    byte[] expected = ConnectionProtocol.BuildNotice(
                        "<color=#FF0000><b>Vous ne possédez pas l'objet nécessaire.</b></color>");
                    Assert.False(await DungeonHandler.AtTheDoorAsync(pipe.ToClient, entrance, selectedReply));
                    Assert.Equal(expected, await pipe.Next(Op.Lqn));
                    Assert.Equal(entrance, pipe.Session.State.MapId);
                    Assert.Equal(hasKeyring ? 1 : 0, Equipment.ByUid(1)?.Quantity ?? 0);
                    Assert.Equal(hasKey ? 3 : 0, Equipment.ByUid(2)?.Quantity ?? 0);

                    // Another request on the same TCP stream must still receive its response.
                    Assert.False(await DungeonHandler.AtTheDoorAsync(pipe.ToClient, entrance, selectedReply));
                    Assert.Equal(expected, await pipe.Next(Op.Lqn));
                    Assert.True(pipe.ToClient.CanWrite);
                }
            }
            finally
            {
                guardians.Remove(npcId);
                dungeons.Remove(dungeonId);
            }
        }

        [Theory]
        [InlineData(true, true, true, true, true)]
        [InlineData(true, true, true, false, false)]
        [InlineData(true, false, true, true, false)]
        [InlineData(true, true, false, true, true)]
        [InlineData(false, true, false, true, false)]
        [InlineData(false, true, true, true, true)]
        [InlineData(false, false, true, false, true)]
        [InlineData(false, false, false, false, false)]
        public async Task The_selected_method_never_falls_back_to_the_other_item(
            bool useKeyring, bool hasKeyring, bool hasKey, bool freeEntry, bool accepted)
        {
            await using var pipe = await ClientPipe.OpenAsync(990000003, 990000004, 1);
            using (SessionContext.Push(pipe.Session))
            {
                var dungeon = new DungeonManager.Dungeon { OnKeyring = true };
                dungeon.Required.Add((999999, 1));
                if (hasKeyring) Equipment.Add(1, DungeonHandler.Keyring, 1, Equipment.Bag, null);
                if (hasKey) Equipment.Add(2, 999999, 3, Equipment.Bag, null);
                var method = useKeyring ? DungeonHandler.EntryMethod.Keyring : DungeonHandler.EntryMethod.Key;

                Assert.Equal(accepted, DungeonHandler.TryTakeTheKey(dungeon, method, freeEntry,
                    out long uid, out int item));
                Assert.Equal(accepted && !useKeyring ? 2 : 0, uid);
                Assert.Equal(accepted ? (useKeyring ? DungeonHandler.Keyring : 999999) : 0, item);
                Assert.Equal(hasKeyring ? 1 : 0, Equipment.ByUid(1)?.Quantity ?? 0);
                Assert.Equal(hasKey ? 3 : 0, Equipment.ByUid(2)?.Quantity ?? 0);

                dungeon.OnKeyring = false;
                Assert.False(DungeonHandler.TryTakeTheKey(dungeon, DungeonHandler.EntryMethod.Keyring,
                    true, out _, out _));
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        public async Task A_key_entry_spends_one_key_and_announces_the_remaining_stack(int quantity)
        {
            const long character = 990000006, uid = 990000007;
            await using var pipe = await ClientPipe.OpenAsync(990000005, character, 1);
            using (SessionContext.Push(pipe.Session))
            {
                Assert.True(DatabaseManager.InsertCharacterItem(uid, character, 999999, quantity,
                    Equipment.Bag, null));
                try
                {
                    Equipment.Add(uid, 999999, quantity, Equipment.Bag, null);
                    Equipment.Add(1, DungeonHandler.Keyring, 1, Equipment.Bag, null);
                    var dungeon = new DungeonManager.Dungeon { OnKeyring = true };
                    dungeon.Required.Add((999999, 1));
                    Assert.True(DungeonHandler.TryTakeTheKey(dungeon, DungeonHandler.EntryMethod.Key,
                        true, out long selected, out _));

                    Assert.True(await ItemUseHandler.SpendOneAsync(pipe.ToClient, selected, weigh: false));
                    Assert.Equal(quantity - 1, Equipment.ByUid(uid)?.Quantity ?? 0);
                    Assert.Equal(1, Equipment.ByUid(1)!.Quantity);
                    Assert.Equal(quantity > 1
                        ? ConnectionProtocol.BuildItemQuantity(uid, quantity - 1)
                        : ConnectionProtocol.BuildItemGone(uid),
                        await pipe.Next(quantity > 1 ? Op.Ivj : Op.Ium));
                    var saved = DatabaseManager.LoadInventory(character);
                    Assert.Equal(quantity - 1, saved.Find(i => i.Uid == uid)?.Quantity ?? 0);
                }
                finally { DatabaseManager.DestroyCharacterItem(character, uid, 0); }
            }
        }
    }
}

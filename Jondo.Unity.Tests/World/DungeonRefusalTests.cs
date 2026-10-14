using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
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
        [InlineData(false)]
        [InlineData(true)]
        public async Task A_missing_key_sends_a_red_bold_notice_and_keeps_the_connection(bool hasKeyring)
        {
            const int dungeonId = int.MaxValue, npcId = int.MaxValue;
            const long entrance = -990000001, keyReply = 24970;
            var dungeons = (Dictionary<int, DungeonManager.Dungeon>)DungeonManager.All;
            var guardians = (Dictionary<int, DungeonDoor.Options>)typeof(DungeonDoor)
                .GetField("_byNpc", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var dungeon = new DungeonManager.Dungeon
            {
                Id = 0, Name = "Cripta de Chadalid", EntranceMapId = entrance,
                OnKeyring = hasKeyring,
            };
            dungeon.Rooms.Add(entrance - 1);
            dungeon.Required.Add((999999, 1));
            dungeons.Add(dungeonId, dungeon);
            guardians.Add(npcId, new DungeonDoor.Options { Key = keyReply, Keyring = keyReply + 1 });

            try
            {
                // Dungeon 0 avoids database access. Selecting the loose key disables free entry,
                // so a keyring in the bag exercises the same refusal branch as an exhausted entry.
                await using var pipe = await ClientPipe.OpenAsync(990000001, 990000002, entrance);
                using (SessionContext.Push(pipe.Session))
                {
                    pipe.Session.State.Language = "fr";
                    pipe.Session.State.CharacterLevel = 200;
                    pipe.Session.State.OpenDialogueNpcId = npcId;
                    if (hasKeyring) Equipment.Add(1, DungeonHandler.Keyring, 1, Equipment.Bag, null);

                    byte[] expected = ConnectionProtocol.BuildNotice(
                        "<color=#FF0000><b>Vous ne possédez pas l'objet nécessaire.</b></color>");
                    Assert.False(await DungeonHandler.AtTheDoorAsync(pipe.ToClient, entrance, keyReply));
                    Assert.Equal(expected, await pipe.Next(Op.Lqn));
                    Assert.Equal(entrance, pipe.Session.State.MapId);
                    if (hasKeyring) Assert.Single(Equipment.All);
                    else Assert.Empty(Equipment.All);

                    // Another request on the same TCP stream must still receive its response.
                    Assert.False(await DungeonHandler.AtTheDoorAsync(pipe.ToClient, entrance, keyReply));
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
    }
}

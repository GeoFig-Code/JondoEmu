using System.Linq;
using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Xunit;

namespace Jondo.Unity.Tests.Quests
{
    /// <summary>The followed quests' box, put in a new order as in «barco hacia vulkania…», frame 365.</summary>
    public sealed class FollowedQuestOrderTests
    {
        private const long Character = 8_900_100_081;

        [Fact]
        public void The_box_keeps_the_order_the_client_sends()
        {
            foreach (int quest in FollowedQuests.Of(Character)) FollowedQuests.Unfollow(Character, quest);
            var session = GameSession.SinSocket();
            session.State.CharacterId = Character;
            try
            {
                using (SessionContext.Push(session))
                {
                    foreach (int quest in new[] { 1869, 724, 725, 730, 726 }) FollowedQuests.Follow(Character, quest);

                    // iek { 1 [730, 1869, 725, 726] }, byte for byte.
                    QuestHandler.Reorder(ConnectionProtocol.Push("iek", System.Convert.FromHexString("0a08da05cd0ed505d605")));
                    Assert.Equal(new[] { 730, 1869, 725, 726, 724 }, FollowedQuests.Of(Character).ToArray());
                }
            }
            finally
            {
                foreach (int quest in FollowedQuests.Of(Character)) FollowedQuests.Unfollow(Character, quest);
            }
        }
    }
}

using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>The zaap a character saves his position at: where defeats and the recall potion take him.</summary>
    [Collection("MapManager")]
    public class SavedPositionTests
    {
        private const long Character = 9_300_000_001;

        [Fact]
        public void The_saved_zaap_is_the_save_point_and_the_starting_one_until_then()
        {
            if (MapManager.GetMapInfo(CharacterCreationHandler.StartingMap) == null) MapManager.Initialize();
            SavedPositions.Forget(Character);
            try
            {
                Assert.Equal(CharacterCreationHandler.StartingMap, FightHandler.SavePointOf(Character).Map);

                // The Pueblo de los ganaderos' zaap, the capture's saved one.
                SavedPositions.Save(Character, 73400320);
                if (MapManager.GetMapInfo(73400320) == null) return;
                Assert.Equal(73400320, FightHandler.SavePointOf(Character).Map);

                // A map the world does not have is not a place to come back to.
                SavedPositions.Save(Character, 999_999_999);
                Assert.Equal(CharacterCreationHandler.StartingMap, FightHandler.SavePointOf(Character).Map);
            }
            finally
            {
                SavedPositions.Forget(Character);
            }
        }
    }
}

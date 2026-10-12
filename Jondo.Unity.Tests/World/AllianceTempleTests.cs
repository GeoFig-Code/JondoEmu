using Jondo.Unity.Server;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// The Temple of Alliances as the interactive registry declares it. In the MapManager
    /// collection, with the other tests that rebuild the registry, which is shared.
    /// </summary>
    [Collection("MapManager")]
    public class AllianceTempleTests
    {
        /// <summary>
        /// The door is declared with the skill the capture measures, 184, and nothing else: the
        /// 2.68 passage (114) that used to sit on it would let anybody in without a gem. The room
        /// behind it has the book that founds, on 355, declared the same way.
        /// </summary>
        [Fact]
        public void The_door_and_the_book_found_and_nothing_else()
        {
            InteractiveRegistry.Initialize();
            if (InteractiveRegistry.OnMap(AllianceHandler.TempleMap).Count == 0) return;

            var door = Assert.Single(InteractiveRegistry.OnMap(AllianceHandler.TempleMap),
                                     i => i.Element.Id == AllianceHandler.TempleDoor);
            var action = Assert.Single(door.Actions);
            Assert.Equal(InteractiveActionKind.AllianceFounding, action.Kind);
            Assert.Equal(AllianceHandler.TempleDoorSkill, action.SkillId);

            var book = Assert.Single(InteractiveRegistry.OnMap(AllianceHandler.InnerRoom),
                                     i => i.Element.Id == AllianceHandler.FoundingBook);
            Assert.Equal(355, book.Element.Cell);
            Assert.Equal(InteractiveActionKind.AllianceFounding, Assert.Single(book.Actions).Kind);
        }

        /// <summary>
        /// The 2.68 passage on the door is left out of the routes, and not only out of the registry:
        /// kept as a route, the start's guard found a passage that resolved to the alliance's
        /// door and stopped the server, and its cell let anybody walk in without the gem.
        /// </summary>
        [Fact]
        public void The_door_is_not_a_passage()
        {
            if (!MapManager.WalkableCells.ContainsKey(AllianceHandler.TempleMap)) MapManager.Initialize();
            if (MapManager.GetMapInfo(AllianceHandler.TempleMap) == null) return;   // no world data
            Interactives.Initialize();
            TeleportManager.Initialize();

            Assert.False(TeleportManager.TryGet(AllianceHandler.TempleMap, AllianceHandler.TempleDoor, out _));
            Assert.DoesNotContain(TeleportManager.All, r =>
                r.SourceMapId == AllianceHandler.TempleMap && r.ElementId == AllianceHandler.TempleDoor);
            var door = Interactives.ByElementId(AllianceHandler.TempleMap, AllianceHandler.TempleDoor);
            Assert.False(TeleportManager.TryGetCellTrigger(AllianceHandler.TempleMap, door.Cell, out _));
        }
    }
}

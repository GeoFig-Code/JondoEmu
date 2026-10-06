using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jondo.Unity.Launcher;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// Room order and boss-room choice: hubs and Exits must not steal LastRoom.
    /// </summary>
    /// <remarks>
    /// Sandy Castle (and Crackler's, Magik Riktus, …) used to keep the extractor's list because
    /// one map had no "Nth Room" ordinal, so ByName aborted and First Room sat at the end as the
    /// fake boss. Quest 896 then asked for the Fifth Room and never ticked.
    /// </remarks>
    public class DungeonRoomOrderTests
    {
        [Fact]
        public void ByName_orders_numbered_rooms_and_keeps_the_hub_after()
        {
            var names = new Dictionary<long, string>
            {
                [1] = "Sandy Castle - Second Room",
                [2] = "Sandy Castle - Third Room",
                [3] = "Sandy Castle - Fourth Room",
                [4] = "Sandy Castle - Fifth Room",
                [5] = "Sandy Castle",
                [6] = "Sandy Castle - First Room",
            };
            var rooms = new List<long> { 1, 2, 3, 4, 5, 6 };

            var ordered = DungeonManager.ByName(rooms, names);

            Assert.NotNull(ordered.Rooms);
            Assert.Equal(new long[] { 6, 1, 2, 3, 4, 5 }, ordered.Rooms);
            Assert.Equal(4, ordered.NumberedLast);
            Assert.Equal(4, DungeonManager.PickBossRoom(ordered.Rooms!, names, ordered.NumberedLast));
        }

        [Fact]
        public void ByName_prefers_the_prefix_that_owns_an_Exit()
        {
            // Koolich: Lair 1..6 and Cavern 1..5 + Exit. The cavern is the real walk.
            var names = new Dictionary<long, string>
            {
                [10] = "Koolich's Lair - First Room",
                [11] = "Koolich's Lair - Second Room",
                [20] = "Koolich Cavern - First Room",
                [21] = "Koolich Cavern - Second Room",
                [22] = "Koolich Cavern - Fifth Room",
                [29] = "Koolich Cavern - Exit",
            };
            var rooms = new List<long> { 10, 11, 20, 21, 22, 29 };

            var ordered = DungeonManager.ByName(rooms, names);

            Assert.NotNull(ordered.Rooms);
            Assert.Equal(new long[] { 20, 21, 22 }, ordered.Rooms!.Take(3));
            Assert.Equal(22, ordered.NumberedLast);
            Assert.Contains(29L, ordered.Rooms);
            Assert.Equal(22, DungeonManager.PickBossRoom(ordered.Rooms, names, ordered.NumberedLast));
        }

        [Fact]
        public void PickBossRoom_skips_Exit_and_picks_the_throne()
        {
            var names = new Dictionary<long, string>
            {
                [1] = "Lord Crow's Library - Throne Room",
                [2] = "Lord Crow's Library - Antechamber",
                [3] = "Lord Crow's Library - Library Annexe",
            };

            Assert.Equal(1, DungeonManager.PickBossRoom(new long[] { 1, 2, 3 }, names));
        }

        [Fact]
        public void PickBossRoom_prefers_a_named_fissure_over_the_last_ordinal()
        {
            var names = new Dictionary<long, string>
            {
                [1] = "Ilyzaelle's Lookout - First Room",
                [2] = "Ilyzaelle's Lookout - Second Room",
                [3] = "Ilyzaelle's Lookout - Erzal's Fissure",
            };

            Assert.Equal(3, DungeonManager.PickBossRoom(new long[] { 1, 2, 3 }, names, numberedLast: 2));
        }

        [Fact]
        public void PickBossRoom_never_returns_an_Exit()
        {
            var names = new Dictionary<long, string>
            {
                [1] = "Magik Riktus Big Top - First Room",
                [2] = "Magik Riktus Big Top - Fifth Room",
                [3] = "Magik Riktus Big Top - Exit",
            };

            Assert.Equal(2, DungeonManager.PickBossRoom(new long[] { 3, 1, 2 }, names, numberedLast: 2));
            Assert.True(DungeonManager.IsExitName("Magik Riktus Big Top - Exit"));
        }

        [Fact]
        public void SameDungeonRoom_is_true_for_two_rooms_of_one_dungeon()
        {
            if (!File.Exists(Paths.DungeonsJson)) return;

            DungeonManager.Initialize();
            var sandy = DungeonManager.Get(19);
            if (sandy == null || sandy.Rooms.Count < 2) return;

            Assert.True(QuestWatcher.SameDungeonRoom(sandy.Rooms[0], sandy.Rooms[^1]));
            Assert.False(QuestWatcher.SameDungeonRoom(sandy.Rooms[0], 0));
        }

        [Fact]
        public void Live_catalogue_puts_bosses_on_the_maps_quests_name()
        {
            if (!File.Exists(Paths.DungeonsJson)) return;

            DungeonManager.Initialize();
            if (!DungeonManager.IsLoaded) return;

            // Castillo de Arena / Sandy Castle — quest 896 wants Fifth Room.
            Assert.Equal(193729536, DungeonManager.Get(19)?.LastRoom);

            // Crackler's Rocky Peaks — Seventh Room.
            Assert.Equal(106960896, DungeonManager.Get(18)?.LastRoom);

            // Koolich Cavern — Fifth Room of the cavern (not the lair, not Exit).
            Assert.Equal(107227136, DungeonManager.Get(30)?.LastRoom);

            // Lord Crow — Throne Room.
            Assert.Equal(159125512, DungeonManager.Get(3)?.LastRoom);

            // Ilyzaelle — Erzal's Fissure.
            Assert.Equal(184686337, DungeonManager.Get(107)?.LastRoom);

            // Magik Riktus — Fifth Room, never the Exit.
            Assert.Equal(181669888, DungeonManager.Get(105)?.LastRoom);
        }

        [Fact]
        public void Live_catalogue_never_uses_an_Exit_as_LastRoom()
        {
            if (!File.Exists(Paths.DungeonsJson)) return;

            DungeonManager.Initialize();
            if (!DungeonManager.IsLoaded) return;

            var names = new Dictionary<long, string>();
            try
            {
                using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                    Paths.WorldConnectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT MapId, Name FROM MapPositions WHERE Name IS NOT NULL;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) names[reader.GetInt64(0)] = reader.GetString(1);
            }
            catch
            {
                return;
            }

            if (names.Count == 0) return;

            var exitsAsBoss = DungeonManager.All.Values
                .Where(d => d.LastRoom != 0 &&
                            names.TryGetValue(d.LastRoom, out string? n) &&
                            n != null &&
                            DungeonManager.IsExitName(n))
                .Select(d => d.Id)
                .ToList();

            Assert.True(exitsAsBoss.Count == 0,
                        "Exit used as LastRoom in dungeons: " + string.Join(", ", exitsAsBoss));
        }
    }
}

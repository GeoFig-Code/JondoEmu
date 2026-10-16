using Jondo.Unity.Server;
using Jondo.Unity.Server.Network;
using Xunit;

namespace Jondo.Unity.Tests.Combat
{
    /// <summary>
    /// Two fights standing on the same map, and what leaks between them.
    /// </summary>
    /// <remarks>
    /// Every roleplay map resolves to ONE arena. That is not an accident to be fixed: the arena is a
    /// real map out of the game files and its cell layout is what the fight is drawn on, measured
    /// against the capture where the Incarnam fight at (-2,-3) was fought in arena 153891076 -- 6 of
    /// 6 fighter positions walkable there and 0 of 6 on the roleplay map.
    ///
    /// The consequence is that two parties fighting on one map get the same MapId, and anything sent
    /// to "everybody on this map" reaches both. Map chat did: you could read the strangers fighting
    /// beside you, and they could read you. The fight packets never could -- those go to the fight's
    /// own participant list and never through the map -- which is why this went unnoticed.
    /// </remarks>
    /// <remarks>
    /// In the MapManager collection because ResolveArenaMapId ENUMERATES MapManager.Maps and
    /// WalkableCells, and MonsterVetoTests rebuilds both out of the 240 MB world.db. Enumerating
    /// a dictionary somebody else is refilling is not a stale read, it throws -- and the load is
    /// slow enough to make that a wide window rather than a freak one.
    /// </remarks>
    [Collection("MapManager")]
    public class ArenaSharingTests
    {
        [Fact]
        public void One_roleplay_map_gives_both_fights_the_same_arena()
        {
            // The premise, stated rather than assumed. If this ever stops being true the filter
            // below becomes unnecessary, and whoever changes it should see that here first.
            long first = MapManager.ResolveArenaMapId(154010883);
            long second = MapManager.ResolveArenaMapId(154010883);

            Assert.Equal(first, second);
        }

        /// <summary>
        /// A subarea with two families of arenas fights in the one beside its maps: the Edge of the
        /// Evil Forest (22) has five at 882176xx and five underground at 994851xx, and a tax
        /// collector's fight at (-1,14), 88212745, was played in a cave.
        /// </summary>
        [Fact]
        public void The_arena_is_of_the_map_s_own_block()
        {
            if (!MapManager.WalkableCells.ContainsKey(88212745)) MapManager.Initialize();
            if (MapManager.GetMapInfo(88212745) == null) return;   // no world data

            long arena = MapManager.ResolveArenaMapId(88212745);
            Assert.Equal(88212745L >> 16, arena >> 16);
            Assert.Equal(22, MapManager.GetMapInfo(arena)!.SubAreaId);
        }

        [Fact]
        public void A_fresh_session_is_in_no_fight()
        {
            // Zero is the "not fighting" value, and it has to be the default rather than something
            // set on login: a session that never entered a fight must still match the players
            // walking around, or map chat would go silent for everybody.
            Assert.Equal(0, new SessionState().FightId);
        }

        [Fact]
        public void Without_a_sender_the_whole_map_hears_it()
        {
            // What the world itself says -- an actor arriving, a map effect -- has no fight behind
            // it and must reach everybody, fighters included. Filtering unconditionally would have
            // made this class of packet vanish for anyone in a fight.
            Assert.True(SessionRegistry.Hears(0, null));
            Assert.True(SessionRegistry.Hears(7, null));
        }

        [Theory]
        [InlineData(0, 0, true)]      // two people walking about: they hear each other
        [InlineData(7, 7, true)]      // same fight: they hear each other
        [InlineData(7, 9, false)]     // the bug: two fights, one arena
        [InlineData(0, 7, false)]     // someone outside must not hear what is said inside
        [InlineData(7, 0, false)]     // nor the other way round
        public void Whether_one_hears_the_other_is_the_fight_and_not_the_map(long speaker,
                                                                            long listener,
                                                                            bool hears)
        {
            // The rule BroadcastToMapAsync applies, called directly. Going through the registry
            // would need two live sockets, and re-stating the comparison in the test would prove
            // only that the test agrees with itself.
            Assert.Equal(hears, SessionRegistry.Hears(listener, speaker));
        }
    }
}

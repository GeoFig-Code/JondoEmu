using System.Collections.Generic;
using System.Linq;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.World.Content;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>
    /// The dream's goblins: where each may stand, and what their powers do to the dream --
    /// Gobserico wins the room, Gobledore goes back to the room before.
    /// </summary>
    [Collection("MapManager")]
    public class DreamGoblinTests
    {
        private static Dreams.Sueno Whole(long who, string? seed = null)
        {
            Interactives.Initialize();
            var dream = Dreams.Crear(who, "Prueba", 200, 5, 100, 200, 0, seed);
            for (int band = 2; band <= Dreams.Bands; band++) Dreams.AnadirFranja(dream);
            return dream;
        }

        /// <summary>
        /// Over many dreams: Bog and the Rey Gob at fountains, Gobledore, Gobserico and the Rey
        /// Gob in fight rooms, none at the entrance, a favour or the end -- and none at all in a
        /// dream from a code.
        /// </summary>
        [Fact]
        public void Each_goblin_stands_where_the_guide_says()
        {
            var seen = new HashSet<(bool Fountain, int Goblin)>();
            for (int i = 0; i < 40; i++)
            {
                var dream = Whole(900_000_000_041 + i);
                foreach (var room in dream.Salas.Where(r => r.Goblin != 0))
                {
                    Assert.False(room.EsFinal || room.EsFavor || room.Fila == 0);
                    if (room.EsFuente) Assert.Contains(room.Goblin, new[] { Dreams.Bog, Dreams.ReyGob });
                    else Assert.Contains(room.Goblin, new[] { Dreams.Gobledore, Dreams.Gobserico, Dreams.ReyGob });
                    seen.Add((room.EsFuente, room.Goblin));
                }
                Dreams.Olvidar(dream.CharacterId);
            }
            Assert.Contains((true, Dreams.Bog), seen);
            Assert.Contains((false, Dreams.Gobledore), seen);
            Assert.Contains((false, Dreams.Gobserico), seen);

            var souvenir = Whole(900_000_000_099, "GOBLINS1");
            Assert.All(souvenir.Salas, r => Assert.Equal(0, r.Goblin));
            Dreams.Olvidar(souvenir.CharacterId);
        }

        /// <summary>Gobserico wins the room for the dreamer: its doors open, its group goes.</summary>
        [Fact]
        public void Gobserico_wins_the_room()
        {
            var dream = Whole(900_000_000_100);
            var room = dream.Salas.First(r => r.Fila == 1 && r.Miembros.Count > 0);
            Dreams.Enter(dream, room.Id, out _);
            Assert.False(room.Hecha);

            Dreams.ClearRoom(room);
            Assert.True(room.Hecha);
            Assert.True(Dreams.CanLeave(room));
            Assert.Equal(0, Dreams.ClearRoom(room));
            Dreams.Olvidar(dream.CharacterId);
        }

        /// <summary>
        /// Gobledore goes back to the room before, whose doors can be chosen again; the room left
        /// pays its portal bonus a second time on the way back in.
        /// </summary>
        [Fact]
        public void Gobledore_goes_back_to_the_room_before()
        {
            var dream = Whole(900_000_000_101);
            var room = dream.Salas.First(r => r.Fila == 1 && r.Miembros.Count > 0);
            Dreams.Enter(dream, room.Id, out _);
            int points = dream.DreamPoints;

            var before = Dreams.GoBack(dream);
            Assert.NotNull(before);
            Assert.Equal(0, before!.Id);
            Assert.Equal(0, dream.Actual);
            Assert.False(room.Cobrada);

            Dreams.Enter(dream, room.Id, out _);
            Assert.Equal(points + room.DreamPoints, dream.DreamPoints);
            Dreams.Olvidar(dream.CharacterId);
        }

        /// <summary>A dream saved before the other goblins said the Rey Gob as HasReyGob: it still does.</summary>
        [Fact]
        public void A_saved_Rey_Gob_comes_back()
        {
            var room = System.Text.Json.JsonSerializer.Deserialize<Dreams.Sala>("{\"Id\":9,\"HasReyGob\":true}")!;
            Assert.Equal(Dreams.ReyGob, room.Goblin);
        }

        /// <summary>
        /// A dream's room fights with the dream's clocks -- 1,800 tenths to place, 900 a player's
        /// turn, the captures' -- and no challenges, and losing costs nothing.
        /// </summary>
        [Fact]
        public void A_dream_fight_has_the_dream_s_clocks()
        {
            var rules = Jondo.Unity.World.Fights.FightRules.EnUnSueno;
            Assert.Equal(1800, rules.RelojDeColocacion);
            Assert.Equal(900, rules.PlayerTurn);
            Assert.False(rules.HayRetos);
            Assert.False(rules.DefeatCosts);
            Assert.True(rules.ReparteBotin);
            Assert.Equal(0, Jondo.Unity.World.Fights.FightRules.ContraMonstruos.PlayerTurn);
        }

        /// <summary>The goblins' accepting replies carry the client's actions: their own power and 4037, hiding them.</summary>
        [Fact]
        public void The_goblins_say_their_power()
        {
            Assert.True(DreamHandler.CarriesGoblinPower(new long[] { 4037, 4033 }));
            Assert.False(DreamHandler.CarriesGoblinPower(new long[] { 1, 2 }));
            Assert.False(DreamHandler.CarriesGoblinPower(new long[0]));
        }
    }
}

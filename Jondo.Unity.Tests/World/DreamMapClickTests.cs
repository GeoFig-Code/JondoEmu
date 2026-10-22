using System.Linq;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Xunit;

namespace Jondo.Unity.Tests.World
{
    /// <summary>A room clicked on the dream's map (ixn): the door's rules, by the map's room ids.</summary>
    [Collection("MapManager")]
    public class DreamMapClickTests : System.IDisposable
    {
        public DreamMapClickTests() => Dreams.OlvidarTodo();

        public void Dispose() => Dreams.OlvidarTodo();

        private static Dreams.Sueno Started()
        {
            Interactives.Initialize();
            var dream = Dreams.Crear(8_872_000_001, "Prueba", 200, 4, 100, 200);
            Dreams.Enter(dream, 0, out _);
            return dream;
        }

        [Fact]
        public void A_click_leads_to_an_exit_of_the_room_stood_in_and_nowhere_else()
        {
            var dream = Started();
            var entrance = dream.SalaActual!;
            Assert.NotEmpty(entrance.Salidas);
            int exit = entrance.Salidas[0];

            Assert.Equal(exit, DreamHandler.RoomChosen(dream, exit.ToString()));

            int elsewhere = dream.Salas.Select(r => r.Id).First(id => id != entrance.Id && !entrance.Salidas.Contains(id));
            Assert.Null(DreamHandler.RoomChosen(dream, elsewhere.ToString()));
            Assert.Null(DreamHandler.RoomChosen(dream, ""));
            Assert.Null(DreamHandler.RoomChosen(dream, "-1"));
            Assert.Null(DreamHandler.RoomChosen(dream, "x1"));
        }

        [Fact]
        public void A_room_whose_group_still_stands_is_not_left_by_the_map_either()
        {
            var dream = Started();
            var fight = dream.Salas.First(r => r.Miembros.Count > 0 && dream.SalaActual!.Salidas.Contains(r.Id));
            Assert.NotNull(Dreams.Enter(dream, fight.Id, out _));
            fight.Plantado = 1;

            Assert.All(fight.Salidas, exit => Assert.Null(DreamHandler.RoomChosen(dream, exit.ToString())));
        }
    }
}

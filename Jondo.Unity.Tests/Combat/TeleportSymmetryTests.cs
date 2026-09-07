using Jondo.Unity.World.Fights;
using Jondo.Unity.World.Maps;
using Xunit;

namespace Jondo.Unity.Tests.Combat
{
    /// <summary>
    /// Los teletransportes simétricos y la memoria de por dónde se pasó.
    /// </summary>
    /// <remarks>
    /// Cuatro efectos, 54 hechizos de clase: 1104 respecto al objetivo, 1105 respecto al
    /// lanzador, 1106 respecto a la casilla apuntada, y 1100, que no es simétrico sino que
    /// deshace el último movimiento.
    ///
    /// La parte fácil de equivocar es la geometría: la retícula de Dofus va en diagonal, así que
    /// el reflejo NO es una cuenta sobre el número de casilla. Hay que pasar por coordenadas.
    /// </remarks>
    public class TeleportSymmetryTests
    {
        [Fact]
        public void El_reflejo_deja_al_otro_lado_y_a_la_misma_distancia()
        {
            // Con el pivote en medio, ir y volver tiene que devolver al punto de partida.
            const int pivote = 300;
            foreach (int desde in new[] { 285, 286, 314, 315, 271, 329 })
            {
                int alOtroLado = MapGeometry.Reflejar(desde, pivote);
                if (alOtroLado < 0) continue;

                Assert.NotEqual(desde, alOtroLado);
                Assert.Equal(MapGeometry.Distance(desde, pivote),
                             MapGeometry.Distance(pivote, alOtroLado));

                // Y es una involución: reflejar el reflejo devuelve el original.
                Assert.Equal(desde, MapGeometry.Reflejar(alOtroLado, pivote));
            }
        }

        [Fact]
        public void Reflejarse_sobre_uno_mismo_no_mueve()
        {
            Assert.Equal(300, MapGeometry.Reflejar(300, 300));
        }

        [Fact]
        public void Fuera_del_tablero_devuelve_menos_uno_en_vez_de_una_casilla_inventada()
        {
            // Con el pivote pegado al borde, el reflejo se sale. Mejor decir que no se puede que
            // mandar a alguien a una casilla que no existe.
            Assert.Equal(-1, MapGeometry.Reflejar(-5, 300));
            Assert.Equal(-1, MapGeometry.Reflejar(300, -5));
        }

        [Fact]
        public void El_luchador_se_acuerda_de_donde_venia()
        {
            var quien = new Fighter { Id = 1, MaxHP = 100, CurrentHP = 100, CellId = 200 };

            // Sin moverse todavía no hay a dónde volver.
            Assert.Equal(-1, quien.CasillaAnterior);

            quien.MoverA(315);
            Assert.Equal(315, quien.CellId);
            Assert.Equal(200, quien.CasillaAnterior);

            quien.MoverA(330);
            Assert.Equal(315, quien.CasillaAnterior);

            // Moverse a donde ya se está no cuenta como movimiento.
            quien.MoverA(330);
            Assert.Equal(315, quien.CasillaAnterior);
        }
    }
}

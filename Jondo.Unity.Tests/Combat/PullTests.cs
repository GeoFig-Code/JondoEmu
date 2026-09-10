using Jondo.Unity.World.Maps;
using Xunit;

namespace Jondo.Unity.Tests.Combat
{
    /// <summary>
    /// Un tirón se para en cuanto llegaría al centro, y no se pasa de largo.
    /// </summary>
    /// <remarks>
    /// Sin esto la Imantación del tymador —que tira de sus bombas SEIS casillas— cruzaba el punto
    /// y las dejaba al otro lado. Y como el hechizo tira dos veces, la segunda las traía de vuelta:
    /// en el registro se ve el baile, la bomba -5 de la 272 a la 185 y de la 185 otra vez a la 272.
    /// </remarks>
    public class PullTests
    {
        private static int Desde(int celda, int dx, int dy)
        {
            var (x, y) = MapGeometry.CellToPoint(celda);
            return MapGeometry.PointToCell(x + dx, y + dy);
        }

        [Fact]
        public void Un_tiron_largo_se_queda_pegado_al_centro()
        {
            int centro = 270;
            int quienTira = Desde(centro, 0, -3);
            int bomba = Desde(centro, 0, 4);

            var r = Zone.Push(centro, quienTira, bomba, casillas: -6,
                              pisables: null, ocupadas: null);

            Assert.Equal(Desde(centro, 0, 1), r.ToCell);
            Assert.NotEqual(centro, r.ToCell);
        }

        [Fact]
        public void Un_tiron_corto_llega_donde_le_toca()
        {
            int centro = 270;
            int bomba = Desde(centro, 0, 5);

            var r = Zone.Push(centro, centro, bomba, casillas: -2,
                              pisables: null, ocupadas: null);

            Assert.Equal(Desde(centro, 0, 3), r.ToCell);
        }

        [Fact]
        public void Un_empujon_no_mira_el_centro_para_nada()
        {
            int centro = 270;
            int bicho = Desde(centro, 0, 1);

            var r = Zone.Push(centro, centro, bicho, casillas: 3,
                              pisables: null, ocupadas: null);

            Assert.Equal(Desde(centro, 0, 4), r.ToCell);
        }
    }
}

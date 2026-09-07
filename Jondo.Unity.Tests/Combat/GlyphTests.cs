using System.Collections.Generic;
using System.Linq;
using Jondo.Unity.World.Fights;
using Xunit;

namespace Jondo.Unity.Tests.Combat
{
    /// <summary>
    /// Lo que se pone en el suelo: glifos, trampas y runas.
    /// </summary>
    /// <remarks>
    /// Las cuatro familias del catálogo —1091 el glifo de aura con 316 hechizos, 401 el de inicio
    /// de turno con 142, 400 la trampa con 100 y 2022 la runa con 65— tienen la misma forma
    /// medida y sólo se distinguen en cuándo se disparan. Por eso hay un solo tipo con un enum y
    /// no cuatro clases con el mismo cuerpo.
    /// </remarks>
    public class GlyphTests
    {
        private static Glifo Poner(Disparo cuando, int caduca = 0, params int[] casillas)
            => new Glifo(dueno: 1, casillas: casillas, hechizo: 6382, grado: 1,
                         color: 16777215, caducaEnRonda: caduca, mascara: "a,A", cuando: cuando);

        [Fact]
        public void La_trampa_se_dispara_al_pisarla_y_no_al_empezar_el_turno()
        {
            var trampa = Poner(Disparo.AlPisar, 0, 100, 101);

            Assert.True(trampa.SeDisparaAlPisar);
            Assert.False(trampa.SeDisparaAlEmpezarElTurno);
            Assert.True(trampa.SeGastaAlDispararse);
            Assert.True(trampa.Cubre(101));
            Assert.False(trampa.Cubre(102));
        }

        [Fact]
        public void El_glifo_de_inicio_de_turno_es_al_reves()
        {
            var glifo = Poner(Disparo.AlEmpezarElTurno, 0, 200);

            Assert.False(glifo.SeDisparaAlPisar);
            Assert.True(glifo.SeDisparaAlEmpezarElTurno);
            Assert.False(glifo.SeGastaAlDispararse);
        }

        [Fact]
        public void El_de_aura_y_la_runa_se_disparan_con_las_dos_cosas()
        {
            var aura = Poner(Disparo.AlPisarYAlEmpezar, 0, 300);

            Assert.True(aura.SeDisparaAlPisar);
            Assert.True(aura.SeDisparaAlEmpezarElTurno);
            Assert.False(aura.SeGastaAlDispararse);
        }

        [Fact]
        public void Un_gastado_ya_no_se_dispara_con_nada()
        {
            var trampa = Poner(Disparo.AlPisar, 0, 100);
            trampa.Gastado = true;

            Assert.False(trampa.SeDisparaAlPisar);
            Assert.False(trampa.SeDisparaAlEmpezarElTurno);
        }

        [Fact]
        public void El_combate_reparte_identificadores_y_barre_los_caidos()
        {
            var combate = new FightInstance(1, 1, 1);

            var permanente = combate.Poner(Poner(Disparo.AlPisarYAlEmpezar, 0, 10));
            var corto = combate.Poner(Poner(Disparo.AlEmpezarElTurno, caduca: 2, casillas: 20));
            var gastada = combate.Poner(Poner(Disparo.AlPisar, 0, 30));

            // Cada uno el suyo: dos con el mismo número serían uno solo para el cliente.
            Assert.Equal(3, new[] { permanente.Id, corto.Id, gastada.Id }.Distinct().Count());
            Assert.Equal(3, combate.Glifos.Count);

            gastada.Gastado = true;
            var caidos = combate.BarrerLosGlifos();

            Assert.Contains(gastada, caidos);
            Assert.Equal(2, combate.Glifos.Count);

            // El de duración -1 llegó como cero y no se cae; el de dos rondas sí, cuando toque.
            Assert.Equal(0, permanente.CaducaEnRonda);
            Assert.Contains(permanente, combate.Glifos);
        }

        [Fact]
        public void Se_encuentra_por_la_casilla_que_se_pisa()
        {
            var combate = new FightInstance(1, 1, 1);
            combate.Poner(Poner(Disparo.AlPisar, 0, 100, 101, 102));
            combate.Poner(Poner(Disparo.AlEmpezarElTurno, 0, 101));

            // Pisar la 101 dispara la trampa, no el de inicio de turno.
            Assert.Single(combate.LosQuePisa(101));
            Assert.Single(combate.LosQueEmpiezan(101));
            Assert.Empty(combate.LosQuePisa(500));
        }
    }
}

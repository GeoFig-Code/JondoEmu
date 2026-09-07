using Jondo.Unity.World.Fights;
using Xunit;

namespace Jondo.Unity.Tests.Combat
{
    /// <summary>
    /// El escudo: los efectos 1020 y 1039, 401 hechizos entre los dos.
    /// </summary>
    /// <remarks>
    /// El 1020 da un tanto por ciento del NIVEL del lanzador y el 1039 uno de la VIDA. El tanto
    /// por ciento va en el dado, no en el valor: Caparazón lleva diceNum 150 y Soldagüino 200;
    /// Bendición Maravillosa 10 y Coraza de Dopeul 20. El value va a cero en los seis leídos.
    ///
    /// Lo que estas pruebas fijan es que el escudo NO es vida: no se cura, no cuenta para la
    /// muerte y se cae solo. Meterlo en CurrentHP habría sido más corto y habría dejado a un
    /// personaje escudado curándose hasta el tope del escudo.
    /// </remarks>
    public class ShieldTests
    {
        private static Fighter Uno(int vida = 1000) => new Fighter
        {
            Id = 1, Level = 200, MaxHP = vida, CurrentHP = vida,
        };

        [Fact]
        public void El_escudo_se_come_el_golpe_antes_que_la_vida()
        {
            var quien = Uno();
            quien.Escudar(300, caducaEnRonda: 3);

            // Un golpe más pequeño que el escudo no toca la vida.
            Assert.Equal(0, quien.PasarPorElEscudo(200));
            Assert.Equal(100, quien.PuntosDeEscudo);

            // Y uno más grande pasa sólo lo que sobra.
            Assert.Equal(150, quien.PasarPorElEscudo(250));
            Assert.Equal(0, quien.PuntosDeEscudo);
        }

        [Fact]
        public void Sin_escudo_el_golpe_pasa_entero()
        {
            var quien = Uno();
            Assert.Equal(500, quien.PasarPorElEscudo(500));
        }

        [Fact]
        public void Se_suma_y_se_queda_la_caducidad_mas_lejana()
        {
            var quien = Uno();
            quien.Escudar(100, caducaEnRonda: 3);
            quien.Escudar(200, caducaEnRonda: 6);

            Assert.Equal(300, quien.PuntosDeEscudo);

            // En la ronda 3 todavía aguanta, porque el segundo llega hasta la 6.
            quien.CaducarElEscudo(3);
            Assert.Equal(300, quien.PuntosDeEscudo);

            quien.CaducarElEscudo(6);
            Assert.Equal(0, quien.PuntosDeEscudo);
        }

        [Fact]
        public void El_escudo_no_es_vida()
        {
            // Ni cuenta para la muerte ni se cura: son dos sacos distintos.
            var quien = Uno(vida: 100);
            quien.Escudar(500, caducaEnRonda: 9);

            Assert.Equal(100, quien.CurrentHP);
            Assert.Equal(100, quien.MaxHP);
            Assert.True(quien.IsAlive);

            quien.TakeDamage(quien.PasarPorElEscudo(600));
            Assert.False(quien.IsAlive);
        }
    }
}

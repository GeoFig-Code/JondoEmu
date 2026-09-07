using Jondo.Unity.Server.Managers;
using Jondo.Unity.World.Fights;
using Xunit;

namespace Jondo.Unity.Tests.Combat
{
    /// <summary>
    /// El «mejor elemento»: el 2822 y el elemento 5 del catálogo.
    /// </summary>
    /// <remarks>
    /// Veinte hechizos de clase pegan «en el mejor elemento del lanzador» — Llamilla, Bilbipo,
    /// Apetito de Cocobur —. No es un elemento: es una pregunta al lanzador, y se contesta con
    /// los embrujos puestos, porque un hechizo que te suba la agilidad a mitad de combate puede
    /// cambiar la respuesta, y eso es justamente para lo que se lanza.
    /// </remarks>
    public class BestElementTests
    {
        private static Fighter Con(int fuerza, int inteligencia, int suerte, int agilidad)
            => new Fighter
            {
                Id = 1, MaxHP = 100, CurrentHP = 100,
                Strength = fuerza, Intelligence = inteligencia,
                Chance = suerte, Agility = agilidad,
            };

        [Theory]
        [InlineData(500, 100, 100, 100, 1)]   // tierra
        [InlineData(100, 500, 100, 100, 2)]   // fuego
        [InlineData(100, 100, 500, 100, 3)]   // agua
        [InlineData(100, 100, 100, 500, 4)]   // aire
        public void Gana_la_caracteristica_mas_alta(int fu, int inte, int su, int ag, int esperado)
        {
            Assert.Equal(esperado, EffectEngine.MejorElementoDe(Con(fu, inte, su, ag), ronda: 1));
        }

        [Fact]
        public void El_empate_se_rompe_siempre_igual()
        {
            // No está medido cuál elige el juego real con dos características iguales, pero hace
            // falta UN criterio estable: dos lanzamientos idénticos tienen que dar lo mismo.
            var quien = Con(300, 300, 300, 300);

            int primero = EffectEngine.MejorElementoDe(quien, ronda: 1);
            Assert.Equal(primero, EffectEngine.MejorElementoDe(quien, ronda: 1));
            Assert.Equal(1, primero);
        }

        [Fact]
        public void El_2822_cuenta_como_dano()
        {
            // Si no contase, sus veinte hechizos no pegarían y no darían ningún error.
            Assert.True(EffectEngine.EsDeDano(2822));
            Assert.True(EffectEngine.EsDeDano(99));
            Assert.False(EffectEngine.EsDeDano(141));
        }
    }
}

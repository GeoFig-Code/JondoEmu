using System.Linq;
using Jondo.Unity.Server.Managers;
using Xunit;

namespace Jondo.Unity.Tests.Combat
{
    /// <summary>
    /// La familia «haz que se lance otro hechizo»: nueve efectos, una sola mecánica.
    /// </summary>
    /// <remarks>
    /// Cuatro de los nueve no tienen ni descripción en el catálogo del cliente, y por eso
    /// hicieron falta las capturas. Censadas las 431 —37.947 tramas jwe, 21.307 lanzamientos,
    /// dos lectores independientes con los mismos totales—, se emparejó cada anuncio de
    /// lanzamiento con el padre que lo produjo:
    ///
    ///   efecto   n      mismo lanzador   misma casilla   objetivo==lanzador
    ///   792      6332   4447             3560            6269
    ///   1160     3826   3783             1772             918
    ///   2160      332    332               54              18
    ///   2792       10      0                2              10
    ///   2793      323     68               74             320
    ///   2794      235    182              228              79
    ///   2795        6      0                6               0
    ///
    /// Y el 1017 aparte: el objetivo del hijo es el lanzador del padre en 97 de 97, y el lanzador
    /// del hijo NO lo es en 97 de 97.
    ///
    /// Lo que esta prueba guarda es la TABLA, porque es donde vive el conocimiento. Cambiar una
    /// fila cambia a quién le pega media clase, y no daría ningún error.
    /// </remarks>
    public class SubcastFamilyTests
    {
        [Fact]
        public void Los_nueve_estan_en_la_tabla_y_ninguno_se_ha_quedado_fuera()
        {
            var esperados = new[] { 792, 1160, 2160, 1017, 2792, 2793, 2794, 2795, 2960 };
            foreach (int efecto in esperados)
            {
                Assert.True(EffectEngine.EsDeLaFamiliaDeSublanzar(efecto),
                            $"el efecto {efecto} debería estar en la familia");
            }

            // Y que no se cuele nada que no lo sea: el 141 mata, no encadena.
            Assert.False(EffectEngine.EsDeLaFamiliaDeSublanzar(141));
            Assert.False(EffectEngine.EsDeLaFamiliaDeSublanzar(5));
        }

        [Theory]
        // El 1017 es el único que devuelve el hechizo al lanzador del padre.
        [InlineData(1017, true, "AlLanzadorPadre")]
        // El 792 y sus primos: el candidato se lo lanza a sí mismo.
        [InlineData(792, true, "AlCandidato")]
        [InlineData(2792, true, "AlCandidato")]
        [InlineData(2793, true, "AlCandidato")]
        [InlineData(2795, true, "AlCandidato")]
        // El 1160 no cambia de lanzador; apunta al candidato.
        [InlineData(1160, false, "AlCandidato")]
        // El 2160 tampoco, y coge al más cercano.
        [InlineData(2160, false, "AlMasCercano")]
        // Los dos de casilla: el 2794 cambia de lanzador y el 2960 no.
        [InlineData(2794, true, "ALaCasillaDelPadre")]
        [InlineData(2960, false, "ALaCasillaDelPadre")]
        public void Cada_uno_lanza_y_apunta_como_dicen_las_capturas(int efecto, bool lanzaElCandidato,
                                                                    string apunta)
        {
            var (quien, aQue) = EffectEngine.ComoSublanza(efecto);

            Assert.Equal(lanzaElCandidato, quien);
            Assert.Equal(apunta, aQue);
        }

        [Theory]
        [InlineData(3792)]
        [InlineData(3793)]
        public void Los_dos_marcadores_de_guion_no_hacen_nada(int efecto)
        {
            // Medido en las 164 filas del 3792 que tienen plantilla, sin una excepción: su value
            // es un id del boundScriptUsageData del propio hechizo, y el resto de la fila está a
            // cero. No lleva ningún número que aplicar a nadie.
            Assert.True(EffectEngine.EsMarcadorDeGuion(efecto));
            Assert.False(EffectEngine.EsDeLaFamiliaDeSublanzar(efecto));
            Assert.False(EffectEngine.EsDeDano(efecto));
        }

        [Fact]
        public void El_3793_no_hace_nada_y_esta_bien_que_no_lo_haga()
        {
            // 430 filas sin texto, sin característica, sin dados y sin duración. El servidor real
            // lo registra y lo anuncia, pero no arrastra a nadie: los efectos que van con él se
            // disparan solos, con su propio disparador. Tratarlo como una puerta condicional
            // sería inventarse una mecánica.
            Assert.False(EffectEngine.EsDeLaFamiliaDeSublanzar(3793));
        }
    }
}

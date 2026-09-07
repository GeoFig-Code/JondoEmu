using System.Collections.Generic;
using System.Linq;
using Jondo.Unity.Server;
using DbCharacter = Jondo.Unity.Server.DatabaseManager.DbCharacter;
using Xunit;

namespace Jondo.Unity.Tests.Sessions
{
    /// <summary>
    /// El personaje recién creado va EL PRIMERO de la lista que se manda después.
    /// </summary>
    /// <remarks>
    /// El cliente no pregunta cuál quieres: coge el primero de la lista y manda su selección al
    /// instante. En el registro se ve con cinco milisegundos de diferencia:
    ///
    ///   00:16:05.798  Creado Tymaviejas (id 13825564)
    ///   00:16:05.803  Selected character 13825558     <- el anterior
    ///
    /// Nuestra lista sale de un «ORDER BY Id», así que el recién creado —el id más alto— iba el
    /// último y el cliente entraba al mundo con el viejo. Había que volver a la pantalla de
    /// selección y elegirlo a mano.
    ///
    /// Que el nuevo va delante está medido en «crear personaje - borrar personaje»: el kvi que
    /// sigue al kvb lleva a «Vos-Xx», el que se acaba de crear, por delante de «Berru».
    /// </remarks>
    public class NewCharacterFirstTests
    {
        /// <summary>Lo mismo que hace el manejador al mandar la lista tras crear.</summary>
        private static List<DbCharacter> ConElNuevoDelante(
            List<DbCharacter> lista, long recienCreado)
        {
            var nuevo = lista.Find(c => c.Id == recienCreado);
            if (nuevo != null)
            {
                lista.Remove(nuevo);
                lista.Insert(0, nuevo);
            }
            return lista;
        }

        private static List<DbCharacter> Lista(params long[] ids)
            => ids.Select(i => new DbCharacter { Id = i, Name = "P" + i }).ToList();

        [Fact]
        public void El_recien_creado_encabeza_la_lista()
        {
            // Los ids salen ordenados de la base; el nuevo es el más alto y sin esto iba último.
            var lista = ConElNuevoDelante(Lista(13825558, 13825560, 13825564), 13825564);

            Assert.Equal(13825564, lista[0].Id);
            Assert.Equal(3, lista.Count);
        }

        [Fact]
        public void Los_demas_conservan_su_orden_relativo()
        {
            var lista = ConElNuevoDelante(Lista(100, 200, 300, 400), 300);

            Assert.Equal(new long[] { 300, 100, 200, 400 }, lista.Select(c => c.Id).ToArray());
        }

        [Fact]
        public void Si_el_nuevo_no_esta_la_lista_se_queda_como_estaba()
        {
            // No debe romperse ni reordenar por su cuenta: mejor una lista intacta que una
            // barajada por un id que no existe.
            var lista = ConElNuevoDelante(Lista(100, 200), 999);

            Assert.Equal(new long[] { 100, 200 }, lista.Select(c => c.Id).ToArray());
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Jondo.Unity.World.Fights;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// Los Sueños Infinitos: el mapa de un sueño y por dónde va cada jugador.
    /// </summary>
    /// <remarks>
    /// Es la versión del POZO, la refundición que convirtió los Sueños en un roguelite: eliges
    /// dificultad, te dan un mapa de salas con bifurcaciones, y en cada sala hay un grupo y una
    /// modificación. Las anteriores funcionaban de otra manera y no valen de referencia.
    ///
    /// Todo lo de aquí sale de las trece capturas de <c>Sueños Infinitos/</c>. El mensaje que abre
    /// la ventana, el iyj, trae DOS listas y son la clave del asunto:
    ///
    /// <code>
    ///   las salas    f1 = "0".."10"
    ///                  f6   la fila del grafo
    ///                  f4   what the room gives: a Reward, whose f9 is its InfiniteDreamRewardData row
    ///   el grafo     0 -> 1,2   1 -> 3,4   2 -> 4,5   3 -> 6,7
    ///                4 -> 7,8   5 -> 8,9   6..9 -> 10
    /// </code>
    ///
    /// Que dibuja un rombo de once salas en cinco filas —1, 2, 3, 4, 1— y no un árbol: a la sala 4
    /// se llega desde la 1 y desde la 2.
    ///
    ///   MEDIDO en la captura de Paradoja I, sala por sala: la fila que dice el f6 de cada una
    ///   coincide exactamente con la que le toca en el grafo. The f9 of those rooms -- 14931,
    ///   14812, 15026, 14798, 14797 -- were read here as MapMobs groups, and they are rewards:
    ///   14798 is +20% vitality in every room it appears in, while the bestiary lists different
    ///   monsters each time. The group a room is fought against is chosen here from MapMobs and
    ///   does not travel in the graph.
    ///
    /// La dificultad va de 1 a 10 y la numeración también está medida, comparando el ixf de nueve
    /// capturas contra el nombre que el jugador eligió en cada una:
    ///
    /// <code>
    ///   1..3   Sueño I, II, III            8..10  Pesadilla I, II, III
    ///   4..7   Paradoja I, II, III, IV
    /// </code>
    /// </remarks>
    public static class Dreams
    {
        /// <summary>Cuántas salas hay en cada fila del rombo. Medido sobre el grafo del iyj.</summary>
        private static readonly int[] Filas = { 1, 3, 3, 3, 1 };

        /// <summary>Lo que puede medir una fila de en medio. Medido de 2 a 4 en nueve capturas.</summary>
        private const int MinimoPorFila = 2;
        private const int MaximoPorFila = 4;

        /// <summary>Y lo que suman las tres juntas: de 7 a 9, o sea sueños de 9, 10 u 11 salas.</summary>
        private const int MinimoDeEnMedio = 7;
        private const int MaximoDeEnMedio = 9;

        /// <summary>
        /// The bonus of each difficulty to experience and loot, in percent: the f22 of the izg,
        /// and the f8 it starts from.
        /// </summary>
        /// <remarks>
        /// Measured in the f22 of the izg of the captures, one difficulty, one value:
        ///
        ///   1: 50   2: 75   3: 100   4: 120   5: 140
        ///   6: 160  7: 190  8: 220   9: 250  10: 300
        ///
        /// These were read for a time as the dream points a dream starts with, and they are not:
        /// the client paints f8 and f22 as the two percentages under the dream's name -- "220%
        /// 220%" in a Pesadilla I -- and the dream points are the f11. What gave it away is the
        /// Rey Gob: "multiply the dream points by 1.5" takes f11 from 25 to 38 in the long capture
        /// and leaves f8 where it was.
        ///
        /// f22 never moves. f8 does, twice in the captures -- 300 to 315 entering a marked room
        /// of Pesadilla III, 50 to 55 in the second band of the long one -- and no rule covers
        /// both, so here it stays where it starts rather than growing by a rule of mine.
        /// </remarks>
        private static readonly int[] BonusByDifficulty =
        {
            0, 50, 75, 100, 120, 140, 160, 190, 220, 250, 300,
        };

        /// <summary>A difficulty's bonus to experience and loot, in percent: the f22 of the izg.</summary>
        public static int BonusOf(int difficulty)
            => difficulty >= 1 && difficulty < BonusByDifficulty.Length
                ? BonusByDifficulty[difficulty]
                : BonusByDifficulty[1];

        /// <summary>
        /// The dream points a dream starts with: the f11 of the izg at the entrance. Ten in the
        /// five captured dreams of Sueño I to III, five in the four of Paradoja, none in the three
        /// of Pesadilla, where f11 is left out.
        /// </summary>
        public static int StartingDreamPoints(int difficulty)
            => difficulty <= LastSueno ? 10 : difficulty <= LastParadoja ? 5 : 0;

        /// <summary>
        /// The Draconiros arenas a dream starts with, the retries: the f17. One in the Sueño
        /// dreams -- the five izg of the captures that start one carry it -- and none in
        /// Paradoja or Pesadilla, whose izg never do. Dying spends it: it is gone from the izg
        /// that follows a death in "Sueño III-pelear-morir", and from the Sueño I that the player
        /// of "Sueño II-descartar" had going.
        /// </summary>
        public static int StartingArenas(int difficulty) => difficulty <= LastSueno ? 1 : 0;

        /// <summary>The last Sueño (3) and the last Paradoja (7) of the ladder.</summary>
        private const int LastSueno = 3;
        private const int LastParadoja = 7;

        /// <summary>La dificultad más alta, Pesadilla III.</summary>
        public const int MaximaDificultad = 10;

        /// <summary>El mapa del Plano Astral, que es donde está el pozo.</summary>
        /// <remarks>
        /// Medido: es a donde lleva el jru que sigue al iyc del botón del menú, y en nuestra propia
        /// base es la subárea 938, «Dominios de Draconiros».
        /// </remarks>
        public const long MapaDelPozo = 238551040;

        /// <summary>El pozo, que en los datos del cliente es un elemento más de ese mapa.</summary>
        /// <remarks>
        /// El 539616, gráfico 90166, casilla 370. Está en el mapa desde siempre; lo que faltaba era
        /// declararle una acción, porque sin ella el cliente no lo deja pulsar y queda de adorno.
        /// </remarks>
        public const int ElementoDelPozo = 539616;

        /// <summary>La habilidad con la que se usa el pozo.</summary>
        /// <remarks>
        /// El 20743 del iwo «0887a20110e0f720» NO es esto. Es el uid de instancia, y confundir uno
        /// con otro es lo que dejó el pozo sin pulsar: anunciábamos la habilidad 20743, que el
        /// cliente no conoce, y un elemento cuya habilidad no existe no se puede clicar y no da un
        /// solo error. El f11 del jss real del mapa lo dice campo a campo:
        ///
        ///   f11 { f1: 1, f4 { f1: 20744, f2: 360 }, f4 { f1: 20743, f2: 184 }, f5: 539616, f6: -1 }
        ///
        /// El f4.f1 es el uid —lo que el cliente devuelve en el iwo— y el f4.f2 la habilidad. La
        /// 184 es la misma con la que ya se entra en una casa y se usa la lotería, así que el
        /// cliente la conoce de sobra. El uid nuestro lo pone Interactives.SkillInstanceOf y el
        /// cliente lo devuelve tal cual, así que no hace falta copiar el suyo.
        /// </remarks>
        public const int HabilidadDelPozo = 184;

        /// <summary>El tipo de interactivo del pozo y de las arcadas: el f6 del f11, medido en -1.</summary>
        public const int TipoDelPozo = -1;

        /// <summary>La segunda acción del pozo, la del f4 { 20744, 360 }.</summary>
        /// <remarks>
        /// El pozo ofrece DOS cosas, no una: en las 22 tramas jss del mapa 238551040 que hay en las
        /// trece capturas —las 22 idénticas— van dos f4, el de la habilidad 184 y éste. Declarar
        /// sólo uno deja al jugador con media carta.
        ///
        /// Qué contesta el servidor real a ésta no se ha medido: en las capturas nadie la pulsa,
        /// las once veces que se usa el pozo van por la 184. Aquí abre la misma ventana, que es lo
        /// único que sabemos hacer con el pozo, y queda dicho que es una suposición.
        /// </remarks>
        public const int SegundaHabilidadDelPozo = 360;

        /// <summary>El mapa de la sala de entrada de todo sueño.</summary>
        /// <remarks>
        /// Medido en las diez capturas que empiezan un sueño: el jru que sigue al primer izg lleva
        /// siempre aquí, sin excepción. Las salas de pelea vienen después y ésas sí cambian.
        /// </remarks>
        public const long MapaDeEntrada = 237897728;

        /// <summary>La subárea donde viven las salas: 484 mapas hechos para esto.</summary>
        /// <remarks>
        /// Los nueve mapas de sala que aparecen en las capturas —237764608, 237765632, 237766656,
        /// 237767680, 237768704, 237765684, 237765686, 237777980 y 237774854— están todos aquí, y
        /// también la entrada. Cada uno lleva EXACTAMENTE tres elementos interactivos con el
        /// gráfico 90166, que son las tres puertas a la fila de abajo.
        ///
        /// Esto es lo que faltaba para que entrar en un sueño no fuese un viaje a Frigost: se
        /// estaba mandando al jugador al mapa del grupo de monstruos, que es un mapa del mundo.
        /// </remarks>
        public const int SubareaDeLasSalas = 904;

        /// <summary>Cuántas puertas tiene una sala. Tres en los 100 mapas que las traen.</summary>
        public const int PuertasPorSala = 3;

        /// <summary>La sala de Draconiros, al otro lado de cualquiera de las cuatro arcadas.</summary>
        /// <remarks>
        /// No es vecina de la del pozo en la rejilla —una está en (0,0) y la otra en (1,-1)— así que
        /// no se llega andando: se llega pulsando una arcada. Sin declararlas, Draconiros está bien
        /// colocado y es inalcanzable, que para el jugador es lo mismo que no estar.
        /// </remarks>
        public const long MapaDeDraconiros = 238553348;

        /// <summary>Las dos clases de sala del f3: 5 en 63 salas medidas, 15 en 8.</summary>
        private const int ClaseNormal = 5;
        private const int ClaseSenalada = 15;

        /// <summary>Cuántos sueños se le han ofrecido a cada personaje, para el f13.</summary>
        private static readonly Dictionary<long, int> _cuenta = new Dictionary<long, int>();

        /// <summary>
        /// Un potenciador de los Sueños: lo que una sala regala al entrar.
        /// </summary>
        /// <remarks>
        /// Censados los 196 f15 de las quince capturas, y sólo hay dos formas:
        ///
        ///   f15 { f1 { f4: el valor,        f11: el efecto }, f2: 1 }   132 veces
        ///   f15 { f1 { f6 { f1: cuántos },  f11: el efecto }, f2: 1 }    64 veces
        ///
        /// El f11 es un id del catálogo de efectos del propio cliente —2844 es «% vitalidad», 111
        /// «PA», 128 «PM», 117 «alcance»—, así que no hay nada que inventar: el cliente sabe
        /// escribir la línea él solo. La segunda forma es la de los efectos cuyo texto nombra un
        /// hechizo, como el 281 «+#3 de alcance máximo».
        /// </remarks>
        public sealed class Bono
        {
            public Bono(int efecto, int valor, bool anidado = false)
            {
                Efecto = efecto;
                Valor = valor;
                Anidado = anidado;
            }

            /// <summary>El id del catálogo de efectos: el f11.</summary>
            public int Efecto { get; }

            /// <summary>Cuánto da.</summary>
            public int Valor { get; }

            /// <summary>
            /// Si el valor viaja dentro del f6 en vez de en el f4.
            /// </summary>
            /// <remarks>
            /// Es sólo dónde va el número, no a qué se aplica. El «+alcance máximo» del 281 sube
            /// el alcance de TODOS los hechizos, no el de uno; leerlo como una referencia a un
            /// hechizo concreto sería equivocarse con el mismo campo por segunda vez.
            /// </remarks>
            public bool Anidado { get; }
        }

        /// <summary>
        /// A reward of the dreams: what a room gives on entering, or what the fountain sells. The
        /// iww of the wire -- the f4 of a room in the graph, an f6 of the izg at a fountain.
        /// </summary>
        /// <remarks>
        /// Measured field by field in the Paradoja III capture and the long one, zeros written:
        ///
        ///   { f1: kind, f2: 0, f3 (repeated) { a bonus }, f4: 0, f5: points, f7: rarity,
        ///     f8: price, f9: reward id, f10: ?, f11: 0 }
        ///
        /// f9 is a row of the client's own InfiniteDreamRewardData -- what the shop's entries are
        /// built on -- and not a group of monsters, which is what it was taken for: the same 14798
        /// is +20% vitality in every room of every capture, whatever monsters the bestiary lists
        /// there. f10 comes with the reward and is not known. f7 is 1, 2 or 3 where it is written,
        /// the rarity classes the shop paints (common, rare, epic, legendary). Kind 1 is a room
        /// that gives dream points instead of a bonus, f5 of them: entering the Paradoja II one
        /// takes f11 up by ten, the room's own five and these five.
        /// </remarks>
        public sealed class Reward
        {
            /// <summary>The InfiniteDreamRewardData row: the f9.</summary>
            public int Id { get; init; }

            /// <summary>The f10, which comes with the reward and whose meaning is unknown.</summary>
            public int Tag { get; init; }

            /// <summary>The f7: left out when zero.</summary>
            public int Rarity { get; init; }

            /// <summary>The f1: <see cref="RewardKindBonus"/> or <see cref="RewardKindPoints"/>.</summary>
            public int Kind { get; init; }

            /// <summary>The dream points a kind-1 reward gives: the f5.</summary>
            public int Points { get; init; }

            /// <summary>What it costs in dream points at the fountain: the f8. Nothing in a room.</summary>
            public int Price { get; init; }

            /// <summary>The bonuses it gives: the f3, one or two.</summary>
            public IReadOnlyList<Bono> Bonuses { get; init; } = Array.Empty<Bono>();
        }

        public const int RewardKindBonus = 0;
        public const int RewardKindPoints = 1;

        /// <summary>
        /// What rooms give: the nine rewards the rooms of the captures' graphs offer, each with the
        /// id, the f10 and the rarity it always carries there.
        /// </summary>
        /// <remarks>
        /// Counted over every room of every graph of the fifteen captures -- 14798 in 270 rooms,
        /// 14804 in 194, 14931 in 152 and so on down to 14808 in 16. Three more rooms appear
        /// with no bonus and a points field of 15 or 30 (14811, 14812, 14895); what they give is
        /// not in the captures, so they are not handed out. The rooms used to draw from the twenty
        /// (effect, value) pairs of the f15 lists, which also hold what the shop sells, and sent a
        /// MapMobs group in the f9.
        /// </remarks>
        internal static readonly Reward[] RoomRewards =
        {
            new Reward { Id = 14797, Tag = 125, Rarity = 2, Bonuses = new[] { new Bono(111, 1) } },     // AP
            new Reward { Id = 14798, Tag = 119, Bonuses = new[] { new Bono(2844, 20) } },               // % vitality
            new Reward { Id = 14804, Tag = 133, Bonuses = new[] { new Bono(4041, 5) } },                // % damage
            new Reward { Id = 14805, Tag = 127, Rarity = 1, Bonuses = new[] { new Bono(117, 2) } },     // range
            new Reward { Id = 14808, Tag = 140, Rarity = 3, Bonuses = new[] { new Bono(286, 1, true) } },
            new Reward { Id = 14808, Tag = 157, Rarity = 2, Bonuses = new[] { new Bono(291, 1, true) } },
            new Reward { Id = 14850, Tag = 111, Rarity = 2, Bonuses = new[] { new Bono(281, 1, true) } }, // max range
            new Reward { Id = 15026, Tag = 126, Rarity = 2, Bonuses = new[] { new Bono(128, 1) } },     // MP
            new Reward { Id = 14931, Tag = 118, Rarity = 1, Kind = RewardKindPoints, Points = 5 },
        };

        /// <summary>
        /// What the fountain sells: the five offers of the one fountain of the captures, in their
        /// order, 15 dream points each. The long capture's room 9, twice with the same five.
        /// </summary>
        internal static readonly Reward[] ShopOffers =
        {
            new Reward { Id = 14798, Tag = 65, Price = 15, Bonuses = new[] { new Bono(2971, 20), new Bono(2844, 40) } },
            new Reward { Id = 15389, Tag = 149, Rarity = 2, Price = 15, Bonuses = new[] { new Bono(3405, 85231) } },
            new Reward { Id = 14799, Tag = 103, Price = 15, Bonuses = new[] { new Bono(2850, 100), new Bono(2852, 100) } },
            new Reward { Id = 15382, Tag = 89, Rarity = 2, Price = 15, Bonuses = new[] { new Bono(3405, 83685) } },
            new Reward { Id = 14813, Tag = 124, Rarity = 2, Price = 15, Bonuses = new[] { new Bono(115, 25) } },  // % critical
        };

        public sealed class Sala
        {
            /// <summary>Su número, que en el cable viaja como CADENA: «0», «1»…</summary>
            public int Id { get; init; }

            /// <summary>La fila del rombo, de 0 a 4. Es el f6 del iyj.</summary>
            public int Fila { get; init; }

            /// <summary>A qué salas se puede ir desde aquí.</summary>
            public List<int> Salidas { get; } = new List<int>();

            /// <summary>La fila de MapMobs que se pelea aquí. Cero en la entrada.</summary>
            public int Grupo { get; set; }

            /// <summary>Los monstruos de ese grupo, con su grado, para plantarlos en la sala.</summary>
            public List<(int Monstruo, int Grado)> Miembros { get; } = new List<(int, int)>();

            /// <summary>El grupo ya plantado en el mapa de la sala, para poder quitarlo.</summary>
            public long Plantado { get; set; }

            /// <summary>El mapa del mundo donde vive ese grupo. NO es a donde se va el jugador.</summary>
            /// <remarks>
            /// Se guarda para poder plantar la pelea con los monstruos que le tocan; mandarle a él
            /// allí es lo que le dejaba en mitad de Frigost con el minimapa apagado.
            /// </remarks>
            public long MapaId { get; set; }

            /// <summary>El mapa de la subárea 904 en el que ocurre esta sala.</summary>
            public long MapaDeLaSala { get; set; }

            /// <summary>La casilla donde está plantado el grupo.</summary>
            public int Casilla { get; set; }

            /// <summary>What the room gives on entering. Null at the entrance and at a fountain.</summary>
            public Reward? Reward { get; set; }

            /// <summary>The room's bonus, when its reward is one. Null at the entrance and at a fountain.</summary>
            public Bono? Regalo => Reward != null && Reward.Bonuses.Count > 0 ? Reward.Bonuses[0] : null;

            /// <summary>
            /// What a fountain has left to sell: the shop's offers, stocked the first time it is
            /// entered, each gone once bought. Null anywhere else.
            /// </summary>
            public List<Reward>? Offers { get; set; }

            /// <summary>Whether the Rey Gob stands in this fountain. See <see cref="ReyGobOneIn"/>.</summary>
            public bool HasReyGob { get; set; }

            /// <summary>Whether his favor -- the dream points times one and a half -- was taken here.</summary>
            public bool FavorTaken { get; set; }

            /// <summary>El efecto que modifica la sala, y cuánto. Cero: sin modificación.</summary>
            public int Efecto => Regalo?.Efecto ?? 0;
            public int Valor => Regalo?.Valor ?? 0;

            /// <summary>Si ya se ha peleado aquí.</summary>
            public bool Hecha { get; set; }

            /// <summary>Si ya se cobró su potenciador. Se vuelve a entrar al continuar un sueño.</summary>
            public bool Cobrada { get; set; }

            /// <summary>The room's score: its f1 in the graph.</summary>
            /// <remarks>
            /// Measured from 4 to 41 over the rooms of the captures, low in a Sueño and high in a
            /// Pesadilla, with no rule tying it to the row. It is not the dream points: those are
            /// the f3, which is what the door's tooltip says. Handed out by row here.
            /// </remarks>
            public int Score { get; set; }

            /// <summary>
            /// The dream points the room gives when it is entered: its f3, the "5 Puntos de sueño"
            /// of the door's tooltip. 5 in most rooms, 15 in the marked ones, 10 deep in a dream.
            /// </summary>
            public int DreamPoints { get; set; }

            /// <summary>The band the room was made in, from 1. A fountain closes its band and opens the next.</summary>
            public int Franja { get; set; } = 1;

            /// <summary>Sala señalada. El f7, que vale 1 en 8 de las 89 y siempre con Clase 15.</summary>
            public bool Senalada { get; set; }

            /// <summary>Si esta sala es la Fuente Onírica: la tienda, y siempre la última.</summary>
            /// <remarks>
            /// Censadas las 665 salas de las quince capturas, el tipo -el f5- no admite dudas:
            /// las filas 1, 2 y 3 valen 1 en las 529, y la fila 4 vale 3 en las 68. O sea que
            /// TODA sala de en medio es de pelea y la última es SIEMPRE la fuente.
            ///
            /// El propio cliente lo dice al pasar el ratón: «Fuente onírica - TIENDA - te permite
            /// intercambiar tus puntos de sueño por bonus». Lo saca de este número.
            ///
            /// Aquí no hay sala de jefe: en las 665 no sale ni una. El «Fin del Sueño» de la guía
            /// tiene que estar al final del sueño entero, después de varias franjas, y de eso no
            /// hay captura.
            /// </remarks>
            public bool EsFuente { get; set; }
        }

        public sealed class Sueno
        {
            public long CharacterId { get; init; }
            public string Nombre { get; init; } = "";
            public int Nivel { get; init; }
            public int Dificultad { get; init; }

            public List<Sala> Salas { get; } = new List<Sala>();

            /// <summary>En qué sala está. Empieza en la cero, que es la entrada.</summary>
            public int Actual { get; set; }

            /// <summary>The breed of the dreamer: the f4 of the izg's f1, the portrait.</summary>
            public int Breed { get; init; }

            /// <summary>
            /// The dream points: the f11. What the rooms give on entering, what the Rey Gob
            /// multiplies, what the fountain's shop is paid with.
            /// </summary>
            public int DreamPoints { get; set; }

            /// <summary>The bonus to experience and loot now, in percent: the f8.</summary>
            public int Bonus { get; set; }

            /// <summary>The difficulty's bonus, which never moves: the f22.</summary>
            public int BaseBonus { get; init; }

            /// <summary>
            /// The rooms behind: the f3 of every graph, newest first. The entrance and a fountain
            /// count from the moment they are entered, a fight room from the moment it is left:
            /// "0" at the entrance and still "0" in the first fight room, "4", "2", "0" once in
            /// the third -- and the fountain on the list while one stands in it.
            /// </summary>
            public List<int> Visited { get; } = new List<int>();

            /// <summary>Tormentas astrales que quedan. Es el f7, y el número del botón.</summary>
            public int Tormentas { get; set; } = 1;

            /// <summary>Draconiros arenas, the retries: the f17. See <see cref="StartingArenas"/>.</summary>
            /// <remarks>
            /// It was sent as the f19 for a time, and the f19 is something else: whether the room
            /// one stands in is clear. Spending it on a death is not implemented.
            /// </remarks>
            public int Arena { get; set; }

            /// <summary>Los potenciadores ya cobrados, en el orden en que cayeron.</summary>
            /// <remarks>
            /// Se cobra al ENTRAR en la sala, no al ganarla: la guía dice que los bonos se
            /// recogen al entrar y que el combate empieza inmediatamente después.
            /// </remarks>
            public List<Bono> Ganados { get; } = new List<Bono>();

            /// <summary>Por qué franja va, empezando por la I.</summary>
            /// <remarks>
            /// La guía del juego lo dice con todas las letras: «Chaque palier (à l'exception du
            /// premier et du dernier) commencera toujours par une Fontaine Onirique». O sea que la
            /// fuente que ABRE una franja es la última sala de la anterior: la misma vista desde
            /// los dos lados, que es justo lo que se mide —fila 4 con tipo 3 en 68 de 68—.
            ///
            /// Por eso al entrar en la fuente el sueño no se acaba: se le añade la franja
            /// siguiente y se sigue bajando. En la captura larga se ve el grafo creciendo, con
            /// salas de fila 5 y más dentro del mismo f16.
            /// </remarks>
            public int Franja { get; set; } = 1;

            /// <summary>Cuántos sueños se le han ofrecido ya. Es el f13 del iyj.</summary>
            /// <remarks>
            /// Las nueve capturas son del mismo personaje y el f13 vale 1, 2, 3, 4, 5, 6, 8, 9 y
            /// 10, en el orden en que se grabaron. O sea: una cuenta, no un identificador.
            /// </remarks>
            public int Cuenta { get; init; }

            /// <summary>Dónde estaba en el mundo antes de entrar, para devolverlo al salir.</summary>
            public long MapaDeVuelta { get; init; }
            public int CasillaDeVuelta { get; init; }

            public Sala? SalaActual => Buscar(Actual);

            public Sala? Buscar(int id)
            {
                foreach (var s in Salas) if (s.Id == id) return s;
                return null;
            }
        }

        private static readonly ConcurrentDictionary<long, Sueno> _enCurso = new();
        private static readonly Random _azar = new Random();

        /// <summary>Los grupos que se pueden plantar en una sala, por nivel.</summary>
        /// <remarks>
        /// Se leen una vez y se quedan: son 38.744 filas y consultarlas por sala sería una lectura
        /// completa por bifurcación. Sólo interesan el mapa, la casilla y el nivel del grupo.
        /// </remarks>
        private static List<(int Id, long MapaId, int Casilla, int Nivel, string Miembros)>? _grupos;
        private static readonly object _candado = new object();

        public static int Activos => _enCurso.Count;

        public static Sueno? De(long characterId)
            => _enCurso.TryGetValue(characterId, out var s) ? s : null;

        /// <summary>Se acabó el sueño: se olvida, y con él los grupos que dejó plantados.</summary>
        /// <remarks>
        /// Lo segundo importa tanto como lo primero. Los mapas de sala son cien y se reparten
        /// entre todos los sueños; un grupo que no se quita se queda ahí para el siguiente que
        /// caiga en ese mapa, y se va acumulando sala tras sala hasta que la sala tiene monstruos
        /// de tres sueños ajenos.
        /// </remarks>
        public static void Olvidar(long characterId)
        {
            if (!_enCurso.TryRemove(characterId, out var sueno)) return;

            foreach (var sala in sueno.Salas)
            {
                if (sala.Plantado == 0) continue;
                MobSpawnManager.RemoveMobGroup(sala.MapaDeLaSala, sala.Plantado);
                sala.Plantado = 0;
            }
        }

        /// <summary>De donde salio cada uno hacia el Plano Astral.</summary>
        /// <remarks>
        /// Se apunta al pulsar el boton del menu, que es el ultimo momento en que se sabe: dentro
        /// del plano y de las salas el mapa de la sesion ya es otro. Sin esto, salir del sueno
        /// dejaria al jugador en el plano en vez de donde estaba.
        /// </remarks>
        private static readonly ConcurrentDictionary<long, (long Mapa, int Casilla)> _deDonde = new();

        public static void RecordarDeDondeViene(long characterId, long mapa, int casilla)
            => _deDonde[characterId] = (mapa, casilla);

        public static (long Mapa, int Casilla) DeDondeViene(long characterId)
            => _deDonde.TryGetValue(characterId, out var d) ? d : (0, 0);

        /// <summary>Cuántos grupos hay disponibles para plantar en las salas.</summary>
        public static int GruposDisponibles { get { Cargar(); return _grupos?.Count ?? 0; } }

        private static void Cargar()
        {
            if (_grupos != null) return;
            lock (_candado)
            {
                if (_grupos != null) return;
                var grupos = new List<(int, long, int, int, string)>();

                try
                {
                    using var conexion = new Microsoft.Data.Sqlite.SqliteConnection(
                        DatabaseManager.WorldConnectionString);
                    conexion.Open();

                    var orden = conexion.CreateCommand();
                    orden.CommandText = "SELECT Id, MapId, CellId, MembersJson FROM MapMobs;";

                    using var lector = orden.ExecuteReader();
                    while (lector.Read())
                    {
                        if (lector.IsDBNull(3)) continue;

                        int nivel = NivelDe(lector.GetString(3));
                        if (nivel <= 0) continue;

                        grupos.Add((lector.GetInt32(0), lector.GetInt64(1),
                                    lector.IsDBNull(2) ? 0 : lector.GetInt32(2), nivel,
                                    lector.GetString(3)));
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Sueños] No se pudieron leer los grupos: {ex.Message}");
                }

                _grupos = grupos;
            }
        }

        /// <summary>El nivel de un grupo: el del miembro más alto, que es lo que lo hace difícil.</summary>
        /// <summary>Los monstruos de un grupo, con el grado con el que salen en el mundo.</summary>
        private static List<(int Monstruo, int Grado)> MiembrosDe(string miembros)
        {
            var salen = new List<(int, int)>();
            try
            {
                using var doc = JsonDocument.Parse(miembros);
                foreach (var m in doc.RootElement.EnumerateArray())
                {
                    if (!m.TryGetProperty("id", out var id) || !id.TryGetInt32(out int monstruo)) continue;
                    int grado = m.TryGetProperty("grade", out var g) && g.TryGetInt32(out int n) ? n : 0;
                    salen.Add((monstruo, grado));
                }
            }
            catch (Exception) { salen.Clear(); }
            return salen;
        }

        private static int NivelDe(string miembros)
        {
            int mayor = 0;
            try
            {
                using var doc = JsonDocument.Parse(miembros);
                foreach (var m in doc.RootElement.EnumerateArray())
                {
                    if (m.TryGetProperty("level", out var n) && n.TryGetInt32(out int nivel))
                    {
                        if (nivel > mayor) mayor = nivel;
                    }
                }
            }
            catch (Exception) { return 0; }
            return mayor;
        }

        public static void Initialize()
        {
            Cargar();
            Console.WriteLine($"[Sueños] {GruposDisponibles} grupos para plantar en las salas.");
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Montar un sueño
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Genera un sueño nuevo: el rombo de once salas, con su grupo y su modificación.
        /// </summary>
        /// <remarks>
        /// La entrada y la última no llevan grupo — en la captura la sala «0» viaja con un solo
        /// campo y la «10» sin f9 —, así que sólo se puebla lo de en medio.
        /// </remarks>
        public static Sueno Crear(long characterId, string nombre, int nivel, int dificultad,
                                  long mapaDeVuelta, int casillaDeVuelta, int breed = 0)
        {
            Cargar();

            // Empezar uno nuevo tira el anterior, que es lo que hace el cliente al confirmar. Va
            // por Olvidar para que se lleve por delante los grupos que dejó plantados.
            Olvidar(characterId);

            _cuenta.TryGetValue(characterId, out int cuenta);
            _cuenta[characterId] = ++cuenta;

            var sueno = new Sueno
            {
                CharacterId = characterId,
                Nombre = nombre,
                Nivel = nivel,
                Dificultad = Math.Clamp(dificultad, 1, MaximaDificultad),
                Cuenta = cuenta,
                Breed = breed,
                BaseBonus = BonusOf(Math.Clamp(dificultad, 1, MaximaDificultad)),
                Bonus = BonusOf(Math.Clamp(dificultad, 1, MaximaDificultad)),
                DreamPoints = StartingDreamPoints(Math.Clamp(dificultad, 1, MaximaDificultad)),
                Arena = StartingArenas(Math.Clamp(dificultad, 1, MaximaDificultad)),
                MapaDeVuelta = mapaDeVuelta,
                CasillaDeVuelta = casillaDeVuelta,
            };

            // Cinco filas: la entrada, tres de entre dos y cuatro salas, y la última. El ancho de
            // las de en medio cambia de un sueño a otro —nueve capturas y siete repartos
            // distintos— así que se sortea, con una semilla que hace el sueño reproducible.
            var dado = new Random(HashCode.Combine(characterId, cuenta));

            // El total de las tres filas de en medio va de siete a nueve —los sueños medidos
            // tienen nueve, diez u once salas—, así que no vale sortear cada fila por su cuenta:
            // tres tiradas libres de 2 a 4 dan de seis a doce. Se reparte un total. Y la primera
            // fila nunca pasa de tres: la entrada abre a TODAS sus salas y un mapa sólo trae tres
            // puertas, así que con cuatro una quedaría sin puerta que la abriese.
            var anchos = AnchosDeLasFilas(dado);

            MontarUnaFranja(sueno, anchos, nivel, primera: true);

            _enCurso[characterId] = sueno;
            return sueno;
        }

        /// <summary>
        /// Añade la franja siguiente al sueño y devuelve por dónde se entra en ella.
        /// </summary>
        /// <remarks>
        /// Se llama al pisar la Fuente, que es la última sala de la franja en curso y a la vez la
        /// primera de la que viene. Sin esto el jugador se queda encerrado ahí: la fuente no tiene
        /// salidas y el sueño no tiene forma de seguir ni de acabarse.
        /// </remarks>
        public static void AnadirFranja(Sueno sueno)
        {
            Cargar();

            var dado = new Random(HashCode.Combine(sueno.CharacterId, sueno.Cuenta, sueno.Franja));
            var anchos = AnchosDeLasFilas(dado);

            sueno.Franja++;
            MontarUnaFranja(sueno, anchos, sueno.Nivel, primera: false);
        }

        /// <summary>Las tres filas de en medio, con el total que sale medido.</summary>
        private static int[] AnchosDeLasFilas(Random dado)
        {
            var anchos = new int[] { MinimoPorFila, MinimoPorFila, MinimoPorFila };
            int sobran = dado.Next(MinimoDeEnMedio, MaximoDeEnMedio + 1) - MinimoPorFila * 3;
            while (sobran > 0)
            {
                int donde = dado.Next(anchos.Length);
                int tope = donde == 0 ? PuertasPorSala : MaximoPorFila;
                if (anchos[donde] >= tope) continue;
                anchos[donde]++;
                sobran--;
            }
            return anchos;
        }

        /// <summary>
        /// Monta una franja: tres filas de pelea y una Fuente al final.
        /// </summary>
        /// <remarks>
        /// La primera lleva además su sala de entrada; las demás entran por la fuente de la
        /// anterior, que ya está puesta y sólo hay que colgarle las salidas nuevas.
        /// </remarks>
        private static void MontarUnaFranja(Sueno sueno, int[] anchos, int nivel, bool primera)
        {
            int siguiente = 0;
            foreach (var puesta in sueno.Salas) siguiente = Math.Max(siguiente, puesta.Id + 1);

            int filaBase = 0;
            foreach (var puesta in sueno.Salas) filaBase = Math.Max(filaBase, puesta.Fila + 1);

            var porFila = new List<List<Sala>>();

            if (primera)
            {
                var entrada = new Sala { Id = siguiente++, Fila = filaBase++ };
                sueno.Salas.Add(entrada);
                porFila.Add(new List<Sala> { entrada });
            }
            else
            {
                // La fuente de la franja anterior es la puerta de ésta. It stays a fountain: in the
                // long capture room 9 is still of type 3 in both graphs once the second band is
                // open. The newest fountain is the last one on the list, which is why this finds it.
                Sala? fuente = null;
                foreach (var puesta in sueno.Salas) if (puesta.EsFuente) fuente = puesta;
                if (fuente == null) return;
                porFila.Add(new List<Sala> { fuente });
            }

            for (int i = 0; i < anchos.Length; i++)
            {
                var deLaFila = new List<Sala>();
                for (int j = 0; j < anchos[i]; j++)
                {
                    var sala = new Sala { Id = siguiente++, Fila = filaBase, Franja = sueno.Franja };
                    deLaFila.Add(sala);
                    sueno.Salas.Add(sala);
                }
                filaBase++;
                porFila.Add(deLaFila);
            }

            var laFuente = new Sala { Id = siguiente++, Fila = filaBase, EsFuente = true, Franja = sueno.Franja };
            lock (_azar) laFuente.HasReyGob = _azar.Next(ReyGobOneIn) == 0;
            sueno.Salas.Add(laFuente);
            porFila.Add(new List<Sala> { laFuente });

            // Y las salidas. Cada sala se abre a la de su misma posición en la fila siguiente y a
            // la de al lado, que es lo que hace que la de en medio se alcance por dos caminos: en
            // la captura a la 4 se llega desde la 1 y desde la 2.
            for (int fila = 0; fila + 1 < porFila.Count; fila++)
            {
                var esta = porFila[fila];
                var abajo = porFila[fila + 1];

                // La entrada abre a TODA la fila siguiente —«0 -> 1,2,3» en la captura— y la fila
                // de encima de la última lleva entera a la última —«7,8,9 -> 10»—. Las dos cosas
                // están en las nueve.
                if (esta.Count == 1 || abajo.Count == 1)
                {
                    foreach (var origen in esta)
                    {
                        foreach (var destino in abajo) origen.Salidas.Add(destino.Id);
                    }
                    continue;
                }

                for (int i = 0; i < esta.Count; i++)
                {
                    int primero = i * abajo.Count / esta.Count;
                    esta[i].Salidas.Add(abajo[primero].Id);
                    if (primero + 1 < abajo.Count) esta[i].Salidas.Add(abajo[primero + 1].Id);
                }

                // Y que no quede ninguna sin padre. Una sala a la que no se puede llegar se dibuja
                // igual en la ventana, y el jugador la ve y no entiende por qué no la alcanza.
                for (int j = 0; j < abajo.Count; j++)
                {
                    if (esta.Exists(x => x.Salidas.Contains(abajo[j].Id))) continue;

                    // Al que tenga sitio: ninguna sala puede ofrecer más salidas que puertas hay
                    // en su mapa, o la de más no se podría pulsar.
                    var padre = esta.Find(x => x.Salidas.Count < PuertasPorSala)
                                ?? esta[Math.Min(j, esta.Count - 1)];
                    padre.Salidas.Add(abajo[j].Id);
                }
            }

            RepartirMapas(sueno);

            // Las de en medio pelean; la entrada y la fuente, no. Medido: filas 1, 2 y 3 con tipo
            // 1 en las 529, fila 4 con tipo 3 en las 68.
            for (int i = 1; i + 1 < porFila.Count; i++)
            {
                foreach (var sala in porFila[i])
                {
                    if (sala.Miembros.Count > 0) continue;
                    Poblar(sala, nivel, sueno.Dificultad);

                    sala.Senalada = i == porFila.Count - 2 && sala.Id % 3 == 0;
                    sala.DreamPoints = sala.Senalada ? ClaseSenalada : ClaseNormal;
                    sala.Score = i * 5 + (sala.Senalada ? 15 : 5);
                }
            }
        }

        /// <summary>
        /// The dream moves into a room: the room left behind goes on the path, and a room pays
        /// out the first time it is entered -- its bonus and its dream points, before the fight,
        /// which is when the captures show f15 and f11 growing and not after the win. Null for a
        /// room the dream does not have; the bonus it paid, when it paid one.
        /// </summary>
        public static Sala? Enter(Sueno dream, int roomId, out Bono? gained)
        {
            gained = null;
            var room = dream.Buscar(roomId);
            if (room == null) return null;

            if (dream.Actual != roomId && dream.Buscar(dream.Actual) != null) Visit(dream, dream.Actual);
            dream.Actual = roomId;
            if (room.Miembros.Count == 0) Visit(dream, roomId);

            if (!room.Cobrada)
            {
                room.Cobrada = true;
                dream.DreamPoints += room.DreamPoints;
                if (room.Reward != null)
                {
                    if (room.Reward.Kind == RewardKindPoints) dream.DreamPoints += room.Reward.Points;
                    foreach (var bonus in room.Reward.Bonuses) Gain(dream, bonus);
                    gained = room.Regalo;
                }
            }
            if (room.EsFuente && room.Offers == null) room.Offers = new List<Reward>(ShopOffers);
            return room;
        }

        /// <summary>
        /// Buys at the fountain one stands at: the offer named by its reward id, or by its place
        /// in the shop. Its price comes off the dream points, its bonuses join the ones gained, and
        /// it leaves the shop. Null when it cannot be bought, and why in <paramref name="refusal"/>.
        /// </summary>
        public static Reward? Buy(Sueno dream, int which, out string refusal)
        {
            refusal = "";
            var room = dream.SalaActual;
            if (room == null || !room.EsFuente || room.Offers == null)
            {
                refusal = "not at a fountain";
                return null;
            }

            var offer = room.Offers.Find(o => o.Id == which)
                        ?? (which >= 0 && which < room.Offers.Count ? room.Offers[which] : null);
            if (offer == null)
            {
                refusal = $"no offer {which}";
                return null;
            }
            if (dream.DreamPoints < offer.Price)
            {
                refusal = $"{dream.DreamPoints} dream points for a price of {offer.Price}";
                return null;
            }

            dream.DreamPoints -= offer.Price;
            foreach (var bonus in offer.Bonuses) Gain(dream, bonus);
            room.Offers.Remove(offer);
            return offer;
        }

        /// <summary>
        /// A bonus joins the ones gained, added to the one of the same effect when there is one:
        /// the captures never list an effect twice but for 792, whose two carry different spells,
        /// and they do list 3 AP, 3 MP and 2 of maximum range, sums of the rooms' ones.
        /// </summary>
        public static void Gain(Sueno dream, Bono bonus)
        {
            int same = Adds(bonus.Efecto)
                ? dream.Ganados.FindIndex(b => b.Efecto == bonus.Efecto && b.Anidado == bonus.Anidado)
                : -1;
            if (same < 0)
            {
                dream.Ganados.Add(bonus);
                return;
            }
            dream.Ganados[same] = new Bono(bonus.Efecto, dream.Ganados[same].Valor + bonus.Valor, bonus.Anidado);
        }

        /// <summary>
        /// Whether two of an effect add up: not 792, whose value is not a quantity, nor 3405,
        /// the shop's spells, whose "value" is which one.
        /// </summary>
        private static bool Adds(int effect) => effect != 792 && effect != 3405;

        // The effects of the rooms' and the shop's bonuses that a fight knows how to apply.
        private const int ActionPointsEffect = 111;
        private const int MovementPointsEffect = 128;
        private const int RangeEffect = 117;
        private const int SpellsMaxRangeEffect = 281;
        private const int SpellsCooldownEffect = 286;
        private const int SpellsCastsPerTargetEffect = 291;
        private const int CriticalEffect = 115;
        private const int VitalityPercentEffect = 2844;
        private const int DamagePercentEffect = 4041;

        /// <summary>
        /// The dream's bonuses on a fighter of one of its rooms: they are fight bonuses, and the
        /// dream's guide says so -- "bonuses apply during combat". Returns what was applied, and
        /// leaves out what a fight here cannot do yet: the spells of the shop (3405), and the
        /// few f15 effects no room of ours gives.
        /// </summary>
        /// <remarks>
        /// "+N maximum range" with no spell named (281) is the range of every spell, which is what
        /// the fighter's own range is. "% vitality" is of the life the fighter starts with.
        /// </remarks>
        public static List<string> ApplyTo(Fighter fighter, Sueno dream)
        {
            var applied = new List<string>();
            foreach (var bonus in dream.Ganados)
            {
                int v = bonus.Valor;
                switch (bonus.Efecto)
                {
                    case ActionPointsEffect:
                        fighter.MaxAP += v; fighter.CurrentAP += v; break;
                    case MovementPointsEffect:
                        fighter.MaxMP += v; fighter.CurrentMP += v; break;
                    case RangeEffect:
                    case SpellsMaxRangeEffect:
                        fighter.Range += v; break;
                    case CriticalEffect:
                        fighter.CriticalBonus += v; break;
                    case VitalityPercentEffect:
                        int extra = (int)Math.Round(fighter.MaxHP * v / 100.0);
                        fighter.MaxHP += extra; fighter.CurrentHP += extra; break;
                    case DamagePercentEffect:
                        fighter.DamageDealtPercent += v; break;
                    case SpellsCooldownEffect:
                        fighter.CooldownReduction += v; break;
                    case SpellsCastsPerTargetEffect:
                        fighter.ExtraCastsPerTarget += v; break;
                    default:
                        continue;
                }
                applied.Add($"{bonus.Efecto}:{v}");
            }
            return applied;
        }

        private static readonly ConcurrentDictionary<int, bool> _bosses = new();

        /// <summary>
        /// Whether a monster is a boss: the value-4 bit of its template's m_flags. All 137 bosses
        /// of the dungeons carry it and 209 monsters of 5,134 do; it is the f4 of a monster of the
        /// bestiary, measured on the three that carry one.
        /// </summary>
        public static bool IsBoss(int monsterId)
            => _bosses.GetOrAdd(monsterId, id =>
            {
                try
                {
                    using var connection = new Microsoft.Data.Sqlite.SqliteConnection(DatabaseManager.WorldConnectionString);
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT Data FROM MonsterTemplates WHERE Id = $id;";
                    command.Parameters.AddWithValue("$id", id);
                    if (command.ExecuteScalar() is not string data) return false;
                    using var doc = JsonDocument.Parse(data);
                    return doc.RootElement.TryGetProperty("m_flags", out var flags)
                           && flags.TryGetInt64(out long bits) && (bits & BossFlag) != 0;
                }
                catch (Exception)
                {
                    return false;
                }
            });

        private const long BossFlag = 4;

        private static void Visit(Sueno dream, int roomId)
        {
            if (!dream.Visited.Contains(roomId)) dream.Visited.Insert(0, roomId);
        }

        /// <summary>
        /// A cada sala, un mapa de los suyos.
        /// </summary>
        /// <remarks>
        /// La entrada es siempre el 237897728 —diez de diez capturas— y las demás salen del
        /// catálogo de la subárea 904, cogiendo sólo los que traen sus tres puertas. Sin repetir
        /// dentro de un mismo sueño: dos salas en el mismo mapa harían que sus puertas fueran las
        /// mismas y el camino dejaría de significar nada.
        /// </remarks>
        private static void RepartirMapas(Sueno sueno)
        {
            var libres = new List<long>(MapasDeSala());
            if (libres.Count == 0) return;

            var dado = new Random(HashCode.Combine(sueno.CharacterId, sueno.Cuenta, sueno.Franja));
            var fuentes = MapasDeFuente();

            foreach (var sala in sueno.Salas)
            {
                if (sala.MapaDeLaSala != 0) continue;
                if (sala.Fila == 0)
                {
                    sala.MapaDeLaSala = MapaDeEntrada;
                    continue;
                }

                // A fountain on one of the five maps that have the fountain itself: the real one
                // is 237783053, whose fourth element is the Fontaine onirique. On any other map
                // there is nothing to open the shop with.
                if (sala.EsFuente && fuentes.Count > 0)
                {
                    sala.MapaDeLaSala = fuentes[dado.Next(fuentes.Count)];
                    continue;
                }

                int i = dado.Next(libres.Count);
                sala.MapaDeLaSala = libres[i];
                libres.RemoveAt(i);

                if (libres.Count == 0) libres.AddRange(MapasDeSala());
            }
        }

        private static List<long>? _mapasDeSala;

        /// <summary>Los mapas de sala: subárea 904 y con sus tres puertas.</summary>
        /// <remarks>
        /// La subárea trae 484 mapas y sólo 100 llevan elementos interactivos. Los que los llevan
        /// llevan exactamente tres, con el gráfico 90166 —el mismo del pozo—, que son las puertas.
        /// Un mapa de sala sin puertas sería un callejón del que no se puede salir.
        /// </remarks>
        /// <summary>Todos los mapas del sueño, la entrada incluida, para declararles las puertas.</summary>
        public static IEnumerable<long> TodosLosMapasDeSala()
        {
            yield return MapaDeEntrada;
            foreach (long mapa in MapasDeSala()) yield return mapa;
            foreach (long mapa in MapasDeFuente()) yield return mapa;
        }

        /// <summary>
        /// The graphics of a room's doors: 90166, the pools of 69 maps, and 65148, the jets of 22
        /// -- blue or red, the colour of what is behind them.
        /// </summary>
        private static readonly HashSet<int> DoorGfx = new HashSet<int> { 90166, 65148 };

        /// <summary>
        /// The Fontaine onirique of a fountain room: graphic 94001, the fourth element of the five
        /// fountain maps -- 539708 on the long capture's 237783053, declared with skill 355,
        /// "Consultar", where the doors have 184.
        /// </summary>
        public const int FountainGfx = 94001;
        public const int FountainSkill = 355;

        /// <summary>The doors of a map, in its own order: its elements of a door's graphic.</summary>
        public static List<Interactives.Element> DoorsOf(long mapId)
            => Interactives.ElementsOf(mapId).Where(e => DoorGfx.Contains(e.Gfx)).ToList();

        /// <summary>The Fontaine onirique of a room, or zero.</summary>
        public static int FountainOf(Sala sala)
        {
            if (sala.MapaDeLaSala == 0) return 0;
            foreach (var element in Interactives.ElementsOf(sala.MapaDeLaSala))
                if (element.Gfx == FountainGfx) return element.Id;
            return 0;
        }

        private static List<long>? _mapasDeFuente;

        /// <summary>The five maps of subarea 904 with the fountain and its three doors.</summary>
        private static List<long> MapasDeFuente()
        {
            if (_mapasDeFuente != null) return _mapasDeFuente;
            var salen = MapasDeLaSubarea()
                .Where(m => DoorsOf(m).Count >= PuertasPorSala
                            && Interactives.ElementsOf(m).Any(e => e.Gfx == FountainGfx))
                .OrderBy(m => m).ToList();
            if (salen.Count > 0) _mapasDeFuente = salen;
            return salen;
        }

        /// <summary>
        /// The Rey Gob stands in one fountain in this many. Not measured: the guide only says he
        /// is "much rarer than the other" goblins, and the one capture that meets him meets him
        /// at a fountain. He used to stand in every one, where the shop is what belongs.
        /// </summary>
        public const int ReyGobOneIn = 4;

        /// <summary>The maps of subarea 904, the dream's.</summary>
        private static HashSet<long> MapasDeLaSubarea()
        {
            var deLaSubarea = new HashSet<long>();
            try
            {
                using var conexion = new Microsoft.Data.Sqlite.SqliteConnection(
                    DatabaseManager.WorldConnectionString);
                conexion.Open();

                var orden = conexion.CreateCommand();
                orden.CommandText = "SELECT MapId FROM MapSubareas WHERE SubAreaId = $sub;";
                orden.Parameters.AddWithValue("$sub", SubareaDeLasSalas);

                using var lector = orden.ExecuteReader();
                while (lector.Read()) deLaSubarea.Add(lector.GetInt64(0));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Sueños] No se han podido leer los mapas de sala: {ex.Message}");
            }
            return deLaSubarea;
        }

        private static List<long> MapasDeSala()
        {
            if (_mapasDeSala != null) return _mapasDeSala;

            var deLaSubarea = MapasDeLaSubarea();
            var salen = new List<long>();
            foreach (long mapId in deLaSubarea)
            {
                if (mapId == MapaDeEntrada) continue;
                var elementos = Interactives.ElementsOf(mapId);
                if (elementos.Count < PuertasPorSala) continue;
                // A fight room is doors and nothing else: the fountain maps and 237787188, whose
                // fourth element (306053) is something else again, are not fight rooms.
                if (elementos.Any(e => !DoorGfx.Contains(e.Gfx))) continue;
                salen.Add(mapId);
            }

            salen.Sort();

            // Vacío NO se guarda. Si esto se pide antes de que Interactives esté cargado la lista
            // sale vacía, y cachearla dejaría todos los sueños de la sesión sin mapas de sala.
            if (salen.Count == 0) return salen;

            _mapasDeSala = salen;
            Console.WriteLine($"[Sueños] {salen.Count} mapas de sala en la subárea {SubareaDeLasSalas}.");
            return _mapasDeSala;
        }

        /// <summary>La puerta número <paramref name="cual"/> de una sala, o cero si no la tiene.</summary>
        public static int PuertaDe(Sala sala, int cual)
        {
            if (sala.MapaDeLaSala == 0) return 0;

            var puertas = DoorsOf(sala.MapaDeLaSala);
            if (cual < 0 || cual >= puertas.Count) return 0;
            return puertas[cual].Id;
        }

        /// <summary>El Rey Gob del Favor Onírico, y dónde se pone.</summary>
        /// <remarks>
        /// Medido en «sueño infinito largo»: npc 7850, casilla 232, orientación 3, con el id
        /// contextual negativo de siempre. Su diálogo está en content/npcs/dialogues.json.
        /// </remarks>
        public const int ReyGob = 7850;
        public const int CasillaDelReyGob = 232;
        public const int OrientacionDelReyGob = 3;

        /// <summary>Le pone a una sala su grupo y su modificación.</summary>
        /// <remarks>
        /// El grupo se elige entre los que andan por el nivel del personaje, con una banda que se
        /// abre si no hay bastantes: los Sueños se juegan a partir del 50 y hay tramos del mundo
        /// donde no hay grupos de ese nivel exacto.
        /// </remarks>
        private static void Poblar(Sala sala, int nivel, int dificultad)
        {
            var candidatos = new List<(int Id, long MapaId, int Casilla, int Nivel, string Miembros)>();

            for (int banda = 20; banda <= 200 && candidatos.Count == 0; banda += 40)
            {
                foreach (var g in _grupos!)
                {
                    if (Math.Abs(g.Nivel - nivel) <= banda) candidatos.Add(g);
                }
            }
            if (candidatos.Count == 0) return;

            (int Id, long MapaId, int Casilla, int Nivel, string Miembros) elegido;
            lock (_azar) elegido = candidatos[_azar.Next(candidatos.Count)];

            sala.Grupo = elegido.Id;
            sala.MapaId = elegido.MapaId;
            sala.Casilla = elegido.Casilla;

            sala.Miembros.Clear();
            sala.Miembros.AddRange(MiembrosDe(elegido.Miembros));

            // Y lo que regala la sala, de las nueve recompensas que ofrecen las salas medidas.
            lock (_azar)
            {
                sala.Reward = RoomRewards[_azar.Next(RoomRewards.Length)];
            }
        }

        internal static void OlvidarTodo()
        {
            _enCurso.Clear();
            _cuenta.Clear();
        }
    }
}

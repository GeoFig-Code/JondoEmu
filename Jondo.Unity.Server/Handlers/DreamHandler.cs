using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.World.Fights;

namespace Jondo.Unity.Server.Handlers
{
    /// <summary>
    /// Los Sueños Infinitos: abrir la ventana, empezar, moverse de sala y salir.
    /// </summary>
    /// <remarks>
    /// El ciclo entero, medido sobre las trece capturas de <c>Sueños Infinitos/</c>:
    ///
    /// <code>
    ///   C-&gt;S  iwo          usar el pozo, que es un interactivo corriente
    ///   S-&gt;C  iyj          el mapa del sueño: once salas y el grafo que las une
    ///   C-&gt;S  ixf { f1 }   empezar en la dificultad que lleva dentro
    ///   S-&gt;C  izg + jru    el estado, y el cambio de mapa a la primera sala
    ///   S-&gt;C  ixa          el acuse, vacío y por la raíz 3
    ///   C-&gt;S  iwo          elegir puerta
    ///   S-&gt;C  izg + jru    estado nuevo, y a la sala siguiente
    ///   C-&gt;S  iyx          salir
    ///   S-&gt;C  jru + ixg + iom  y el iyb «0801» por la raíz 3
    /// </code>
    ///
    /// Lo bueno de esto es cuánto se apoya en lo que ya hay: el pozo y cada puerta son
    /// <c>iwo</c>, el interactivo de toda la vida; las salas se pueblan con filas de
    /// <see cref="Dreams"/> sacadas de MapMobs; y la modificación de cada sala es un efecto del
    /// mismo catálogo que mueve el motor de hechizos.
    /// </remarks>
    public static class DreamHandler
    {
        /// <summary>
        /// El Plano Astral, que es a donde lleva el boton del menu.
        /// </summary>
        /// <remarks>
        /// Medido: el jru que sigue al iyc va al 238551040, que en nuestra propia base es la
        /// subarea 938, «Dominios de Draconiros». Es el vestibulo de los Suenos, no una sala.
        /// </remarks>
        public const long PlanoAstral = 238551040;

        // ═══════════════════════════════════════════════════════════════════
        //  El boton del menu
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// El boton de Suenos Infinitos del menu, y la tecla T (iyc).
        /// </summary>
        /// <remarks>
        /// No abre la ventana: TELETRANSPORTA al Plano Astral, y alli el pozo es el que la abre.
        /// Medido en la captura, donde al iyc le siguen un jru al plano y el iom de siempre.
        ///
        /// Se apunta de donde viene para poder devolverlo: si ya esta en el plano no se hace nada,
        /// que si no un segundo toque a la tecla se guardaria el plano como sitio de vuelta y el
        /// jugador se quedaria alli para siempre.
        /// </remarks>
        public static async Task ToAstralPlaneAsync(NetworkStream stream)
        {
            if (GameState.MapId == PlanoAstral)
            {
                Console.WriteLine("[Sueños] Ya está en el Plano Astral.");
                return;
            }

            Dreams.RecordarDeDondeViene(GameState.CharacterId, GameState.MapId, GameState.CellId);

            int aterriza = await TeleportHandler.ToMapAsync(stream, PlanoAstral, 0);
            Console.WriteLine($"[Sueños] {GameState.CharacterId} al Plano Astral, casilla {aterriza}.");
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Abrir la ventana
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Enseña el mapa del sueño. Es lo que contesta al usar el pozo.</summary>
        public static async Task ShowAsync(NetworkStream stream)
        {
            var yo = GameState.CharacterId;
            var sueno = Dreams.De(yo);

            // Sin sueño en curso se enseña uno nuevo, que es lo que hace la ventana: ofrece.
            sueno ??= Dreams.Crear(yo, GameState.CharacterName, GameState.CharacterLevel, 1,
                                   GameState.MapId, GameState.CellId, GameState.Breed);

            // Primero soltar el elemento. En la captura de Pesadilla II el orden es exacto:
            //
            //   C->S iwo  0887a20110e0f720
            //   S->C iwn  080110e0f72020b80128a28280c8e708
            //   S->C iyj  (618 B)
            //
            // Y el orden importa: sin el iwn el cliente sigue teniendo el pozo por ocupado y no
            // abre la ventana que le llega detrás. No da ningún error; simplemente no pasa nada,
            // que es lo que se vio al pulsarlo. El f4 de ese iwn es 184, la misma habilidad que
            // ya se anuncia en el f11.
            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.Push(Op.Iwn, ConnectionProtocol.BuildElementInUse(
                    Dreams.ElementoDelPozo, Dreams.HabilidadDelPozo, yo)));

            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.Push(Op.Iyj, DreamProtocol.BuildDreamMap(sueno)));

            Console.WriteLine($"[Sueños] Mapa ofrecido a {yo}: {sueno.Salas.Count} salas.");
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Empezar y descartar
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Empezar un sueño (ixf f1) o entrar en el que ya hay (ixf f2).</summary>
        /// <remarks>
        /// El f2 se leyó mal durante un tiempo: se tomó por «descartar» porque las capturas donde
        /// aparece se llaman «descartar el sueño en curso». Los bytes dicen otra cosa. En
        /// «continuar sueño infinito» y en «Sueño II-descartar», el mismo «12020801» va seguido de
        /// un izg y de un jru A UNA SALA: el jugador ENTRA.
        ///
        ///   C->S ixf  12020801
        ///   S->C izg  (1150 B)
        ///   S->C jru  108080b071      -> 237764608, la sala en la que estaba
        ///   S->C ixa  (raíz 3, vacío)
        ///
        /// Y descartar no tiene mensaje propio: en «Sueño III-descartar» y «paradoja I-descartar»
        /// el cliente manda directamente el f1 con la dificultad nueva. La ventana de «ya tienes
        /// un sueño en curso» se resuelve en el cliente; al servidor sólo le llega el comienzo.
        /// </remarks>
        public static async Task StartOrDiscardAsync(NetworkStream stream, byte[] payload)
        {
            byte[]? ixf = ConnectionProtocol.ReadPayload(payload, Op.Ixf);
            if (ixf == null) return;

            int dificultad = 0;
            bool continuar = false;

            foreach (var field in ProtoMessage.Parse(ixf).Fields)
            {
                if (field.WireType != 2 || field.BytesValue == null) continue;

                if (field.FieldNumber == 1)
                {
                    // Empezar: la dificultad va en el f3 de dentro.
                    foreach (var dentro in ProtoMessage.Parse(field.BytesValue).Fields)
                    {
                        if (dentro.FieldNumber == 3 && dentro.WireType == 0)
                        {
                            dificultad = (int)dentro.VarIntValue;
                        }
                    }
                }
                else if (field.FieldNumber == 2)
                {
                    continuar = true;
                }
            }

            long yo = GameState.CharacterId;

            if (continuar)
            {
                var enCurso = Dreams.De(yo);
                if (enCurso == null)
                {
                    Console.WriteLine($"[Sueños] {yo} quiere continuar y no tiene sueño en curso.");
                    return;
                }

                Console.WriteLine($"[Sueños] {yo} continúa en la sala {enCurso.Actual}.");

                await EntrarEnSalaAsync(stream, enCurso, enCurso.Actual);

                await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                    ConnectionProtocol.Answer(Op.Ixa, null, ConnectionProtocol.RequestId(payload)));
                return;
            }

            if (dificultad <= 0 || dificultad > Dreams.MaximaDificultad)
            {
                Console.WriteLine($"[Sueños] Dificultad {dificultad} fuera de la escalera de 1 a " +
                                  $"{Dreams.MaximaDificultad}. ixf: " +
                                  Convert.ToHexString(ixf).ToLowerInvariant());
                return;
            }

            var sueno = Dreams.Crear(yo, GameState.CharacterName, GameState.CharacterLevel,
                                     dificultad, GameState.MapId, GameState.CellId, GameState.Breed);

            Console.WriteLine($"[Sueños] {yo} empieza en dificultad {dificultad}: " +
                              $"{sueno.Salas.Count} salas.");

            await EntrarEnSalaAsync(stream, sueno, 0);

            // El acuse va por la raíz 3, vacío, con el id de la petición.
            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.Answer(Op.Ixa, null, ConnectionProtocol.RequestId(payload)));
        }

        // ═══════════════════════════════════════════════════════════════════
        //  Moverse
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Una puerta del sueño, pulsada. Devuelve falso si esa habilidad no es de ninguna puerta.
        /// </summary>
        /// <remarks>
        /// Se llama desde el manejador de interactivos, antes de que trate el iwo como lo que trata
        /// siempre: dentro de un sueño las puertas son interactivos que no existen en el mapa de
        /// rol, así que el camino normal no sabría qué hacer con ellas.
        /// </remarks>
        public static async Task<bool> TryDoorAsync(NetworkStream stream, int elementId)
        {
            var sueno = Dreams.De(GameState.CharacterId);
            if (sueno == null) return false;

            var actual = sueno.SalaActual;
            if (actual == null) return false;

            // The Fontaine onirique of a fountain room: the shop, not a way out.
            if (actual.EsFuente && elementId == Dreams.FountainOf(actual))
            {
                await ShopAsync(stream, sueno, elementId);
                return true;
            }

            // Y no se sale de una sala sin haberla limpiado. La guía lo dice de la única manera
            // que importa: «es absolutamente imposible volver atrás» una vez entras, y se avanza
            // sala a sala peleando. Con las puertas abiertas desde el principio se podía recorrer
            // el sueño entero sin dar un golpe, cobrando los puntos de todas las salas.
            //
            // La entrada y la última no tienen grupo, así que no bloquean.
            if (!SalaSuperada(actual))
            {
                Console.WriteLine($"[Sueños] La sala {actual.Id} todavía tiene su grupo en pie: " +
                                  "no se abre la puerta.");
                return false;
            }

            // Las puertas son los elementos del propio mapa de la sala, en su orden: la primera
            // lleva a la primera salida, la segunda a la segunda. Los mapas de la subárea 904
            // traen tres, que es también el máximo de salidas que se ha medido en una sala.
            for (int cual = 0; cual < actual.Salidas.Count; cual++)
            {
                if (Dreams.PuertaDe(actual, cual) != elementId) continue;

                // Soltar la puerta ANTES del izg y del jru. Medido en la captura de Sueño III:
                //
                //   C->S iwo  08d0a59f0310f7f620          el elemento 539511
                //   S->C iwn  080110f7f62020b80128…       con la habilidad 184
                //   S->C izg  (833 B)
                //   S->C jru  108090b071
                //
                // Es el mismo orden que el del pozo, y saltárselo tiene el mismo precio: el
                // cliente se queda con la puerta por ocupada y no pasa nada de lo que venga
                // detrás. Sin un solo error.
                await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                    ConnectionProtocol.Push(Op.Iwn, ConnectionProtocol.BuildElementInUse(
                        elementId, Dreams.HabilidadDelPozo, GameState.CharacterId)));

                await EntrarEnSalaAsync(stream, sueno, actual.Salidas[cual]);
                return true;
            }

            return false;
        }

        /// <summary>¿Se puede salir ya de esta sala?</summary>
        /// <remarks>
        /// Una sala sin grupo —la entrada y la última— se cruza sin más. Una de pelea hace falta
        /// haberla ganado: <see cref="Dreams.Sala.Hecha"/> lo pone el final del combate.
        ///
        /// Se mira también si el grupo sigue plantado, y no sólo la marca, porque son dos cosas
        /// distintas: la marca dice que se ganó, y el grupo en pie dice que sigue ahí. Con sólo
        /// una de las dos, un sueño continuado tras reconectar dejaría pasar sin pelear.
        /// </remarks>
        private static bool SalaSuperada(Dreams.Sala sala)
        {
            if (sala.Miembros.Count == 0) return true;
            if (sala.Hecha) return true;
            return sala.Plantado == 0;
        }

        /// <summary>Mete al jugador en una sala: el estado y el cambio de mapa.</summary>
        private static async Task EntrarEnSalaAsync(NetworkStream stream, Dreams.Sueno sueno,
                                                    int salaId)
        {
            int pointsBefore = sueno.DreamPoints;
            var sala = Dreams.Enter(sueno, salaId, out var gained);
            if (sala == null) return;

            // El potenciador y los puntos se cobran AL ENTRAR, antes de pelear, y una sola vez por sala.
            if (gained != null || sueno.DreamPoints != pointsBefore)
            {
                Console.WriteLine($"[Sueños] Sala {sala.Id}: +{sueno.DreamPoints - pointsBefore} dream points, " +
                                  $"{sueno.DreamPoints} in all" +
                                  (gained != null ? $"; bonus {gained.Efecto} of {gained.Valor}, " +
                                                    $"{sueno.Ganados.Count} so far." : "."));
            }

            // Pisar la Fuente abre la franja siguiente, porque la fuente es a la vez la última
            // sala de ésta y la primera de la que viene: «Chaque palier commencera toujours par
            // une Fontaine Onirique». Si no se añade aquí, el jugador entra en una sala sin
            // salidas y se queda encerrado, que es lo que pasaba.
            if (sala.EsFuente && sala.Salidas.Count == 0)
            {
                Dreams.AnadirFranja(sueno);
                Console.WriteLine($"[Sueños] Franja {sueno.Franja} abierta: " +
                                  $"{sueno.Salas.Count} salas en total.");
            }

            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.Push(Op.Izg, StateOf(sueno)));

            // Y el cambio de mapa: al mapa DE LA SALA, que es uno de los 484 de la subárea 904
            // hechos para esto, no al del grupo de monstruos. Mandarle al del grupo es lo que le
            // dejaba de pie en Frigost, andando por el mundo y sin minimapa.
            long mapa = sala.MapaDeLaSala;
            if (mapa == 0)
            {
                Console.WriteLine($"[Sueños] La sala {salaId} se ha quedado sin mapa propio.");
                return;
            }

            // El grupo se planta ANTES del cambio de mapa: el jss que el cliente pide justo
            // después es el que lleva los actores, y un grupo plantado un instante tarde no
            // aparece hasta que se vuelve a entrar.
            if (sala.EsFuente) { if (sala.HasReyGob) PlantarLaTienda(sala); } else PlantarElGrupo(sala);

            int aterriza = await TeleportHandler.ToMapAsync(stream, mapa, 0);

            Console.WriteLine($"[Sueños] Sala {salaId} (fila {sala.Fila}): mapa {mapa}, " +
                              $"grupo {sala.Grupo} del mapa {sala.MapaId} con " +
                              $"{sala.Miembros.Count} monstruo(s), efecto {sala.Efecto} de " +
                              $"{sala.Valor}. Aterriza en {aterriza}.");
        }

        /// <summary>
        /// Pone al vendedor en la Fuente Onírica.
        /// </summary>
        /// <remarks>
        /// No hace falta protocolo nuevo: la fuente de los Sueños es un NPC y punto. Se coloca
        /// como cualquier otro y el motor de diálogos hace el resto; lo que ofrece va escrito en
        /// su respuesta, con el porcentaje de puntos que da.
        /// </remarks>
        private static void PlantarLaTienda(Dreams.Sala sala)
        {
            if (sala.MapaDeLaSala == 0) return;

            Managers.Npcs.PonerDelSueno(sala.MapaDeLaSala, Dreams.ReyGob,
                                        Dreams.CasillaDelReyGob, Dreams.OrientacionDelReyGob);
        }

        /// <summary>
        /// Pone en la sala los monstruos que le tocan, si no están ya.
        /// </summary>
        /// <remarks>
        /// Sin esto la sala está vacía y no hay nada que atacar: el cliente pide la pelea con un
        /// hqa que lleva el id contextual de un grupo del mapa, así que si no hay grupo no hay
        /// manera de empezar. En la captura de Sueño III se ve el hqa con ese negativo justo antes
        /// del kub, y hasta ahí llega la sala sin dar ningún error: simplemente no se puede pelear.
        ///
        /// La entrada y la última no llevan grupo, que es lo que dicen las nueve capturas.
        /// </remarks>
        private static void PlantarElGrupo(Dreams.Sala sala)
        {
            if (sala.Miembros.Count == 0 || sala.MapaDeLaSala == 0) return;

            // Ya plantado: se vuelve a entrar en la misma sala al continuar un sueño.
            if (sala.Plantado != 0
                && MobSpawnManager.GetMobGroupById(sala.Plantado) != null) return;

            var grupo = MobSpawnManager.SpawnComposed(sala.MapaDeLaSala, sala.Miembros);
            if (grupo == null)
            {
                Console.WriteLine($"[Sueños] La sala {sala.Id} no ha podido plantar su grupo.");
                return;
            }

            sala.Plantado = grupo.MobId;
        }

        /// <summary>
        /// Se ha ganado la pelea de una sala: la marca hecha.
        /// </summary>
        /// <remarks>
        /// Devuelve verdadero si el grupo derrotado era el de una sala, que es lo que le dice al
        /// motor de combate que NO reponga otro en su sitio.
        ///
        /// Winning pays nothing: the room paid its dream points when it was entered. In the long
        /// capture the izg that follows a win carries the same f11 as the one of the entrance,
        /// and only the f18 and f19 change. This used to add the room's score to the f8, which is
        /// the bonus to experience and loot: 220% became 275% in three rooms.
        /// </remarks>
        public static bool SalaLimpiada(long grupoDerrotado)
        {
            if (grupoDerrotado == 0) return false;

            var sueno = Dreams.De(GameState.CharacterId);
            if (sueno == null) return false;

            foreach (var sala in sueno.Salas)
            {
                if (sala.Plantado != grupoDerrotado) continue;

                sala.Plantado = 0;
                if (sala.Hecha) return true;

                sala.Hecha = true;

                Console.WriteLine($"[Sueños] Sala {sala.Id} limpiada; {sueno.DreamPoints} dream points.");
                return true;
            }

            return false;
        }

        /// <summary>Vuelve a mandar el estado del sueño, si es que hay uno.</summary>
        public static async Task RefrescarEstadoAsync(NetworkStream stream)
        {
            var sueno = Dreams.De(GameState.CharacterId);
            if (sueno == null) return;

            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.Push(Op.Izg, StateOf(sueno)));
        }

        /// <summary>The izg of a dream, with the bestiary of the room one stands in.</summary>
        private static byte[] StateOf(Dreams.Sueno sueno)
            => DreamProtocol.BuildDreamState(sueno, BestiaryOf(sueno.SalaActual));

        // ═══════════════════════════════════════════════════════════════════
        //  The bestiary
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// The monsters of a room as the fight will have them: each built by the fight's own
        /// <see cref="FightHandler.BuildMonsterFighter"/>, on the cell the fight will place it on.
        /// Empty when the room has no fight left to win.
        /// </summary>
        /// <remarks>
        /// What the real server lists are its dream's monsters, scaled to the dream's level --
        /// monster 209, 580 life points at its fifth grade, has 5,510 in the bestiary. The monsters
        /// here are the world group the room plants, at their own grades, and the bestiary shows
        /// them as they are: showing scaled figures for unscaled monsters would be a lie.
        /// </remarks>
        internal static List<DreamProtocol.Beast> BestiaryOf(Dreams.Sala? sala)
        {
            var beasts = new List<DreamProtocol.Beast>();
            if (sala == null || sala.Miembros.Count == 0 || sala.Hecha) return beasts;

            var group = MobSpawnManager.ComposeOffMap(sala.Miembros);
            if (group == null) return beasts;

            var cells = FightHandler.DefenderPlacement(sala.MapaDeLaSala);
            for (int i = 0; i < group.Members.Count; i++)
            {
                var member = group.Members[i];
                int cell = i < cells.Count ? cells[i] : cells.FirstOrDefault();
                var fighter = FightHandler.BuildMonsterFighter(member, -(i + 1), cell);
                beasts.Add(new DreamProtocol.Beast(cell, fighter.MonsterId, fighter.Level,
                                                   Dreams.IsBoss(fighter.MonsterId), StatsOf(fighter)));
            }
            return beasts;
        }

        /// <summary>
        /// A monster's characteristics as the bestiary lists them, in the order of the captures:
        /// life, AP, the five resistances that are not zero, initiative, evasion and lock, MP when
        /// it has some, and the two dodges. Evasion and lock are what the fight gives a monster --
        /// nothing, today -- and not a figure of their own.
        /// </summary>
        internal static List<(int Characteristic, int Value)> StatsOf(Fighter monster)
        {
            var stats = new List<(int, int)> { (LifePoints, monster.MaxHP), (ActionPoints, monster.MaxAP) };
            foreach (var (id, value) in new[]
                     {
                         (EarthResistance, monster.EarthResPct), (FireResistance, monster.FireResPct),
                         (WaterResistance, monster.WaterResPct), (AirResistance, monster.AirResPct),
                         (NeutralResistance, monster.NeutralResPct),
                     })
            {
                if (value != 0) stats.Add((id, value));
            }
            stats.Add((Initiative, monster.Initiative));
            stats.Add((Evasion, monster.Otras.GetValueOrDefault(Evasion)));
            stats.Add((Lock, monster.Otras.GetValueOrDefault(Lock)));
            if (monster.MaxMP != 0) stats.Add((MovementPoints, monster.MaxMP));
            stats.Add((EffectEngine.EsquivaPA, monster.Otras.GetValueOrDefault(EffectEngine.EsquivaPA)));
            stats.Add((EffectEngine.EsquivaPM, monster.Otras.GetValueOrDefault(EffectEngine.EsquivaPM)));
            return stats;
        }

        // The characteristics of datos/characteristics.json the bestiary lists.
        private const int LifePoints = 0;
        private const int ActionPoints = 1;
        private const int MovementPoints = 23;
        private const int EarthResistance = 33;
        private const int FireResistance = 34;
        private const int WaterResistance = 35;
        private const int AirResistance = 36;
        private const int NeutralResistance = 37;
        private const int Initiative = 44;
        private const int Evasion = 78;
        private const int Lock = 79;

        // ═══════════════════════════════════════════════════════════════════
        //  The fountain's shop
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// The Fontaine onirique used: its element let go (iwn, skill 355) and the state again,
        /// which carries the shop's offers in its f6.
        /// </summary>
        /// <remarks>
        /// What the real server answers is not captured -- the one capture at a fountain never
        /// touches it -- and the client has the offers from the izg of the room already. The
        /// element is let go the way every door and the well are, so the client does not keep it
        /// taken; the shop's window is the client's.
        /// </remarks>
        private static async Task ShopAsync(NetworkStream stream, Dreams.Sueno sueno, int elementId)
        {
            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.Push(Op.Iwn, ConnectionProtocol.BuildElementInUse(
                    elementId, Dreams.FountainSkill, GameState.CharacterId)));
            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.Push(Op.Izg, StateOf(sueno)));
            Console.WriteLine($"[Sueños] The fountain of room {sueno.Actual}: " +
                              $"{sueno.SalaActual?.Offers?.Count ?? 0} offer(s), {sueno.DreamPoints} dream points.");
        }

        // ═══════════════════════════════════════════════════════════════════
        //  The loot table and the positions
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>ixq: the loot table of the room one stands in, answered by izo.</summary>
        public static async Task DropTableAsync(NetworkStream stream, byte[] payload)
        {
            var sueno = Dreams.De(GameState.CharacterId);
            var drops = DropsOf(sueno?.SalaActual);
            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.Answer(Op.Izo, DreamProtocol.BuildDropTable(drops),
                                          ConnectionProtocol.RequestId(payload)));
            Console.WriteLine($"[Sueños] Loot table of room {sueno?.Actual}: {drops.Count} line(s).");
        }

        /// <summary>
        /// What the room's fight can drop, as the fight rolls it: the Jondo coin of every monster,
        /// always and added up, then each item of the monsters' own tables and of their global
        /// ones, at the best chance any of them gives, with its criterion when it has one.
        /// </summary>
        /// <remarks>
        /// The real table is the dream's own -- 61 lines of dream loot under dream criteria in
        /// the capture. The monsters here are the world's and drop what they drop in the world,
        /// and the table says so rather than promising the dream's.
        /// </remarks>
        internal static List<(string Criterion, int Item, int Quantity, double Percent)> DropsOf(Dreams.Sala? sala)
        {
            var lines = new List<(string Criterion, int Item, int Quantity, double Percent)>();
            if (sala == null || sala.Miembros.Count == 0) return lines;

            var group = MobSpawnManager.ComposeOffMap(sala.Miembros);
            if (group == null) return lines;

            int coins = 0;
            var best = new Dictionary<(string, int), double>();
            void Keep(string criterion, int item, double percent)
            {
                var key = (criterion ?? "", item);
                if (!best.TryGetValue(key, out double had) || percent > had) best[key] = percent;
            }

            foreach (var member in group.Members)
            {
                int monster = member.Monster?.Id ?? 0;
                coins += JondoCoin.RewardFor(member.Level);
                foreach (var drop in DatabaseManager.GetMonsterDrops(monster, member.GradeIndex))
                    Keep("", drop.ObjectId, drop.PercentDrop);
                foreach (var drop in DatabaseManager.GetMonsterGlobalDrops(monster))
                    Keep(drop.ReceiverCriterion, drop.ObjectId, drop.PercentDrop);
            }

            if (coins > 0) lines.Add(("", JondoCoin.TemplateId, coins, 100.0));
            foreach (var ((criterion, item), percent) in best.OrderByDescending(kv => kv.Value))
                lines.Add((criterion, item, 1, percent));
            return lines;
        }

        /// <summary>
        /// kaz: where a fight on this map would place everybody, answered by jxj -- the cells a
        /// fight here is placed from, computed as the fight computes them.
        /// </summary>
        public static async Task PositionsAsync(NetworkStream stream, byte[] payload)
        {
            long map = GameState.MapId;
            long arena = MapManager.ResolveArenaMapId(map);
            var (attackers, defenders) = FightHandler.Placement(map);
            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.Answer(Op.Jxj, DreamProtocol.BuildPositions(arena, map, attackers, defenders),
                                          ConnectionProtocol.RequestId(payload)));
            Console.WriteLine($"[Sueños] Positions of map {map}: {attackers.Count} and {defenders.Count} cells.");
        }

        /// <summary>
        /// iym: buying at the fountain. Its price off the dream points, its bonuses gained, and the
        /// izg again with what the shop has left.
        /// </summary>
        /// <remarks>
        /// INFERRED, NOT MEASURED: no capture buys anything -- the long one reaches a fountain
        /// and only talks to the Rey Gob. iym is the one request of the client's dream requests
        /// that carries a choice: the class that sends them (efs) sends six, four of them
        /// measured -- ixf entering, ixq the loot table, izh the storm, iyx leaving -- the fifth,
        /// iwt, is empty, and iym carries an int32 in f1 and a message in f2. The int is taken as
        /// the offer, by its reward id or by its place in the shop, and the frame is logged whole
        /// so the first real purchase says what it is. The answer to it is not known either: the
        /// izg that follows is what tells the client.
        /// </remarks>
        public static async Task BuyAsync(NetworkStream stream, byte[] payload)
        {
            byte[]? iym = ConnectionProtocol.ReadPayload(payload, Op.Iym);
            if (iym == null) return;
            Console.WriteLine($"[Sueños] iym (buy, inferred): {Convert.ToHexString(iym).ToLowerInvariant()}");

            var sueno = Dreams.De(GameState.CharacterId);
            if (sueno == null)
            {
                Console.WriteLine("[Sueños] iym with no dream going.");
                return;
            }

            int which = 0;
            foreach (var f in ProtoMessage.Parse(iym).Fields)
                if (f.FieldNumber == 1 && f.WireType == 0) which = (int)f.VarIntValue;

            var bought = Dreams.Buy(sueno, which, out string refusal);
            if (bought == null)
                Console.WriteLine($"[Sueños] Nothing bought with {which}: {refusal}.");
            else
                Console.WriteLine($"[Sueños] Bought reward {bought.Id} for {bought.Price}: " +
                                  $"{string.Join(", ", bought.Bonuses.Select(b => $"{b.Efecto} of {b.Valor}"))}; " +
                                  $"{sueno.DreamPoints} dream points left.");

            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.Push(Op.Izg, StateOf(sueno)));
        }

        // ═══════════════════════════════════════════════════════════════════
        //  La tormenta y la salida
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>La tormenta astral (izh): te mueve de sala sin pelear.</summary>
        /// <remarks>
        /// Medido: el cliente lo manda vacío y vuelven un izg, un jru y un izj «1001». Lo que la
        /// tormenta HACE no está medido —en la captura el jugador la usa y acaba en otro sitio—,
        /// así que aquí lleva a la primera salida de la sala en la que esté, que es lo que
        /// reproduce lo observable sin inventar reglas.
        /// </remarks>
        public static async Task AstralStormAsync(NetworkStream stream)
        {
            var sueno = Dreams.De(GameState.CharacterId);
            if (sueno == null) return;

            var actual = sueno.SalaActual;
            if (actual != null && actual.Salidas.Count > 0)
            {
                await EntrarEnSalaAsync(stream, sueno, actual.Salidas[0]);
            }

            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.Push(Op.Izj, DreamProtocol.BuildStorm()));

            Console.WriteLine($"[Sueños] Tormenta astral de {sueno.CharacterId}.");
        }

        /// <summary>Salir del sueño (iyx) y volver a donde se estaba.</summary>
        public static async Task LeaveAsync(NetworkStream stream, byte[] payload)
        {
            var sueno = Dreams.De(GameState.CharacterId);
            if (sueno == null) return;

            // A donde estaba ANTES DE PULSAR EL BOTON, no al mapa desde el que empezo el sueno:
            // a esas alturas ese mapa ya es el propio Plano Astral, y devolverlo alli lo dejaria
            // dando vueltas por el vestibulo.
            var (mapa, casilla) = Dreams.DeDondeViene(sueno.CharacterId);
            if (mapa == 0) { mapa = sueno.MapaDeVuelta; casilla = sueno.CasillaDeVuelta; }

            if (mapa != 0 && mapa != PlanoAstral)
            {
                await TeleportHandler.ToMapAsync(stream, mapa, casilla);
            }
            else
            {
                // Sin sitio conocido, al plano: es de donde se entro y siempre existe.
                await TeleportHandler.ToMapAsync(stream, PlanoAstral, 0);
            }

            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.Push(Op.Ixg));
            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.Push(Op.Iom));

            await Jondo.Protocol.NetworkMessage.WriteFrameAsync(stream,
                ConnectionProtocol.Answer(Op.Iyb, DreamProtocol.BuildLeft(),
                                          ConnectionProtocol.RequestId(payload)));

            Console.WriteLine($"[Sueños] {sueno.CharacterId} sale del sueño en la sala " +
                              $"{sueno.Actual} con {sueno.DreamPoints} dream point(s).");

            Dreams.Olvidar(sueno.CharacterId);
        }
    }
}
